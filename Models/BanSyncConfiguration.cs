using System.ComponentModel.DataAnnotations;

namespace BBR_Ban_Sync.Models;

public class BanSyncConfiguration
{
    public const string SectionName = "BanSync";

    [Required]
    public string OutputFile { get; set; } = string.Empty;

    [Required]
    public string SteamAPIKey { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int ServerID { get; set; }

    public bool DebugMode { get; set; }

    [Range(1, 60)]
    public int SyncIntervalMinutes { get; set; } = 1;

    [Range(1, 24)]
    public int ReleaseCheckIntervalHours { get; set; } = 1;

    public bool FileWatcherEnabled { get; set; } = true;

    [Range(1, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    [Range(1, 60)]
    public int RetryDelaySeconds { get; set; } = 5;

    [Range(1, 1440)]
    public int CacheExpirationMinutes { get; set; } = 30;
}

public class DiscordConfiguration
{
    public const string SectionName = "Discord";

    public bool Enabled { get; set; }
    public List<string> WebhookUrls { get; set; } = new();
    public int EmbedColor { get; set; } = 16711680;
    public string ReclamationUrl { get; set; } = string.Empty;
}

public class GitHubConfiguration
{
    public const string SectionName = "GitHub";

    [Required]
    public string Owner { get; set; } = string.Empty;

    [Required]
    public string Repository { get; set; } = string.Empty;

    [Required]
    public string CurrentVersion { get; set; } = string.Empty;
}
