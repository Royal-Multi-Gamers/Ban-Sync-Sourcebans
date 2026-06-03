using BBR_Ban_Sync.Interfaces;
using BBR_Ban_Sync.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BBR_Ban_Sync.Services;

public class BanSyncService : BackgroundService
{
    private readonly IDatabaseService _databaseService;
    private readonly ISteamService _steamService;
    private readonly IDiscordService _discordService;
    private readonly IFileWatcherService _fileWatcherService;
    private readonly IGitHubService _gitHubService;
    private readonly ILogger<BanSyncService> _logger;
    private readonly BanSyncConfiguration _config;

    public BanSyncService(
        IDatabaseService databaseService,
        ISteamService steamService,
        IDiscordService discordService,
        IFileWatcherService fileWatcherService,
        IGitHubService gitHubService,
        ILogger<BanSyncService> logger,
        IOptions<BanSyncConfiguration> config)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _steamService = steamService ?? throw new ArgumentNullException(nameof(steamService));
        _discordService = discordService ?? throw new ArgumentNullException(nameof(discordService));
        _fileWatcherService = fileWatcherService ?? throw new ArgumentNullException(nameof(fileWatcherService));
        _gitHubService = gitHubService ?? throw new ArgumentNullException(nameof(gitHubService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));

        _fileWatcherService.OnNewLinesDetected += OnNewLinesDetected;
        _fileWatcherService.OnLinesRemoved += OnLinesRemoved;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BanSyncService starting...");

        try
        {
            if (!await _databaseService.TestConnectionAsync(stoppingToken))
            {
                _logger.LogError("Database connection test failed. Service cannot start.");
                return;
            }

            if (_config.DebugMode)
            {
                await _discordService.TestWebhookAsync(stoppingToken);
            }

            await SyncDatabaseToFileAsync(stoppingToken);

            if (_config.FileWatcherEnabled)
            {
                await _fileWatcherService.StartAsync(stoppingToken);
                _logger.LogInformation("File watcher started");
            }

            await CheckForNewReleaseAsync(stoppingToken);

            _logger.LogInformation("BanSyncService started successfully");

            var syncTask = RunPeriodicAsync(
                TimeSpan.FromMinutes(_config.SyncIntervalMinutes),
                SyncDatabaseToFileAsync,
                "sync",
                stoppingToken);

            var releaseTask = RunPeriodicAsync(
                TimeSpan.FromHours(_config.ReleaseCheckIntervalHours),
                CheckForNewReleaseAsync,
                "release-check",
                stoppingToken);

            var cacheTask = RunPeriodicAsync(
                TimeSpan.FromMinutes(_config.CacheExpirationMinutes),
                ct => { _steamService.CleanupExpiredCache(); return Task.CompletedTask; },
                "cache-cleanup",
                stoppingToken);

            await Task.WhenAll(syncTask, releaseTask, cacheTask);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("BanSyncService is stopping due to cancellation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in BanSyncService");
            throw;
        }
    }

    private async Task RunPeriodicAsync(TimeSpan interval, Func<CancellationToken, Task> action, string name, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await action(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in periodic task '{Name}'", name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BanSyncService stopping...");

        if (_config.FileWatcherEnabled)
        {
            await _fileWatcherService.StopAsync(cancellationToken);
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("BanSyncService stopped");
    }

    private async Task OnNewLinesDetected(IEnumerable<string> newLines)
    {
        var newLinesList = newLines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
        _logger.LogInformation("Processing {Count} new lines from file", newLinesList.Count);

        await Parallel.ForEachAsync(newLinesList,
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (line, ct) =>
            {
                try
                {
                    await ProcessNewSteamIdAsync(line, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing new SteamID: {SteamId}", line);
                }
            });
    }

    private Task OnLinesRemoved(IEnumerable<string> removedLines)
    {
        var count = removedLines.Count();
        _logger.LogInformation("Detected {Count} removed lines from file", count);
        return Task.CompletedTask;
    }

    private async Task ProcessNewSteamIdAsync(string steamId64, CancellationToken cancellationToken = default)
    {
        if (!_steamService.IsValidSteamId64(steamId64))
        {
            _logger.LogWarning("Invalid SteamID64 format: {SteamId64}", steamId64);
            return;
        }

        var steamId2 = _steamService.ConvertSteamId64ToSteamId2(steamId64);

        if (await _databaseService.IsSteamIdInDatabaseAsync(steamId2, _config.ServerID, cancellationToken))
        {
            if (_config.DebugMode)
                _logger.LogDebug("SteamID {SteamId64} already exists in database", steamId64);
            return;
        }

        var playerName = await _steamService.GetPlayerNameAsync(steamId64, cancellationToken);
        if (string.IsNullOrWhiteSpace(playerName))
        {
            _logger.LogWarning("Could not retrieve player name for SteamID64: {SteamId64}", steamId64);
            playerName = "Unknown Player";
        }

        var banRecord = new BanRecord
        {
            AuthId = steamId2,
            Name = playerName,
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Ends = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Length = 0,
            ServerId = _config.ServerID,
            IpAddress = ""
        };

        await _databaseService.AddBanRecordAsync(banRecord, cancellationToken);
        await _discordService.SendBanNotificationAsync(steamId64, playerName, cancellationToken);

        _logger.LogInformation("Successfully processed new ban for {PlayerName} ({SteamId64})", playerName, steamId64);
    }

    private async Task SyncDatabaseToFileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_config.DebugMode)
                _logger.LogDebug("Starting database to file synchronization");

            var currentSteamIds = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                currentSteamIds = new HashSet<string>(await _fileWatcherService.ReadFileAsync(cancellationToken), StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read current file content, treating as empty");
            }

            var activeBanSteamIds2 = await _databaseService.GetActiveBanSteamIdsAsync(_config.ServerID, cancellationToken);
            var activeBanSteamIds64 = new HashSet<string>(StringComparer.Ordinal);

            foreach (var steamId2 in activeBanSteamIds2)
            {
                try
                {
                    activeBanSteamIds64.Add(_steamService.ConvertSteamId2ToSteamId64(steamId2));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not convert SteamID2 to SteamID64: {SteamId2}", steamId2);
                }
            }

            if (currentSteamIds.SetEquals(activeBanSteamIds64))
            {
                if (_config.DebugMode)
                    _logger.LogDebug("No changes detected during sync");
                return;
            }

            await _fileWatcherService.WriteFileAsync(activeBanSteamIds64, cancellationToken);

            var newlyAddedIds = activeBanSteamIds64.Except(currentSteamIds).ToList();
            if (newlyAddedIds.Count > 0)
            {
                var players = await _steamService.GetPlayerInfoBatchAsync(newlyAddedIds, cancellationToken);
                var notifications = newlyAddedIds
                    .Where(id => players.ContainsKey(id) && !string.IsNullOrWhiteSpace(players[id].PersonaName))
                    .Select(id => (id, players[id].PersonaName))
                    .ToList();

                if (notifications.Count > 0)
                {
                    await _discordService.SendBulkBanNotificationAsync(notifications, cancellationToken);
                }
            }

            _logger.LogInformation("Synchronized {Count} active bans to file", activeBanSteamIds64.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during database to file synchronization");
        }
    }

    private async Task CheckForNewReleaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _gitHubService.IsNewVersionAvailableAsync(cancellationToken))
            {
                _logger.LogInformation("Application is up to date");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for new release");
        }
    }
}
