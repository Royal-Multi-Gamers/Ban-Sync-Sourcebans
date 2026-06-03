using BBR_Ban_Sync.Interfaces;
using BBR_Ban_Sync.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BBR_Ban_Sync.Services;

public class GitHubService : IGitHubService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string CurrentAssemblyVersion = ResolveAssemblyVersion();

    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubService> _logger;
    private readonly GitHubConfiguration _config;

    public GitHubService(HttpClient httpClient, ILogger<GitHubService> logger, IOptions<GitHubConfiguration> config)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"BBR-Ban-Sync/{CurrentVersion}");
        }
    }

    private string CurrentVersion =>
        string.IsNullOrWhiteSpace(_config.CurrentVersion) ? CurrentAssemblyVersion : _config.CurrentVersion;

    public async Task<string?> CheckForNewReleaseAsync(CancellationToken cancellationToken = default)
    {
        var release = await FetchLatestReleaseAsync(cancellationToken);
        return release?.TagName;
    }

    public async Task<bool> IsNewVersionAvailableAsync(CancellationToken cancellationToken = default)
    {
        var release = await FetchLatestReleaseAsync(cancellationToken);
        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            return false;

        var current = NormalizeVersion(CurrentVersion);
        var latest = NormalizeVersion(release.TagName);

        bool isNewer;
        if (Version.TryParse(current, out var currentVer) && Version.TryParse(latest, out var latestVer))
        {
            isNewer = latestVer > currentVer;
        }
        else
        {
            isNewer = !string.Equals(release.TagName, CurrentVersion, StringComparison.OrdinalIgnoreCase);
        }

        if (isNewer)
        {
            _logger.LogInformation("New version available: {LatestVersion} (current: {CurrentVersion}) - {Url}",
                release.TagName, CurrentVersion, release.HtmlUrl);
        }
        else
        {
            _logger.LogDebug("Up to date. Current: {CurrentVersion}, latest: {LatestVersion}",
                CurrentVersion, release.TagName);
        }

        return isNewer;
    }

    private async Task<GitHubRelease?> FetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://api.github.com/repos/{_config.Owner}/{_config.Repository}/releases/latest";

            _logger.LogDebug("Checking for new release at: {Url}", url);

            using var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to check for new release. Status: {StatusCode}", response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                _logger.LogWarning("Could not parse latest release from GitHub API response");
                return null;
            }

            return release;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for new release from GitHub");
            return null;
        }
    }

    private static string NormalizeVersion(string version)
    {
        var trimmed = version.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            trimmed = trimmed[1..];
        return trimmed;
    }

    private static string ResolveAssemblyVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // strip "+commitsha" suffix added by SourceLink
            var plus = info.IndexOf('+');
            if (plus >= 0) info = info[..plus];
            return info!;
        }

        return asm.GetName().Version?.ToString() ?? "0.0.0";
    }
}

internal class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }
    public string? Name { get; set; }
    public bool Draft { get; set; }
    public bool Prerelease { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime PublishedAt { get; set; }
    public string? Body { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
}
