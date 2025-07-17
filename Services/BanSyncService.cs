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
    private readonly Timer _syncTimer;
    private readonly Timer _releaseCheckTimer;
    private readonly Timer _cacheCleanupTimer;

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

        // Setup timers
        _syncTimer = new Timer(SyncDatabaseToFileCallback, null, Timeout.Infinite, Timeout.Infinite);
        _releaseCheckTimer = new Timer(CheckForNewReleaseCallback, null, Timeout.Infinite, Timeout.Infinite);
        _cacheCleanupTimer = new Timer(CleanupCacheCallback, null, Timeout.Infinite, Timeout.Infinite);

        // Subscribe to file watcher events
        _fileWatcherService.OnNewLinesDetected += OnNewLinesDetected;
        _fileWatcherService.OnLinesRemoved += OnLinesRemoved;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BanSyncService starting...");

        try
        {
            // Test database connection
            if (!await _databaseService.TestConnectionAsync(stoppingToken))
            {
                _logger.LogError("Database connection test failed. Service cannot start.");
                return;
            }

            // Test Discord webhook if enabled
            if (_config.DebugMode)
            {
                await _discordService.TestWebhookAsync(stoppingToken);
            }

            // Initial sync
            await SyncDatabaseToFileAsync(stoppingToken);

            // Start file watcher if enabled
            if (_config.FileWatcherEnabled)
            {
                await _fileWatcherService.StartAsync(stoppingToken);
                _logger.LogInformation("File watcher started");
            }

            // Start timers
            var syncInterval = TimeSpan.FromMinutes(_config.SyncIntervalMinutes);
            var releaseCheckInterval = TimeSpan.FromHours(_config.ReleaseCheckIntervalHours);
            var cacheCleanupInterval = TimeSpan.FromMinutes(_config.CacheExpirationMinutes);

            _syncTimer.Change(syncInterval, syncInterval);
            _releaseCheckTimer.Change(releaseCheckInterval, releaseCheckInterval);
            _cacheCleanupTimer.Change(cacheCleanupInterval, cacheCleanupInterval);

            _logger.LogInformation("BanSyncService started successfully");

            // Check for new release on startup
            await CheckForNewReleaseAsync(stoppingToken);

            // Keep the service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BanSyncService stopping...");

        // Stop timers
        await _syncTimer.DisposeAsync();
        await _releaseCheckTimer.DisposeAsync();
        await _cacheCleanupTimer.DisposeAsync();

        // Stop file watcher
        if (_config.FileWatcherEnabled)
        {
            await _fileWatcherService.StopAsync(cancellationToken);
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("BanSyncService stopped");
    }

    private async Task OnNewLinesDetected(IEnumerable<string> newLines)
    {
        var newLinesList = newLines.ToList();
        _logger.LogInformation("Processing {Count} new lines from file", newLinesList.Count);

        foreach (var line in newLinesList)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                await ProcessNewSteamIdAsync(line.Trim());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing new SteamID: {SteamId}", line);
            }
        }
    }

    private Task OnLinesRemoved(IEnumerable<string> removedLines)
    {
        var removedLinesList = removedLines.ToList();
        _logger.LogInformation("Detected {Count} removed lines from file", removedLinesList.Count);
        
        // Note: In the original implementation, removed lines weren't processed
        // This could be extended to handle unbans if needed
        
        return Task.CompletedTask;
    }

    private async Task ProcessNewSteamIdAsync(string steamId64)
    {
        if (!_steamService.IsValidSteamId64(steamId64))
        {
            _logger.LogWarning("Invalid SteamID64 format: {SteamId64}", steamId64);
            return;
        }

        var steamId2 = _steamService.ConvertSteamId64ToSteamId2(steamId64);

        // Check if already in database
        if (await _databaseService.IsSteamIdInDatabaseAsync(steamId2))
        {
            if (_config.DebugMode)
            {
                _logger.LogDebug("SteamID {SteamId64} already exists in database", steamId64);
            }
            return;
        }

        // Get player name
        var playerName = await _steamService.GetPlayerNameAsync(steamId64);
        if (string.IsNullOrWhiteSpace(playerName))
        {
            _logger.LogWarning("Could not retrieve player name for SteamID64: {SteamId64}", steamId64);
            playerName = "Unknown Player";
        }

        // Create ban record
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

        // Add to database
        await _databaseService.AddBanRecordAsync(banRecord);

        // Send Discord notification
        await _discordService.SendBanNotificationAsync(steamId64, playerName);

        _logger.LogInformation("Successfully processed new ban for {PlayerName} ({SteamId64})", playerName, steamId64);
    }

    private async Task SyncDatabaseToFileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_config.DebugMode)
            {
                _logger.LogDebug("Starting database to file synchronization");
            }

            // Get current file content
            var currentSteamIds = new HashSet<string>();
            try
            {
                currentSteamIds = new HashSet<string>(await _fileWatcherService.ReadFileAsync(cancellationToken));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read current file content, treating as empty");
            }

            // Get active bans from database
            var activeBanSteamIds2 = await _databaseService.GetActiveBanSteamIdsAsync(_config.ServerID, cancellationToken);
            var activeBanSteamIds64 = new HashSet<string>();

            foreach (var steamId2 in activeBanSteamIds2)
            {
                try
                {
                    var steamId64 = _steamService.ConvertSteamId2ToSteamId64(steamId2);
                    activeBanSteamIds64.Add(steamId64);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not convert SteamID2 to SteamID64: {SteamId2}", steamId2);
                }
            }

            // Check if there are changes
            if (currentSteamIds.SetEquals(activeBanSteamIds64))
            {
                if (_config.DebugMode)
                {
                    _logger.LogDebug("No changes detected during sync");
                }
                return;
            }

            // Write updated content to file
            await _fileWatcherService.WriteFileAsync(activeBanSteamIds64, cancellationToken);

            // Find newly added SteamIDs for Discord notifications
            var newlyAddedIds = activeBanSteamIds64.Except(currentSteamIds).ToList();
            if (newlyAddedIds.Any())
            {
                var notifications = new List<(string steamId64, string playerName)>();

                foreach (var steamId64 in newlyAddedIds)
                {
                    var playerName = await _steamService.GetPlayerNameAsync(steamId64, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(playerName))
                    {
                        notifications.Add((steamId64, playerName));
                    }
                }

                if (notifications.Any())
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
            var isNewVersionAvailable = await _gitHubService.IsNewVersionAvailableAsync(cancellationToken);
            if (isNewVersionAvailable)
            {
                var latestVersion = await _gitHubService.CheckForNewReleaseAsync(cancellationToken);
                _logger.LogInformation("New version available: {LatestVersion}", latestVersion);
            }
            else
            {
                _logger.LogInformation("Application is up to date");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for new release");
        }
    }

    private void SyncDatabaseToFileCallback(object? state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await SyncDatabaseToFileAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in sync timer callback");
            }
        });
    }

    private void CheckForNewReleaseCallback(object? state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await CheckForNewReleaseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in release check timer callback");
            }
        });
    }

    private void CleanupCacheCallback(object? state)
    {
        _ = Task.Run(() =>
        {
            try
            {
                if (_steamService is SteamService steamService)
                {
                    steamService.CleanupExpiredCache();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in cache cleanup timer callback");
            }
        });
    }

    public override void Dispose()
    {
        _syncTimer?.Dispose();
        _releaseCheckTimer?.Dispose();
        _cacheCleanupTimer?.Dispose();
        base.Dispose();
    }
}
