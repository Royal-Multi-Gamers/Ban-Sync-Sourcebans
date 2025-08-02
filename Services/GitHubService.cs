using BBR_Ban_Sync.Interfaces;
using BBR_Ban_Sync.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BBR_Ban_Sync.Services;

public class GitHubService : IGitHubService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubService> _logger;

    private readonly string _owner = "Royal-Multi-Gamers";
    private readonly string _repository = "Ban-Sync-Sourcebans";
    private readonly string _currentVersion = "v0.0.6";

    public GitHubService(HttpClient httpClient, ILogger<GitHubService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Set User-Agent header required by GitHub API
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "BBR-Ban-Sync");
        }
    }

    public async Task<string?> CheckForNewReleaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{_owner}/{_repository}/releases/latest";
            
            _logger.LogDebug("Checking for new release at: {Url}", url);

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to check for new release. Status: {StatusCode}", response.StatusCode);
                return null;
            }

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var releaseInfo = JsonSerializer.Deserialize<GitHubRelease>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var latestVersion = releaseInfo?.TagName;

            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                _logger.LogWarning("Could not parse latest version from GitHub API response");
                return null;
            }

            _logger.LogDebug("Latest version from GitHub: {LatestVersion}, Current version: {CurrentVersion}", 
                latestVersion, _currentVersion);

            return latestVersion;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for new release from GitHub");
            return null;
        }
    }

    public async Task<bool> IsNewVersionAvailableAsync(CancellationToken cancellationToken = default)
    {
        var latestVersion = await CheckForNewReleaseAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            return false;
        }

        var isNewVersion = !string.Equals(latestVersion, _currentVersion, StringComparison.OrdinalIgnoreCase);

        if (isNewVersion)
        {
            _logger.LogInformation("New version available: {LatestVersion} (current: {CurrentVersion})", 
                latestVersion, _currentVersion);
        }
        else
        {
            _logger.LogDebug("No new version available. Current version {CurrentVersion} is up to date", 
                _currentVersion);
        }

        return isNewVersion;
    }
}

// GitHub API response models
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
    public string? HtmlUrl { get; set; }
}
