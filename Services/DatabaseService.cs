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

    public async Task<bool> IsSteamIdInDatabaseAsync(string steamId2, int serverId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(steamId2))
            throw new ArgumentException("SteamID2 cannot be null or empty", nameof(steamId2));

        return await ExecuteWithRetryAsync(async () =>
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string query = "SELECT 1 FROM sb_bans WHERE authid = @authid AND RemoveType IS NULL AND (sid = 0 OR sid = @sid) LIMIT 1";
            await using var command = new MySqlCommand(query, connection);
            command.Parameters.Add("@authid", MySqlDbType.VarChar).Value = steamId2;
            command.Parameters.Add("@sid", MySqlDbType.Int32).Value = serverId;

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var exists = result != null && result != DBNull.Value;

            _logger.LogDebug("SteamID2 {SteamId2} active ban on server {ServerId}: {Exists}", steamId2, serverId, exists);

            return exists;
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
            command.Parameters.Add("@authid", MySqlDbType.VarChar).Value = banRecord.AuthId;
            command.Parameters.Add("@name", MySqlDbType.VarChar).Value = banRecord.Name;
            command.Parameters.Add("@created", MySqlDbType.Int64).Value = banRecord.Created;
            command.Parameters.Add("@ends", MySqlDbType.Int64).Value = banRecord.Ends;
            command.Parameters.Add("@length", MySqlDbType.Int32).Value = banRecord.Length;
            command.Parameters.Add("@sid", MySqlDbType.Int32).Value = banRecord.ServerId;
            command.Parameters.Add("@ip", MySqlDbType.VarChar).Value = banRecord.IpAddress ?? string.Empty;
            command.Parameters.Add("@reason", MySqlDbType.VarChar).Value = banRecord.Reason;

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

    public async Task<int> RemoveActiveBanAsync(string steamId2, int serverId, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(steamId2))
            throw new ArgumentException("SteamID2 cannot be null or empty", nameof(steamId2));

        return await ExecuteWithRetryAsync(async () =>
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string query = @"
                UPDATE sb_bans
                SET RemoveType = 'U', RemovedBy = 0, RemovedOn = @removedOn, ureason = @ureason
                WHERE authid = @authid AND RemoveType IS NULL AND (sid = 0 OR sid = @sid)";

            await using var command = new MySqlCommand(query, connection);
            command.Parameters.Add("@authid", MySqlDbType.VarChar).Value = steamId2;
            command.Parameters.Add("@sid", MySqlDbType.Int32).Value = serverId;
            command.Parameters.Add("@removedOn", MySqlDbType.Int32).Value = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            command.Parameters.Add("@ureason", MySqlDbType.Text).Value = reason ?? "Removed via Ban Sync file";

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

            if (rowsAffected > 0)
            {
                _logger.LogInformation("Marked {Count} active ban(s) as removed for {SteamId2} on server {ServerId}",
                    rowsAffected, steamId2, serverId);
            }
            else
            {
                _logger.LogDebug("No active ban found to remove for {SteamId2} on server {ServerId}",
                    steamId2, serverId);
            }

            return rowsAffected;
        }, $"RemoveActiveBan({steamId2})");
    }

    public async Task<IEnumerable<string>> GetActiveBanSteamIdsAsync(int serverId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string query = "SELECT DISTINCT authid FROM sb_bans WHERE RemoveType IS NULL AND (sid = 0 OR sid = @sid)";
            await using var command = new MySqlCommand(query, connection);
            command.Parameters.Add("@sid", MySqlDbType.Int32).Value = serverId;

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
