using BBR_Ban_Sync.Interfaces;
using BBR_Ban_Sync.Models;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace BBR_Ban_Sync.Services;

public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseService> _logger;
    private readonly int _maxRetryAttempts;
    private readonly TimeSpan _retryDelay;

    public DatabaseService(string connectionString, ILogger<DatabaseService> logger, int maxRetryAttempts = 3, int retryDelaySeconds = 5)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxRetryAttempts = maxRetryAttempts;
        _retryDelay = TimeSpan.FromSeconds(retryDelaySeconds);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName)
    {
        Exception? lastException = null;

        for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (ex is MySqlException or TimeoutException)
            {
                lastException = ex;
                _logger.LogWarning("Database operation '{OperationName}' failed on attempt {Attempt}/{MaxAttempts}. Error: {Error}",
                    operationName, attempt, _maxRetryAttempts, ex.Message);

                if (attempt < _maxRetryAttempts)
                {
                    await Task.Delay(_retryDelay);
                }
            }
        }

        _logger.LogError(lastException, "Database operation '{OperationName}' failed after {MaxAttempts} attempts", operationName, _maxRetryAttempts);
        throw lastException!;
    }

    public async Task<bool> IsSteamIdInDatabaseAsync(string steamId2, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(steamId2))
            throw new ArgumentException("SteamID2 cannot be null or empty", nameof(steamId2));

        return await ExecuteWithRetryAsync(async () =>
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string query = "SELECT COUNT(*) FROM sb_bans WHERE authid = @authid AND RemoveType IS NULL";
            await using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@authid", steamId2);

            _logger.LogDebug("Executing query: {Query} with SteamID2: {SteamId2}", query, steamId2);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var count = Convert.ToInt64(result ?? 0L);

            _logger.LogDebug("Found {Count} active bans for SteamID2: {SteamId2}", count, steamId2);

            return count > 0;
        }, $"IsSteamIdInDatabase({steamId2})");
    }

    public async Task AddBanRecordAsync(BanRecord banRecord, CancellationToken cancellationToken = default)
    {
        if (banRecord == null)
            throw new ArgumentNullException(nameof(banRecord));

        if (string.IsNullOrWhiteSpace(banRecord.AuthId))
            throw new ArgumentException("AuthId cannot be null or empty", nameof(banRecord));

        await ExecuteWithRetryAsync(async () =>
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string query = @"
                INSERT INTO sb_bans (authid, name, created, ends, length, sid, ip, reason) 
                VALUES (@authid, @name, @created, @ends, @length, @sid, @ip, @reason)";

            await using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@authid", banRecord.AuthId);
            command.Parameters.AddWithValue("@name", banRecord.Name);
            command.Parameters.AddWithValue("@created", banRecord.Created);
            command.Parameters.AddWithValue("@ends", banRecord.Ends);
            command.Parameters.AddWithValue("@length", banRecord.Length);
            command.Parameters.AddWithValue("@sid", banRecord.ServerId);
            command.Parameters.AddWithValue("@ip", banRecord.IpAddress);
            command.Parameters.AddWithValue("@reason", banRecord.Reason);

            _logger.LogDebug("Executing ban insert query for SteamID2: {SteamId2}, Name: {Name}", 
                banRecord.AuthId, banRecord.Name);

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

            if (rowsAffected > 0)
            {
                _logger.LogInformation("Successfully added ban record for {Name} ({SteamId2})", 
                    banRecord.Name, banRecord.AuthId);
            }
            else
            {
                _logger.LogWarning("No rows affected when adding ban record for {SteamId2}", banRecord.AuthId);
            }

            return rowsAffected;
        }, $"AddBanRecord({banRecord.AuthId})");
    }

    public async Task<IEnumerable<string>> GetActiveBanSteamIdsAsync(int serverId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string query = "SELECT DISTINCT authid FROM sb_bans WHERE RemoveType IS NULL AND (sid = 0 OR sid = @sid)";
            await using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@sid", serverId);

            _logger.LogDebug("Executing query to get active bans for server ID: {ServerId}", serverId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var steamIds = new List<string>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var steamId2 = reader.GetString("authid");
                if (!string.IsNullOrWhiteSpace(steamId2))
                {
                    steamIds.Add(steamId2);
                }
            }

            _logger.LogDebug("Retrieved {Count} active ban SteamIDs from database", steamIds.Count);

            return (IEnumerable<string>)steamIds;
        }, $"GetActiveBanSteamIds({serverId})");
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                const string query = "SELECT 1";
                await using var command = new MySqlCommand(query, connection);
                await command.ExecuteScalarAsync(cancellationToken);

                _logger.LogInformation("Database connection test successful");
                return true;
            }, "TestConnection");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database connection test failed");
            return false;
        }
    }
}
