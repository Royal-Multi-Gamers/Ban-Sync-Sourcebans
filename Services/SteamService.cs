using BBR_Ban_Sync.Interfaces;
using BBR_Ban_Sync.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace BBR_Ban_Sync.Services;

public class SteamService : ISteamService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SteamService> _logger;
    private readonly string _steamApiKey;
    private readonly ConcurrentDictionary<string, SteamPlayer> _playerCache;
    private readonly TimeSpan _cacheExpiration;

    public SteamService(HttpClient httpClient, ILogger<SteamService> logger, string steamApiKey, TimeSpan cacheExpiration)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _steamApiKey = steamApiKey ?? throw new ArgumentNullException(nameof(steamApiKey));
        _cacheExpiration = cacheExpiration;
        _playerCache = new ConcurrentDictionary<string, SteamPlayer>();
    }

    public async Task<string?> GetPlayerNameAsync(string steamId64, CancellationToken cancellationToken = default)
    {
        var playerInfo = await GetPlayerInfoAsync(steamId64, cancellationToken);
        return playerInfo?.PersonaName;
    }

    public async Task<SteamPlayer?> GetPlayerInfoAsync(string steamId64, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(steamId64))
        {
            _logger.LogWarning("SteamID64 is null or empty");
            return null;
        }

        if (!IsValidSteamId64(steamId64))
        {
            _logger.LogWarning("Invalid SteamID64 format: {SteamId64}", steamId64);
            return null;
        }

        // Check cache first
        if (_playerCache.TryGetValue(steamId64, out var cachedPlayer))
        {
            if (DateTime.UtcNow - cachedPlayer.LastUpdated < _cacheExpiration)
            {
                _logger.LogDebug("Retrieved player info from cache for SteamID64: {SteamId64}", steamId64);
                return cachedPlayer;
            }
            else
            {
                // Remove expired entry
                _playerCache.TryRemove(steamId64, out _);
            }
        }

        try
        {
            var url = $"http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={_steamApiKey}&steamids={steamId64}";
            
            _logger.LogDebug("Fetching player info from Steam API for SteamID64: {SteamId64}", steamId64);

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Steam API rate limit exceeded for SteamID64: {SteamId64}", steamId64);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<SteamApiResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (apiResponse?.Response?.Players == null || !apiResponse.Response.Players.Any())
            {
                _logger.LogWarning("No player found for SteamID64: {SteamId64}", steamId64);
                return null;
            }

            var playerSummary = apiResponse.Response.Players.First();
            var steamPlayer = new SteamPlayer
            {
                SteamId64 = steamId64,
                SteamId2 = ConvertSteamId64ToSteamId2(steamId64),
                PersonaName = playerSummary.PersonaName ?? "Unknown",
                LastUpdated = DateTime.UtcNow
            };

            // Cache the result
            _playerCache.TryAdd(steamId64, steamPlayer);

            _logger.LogDebug("Successfully retrieved player info for {PersonaName} ({SteamId64})", 
                steamPlayer.PersonaName, steamId64);

            return steamPlayer;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("429"))
        {
            _logger.LogWarning("Steam API rate limit exceeded for SteamID64: {SteamId64}", steamId64);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching player info for SteamID64: {SteamId64}", steamId64);
            return null;
        }
    }

    public string ConvertSteamId64ToSteamId2(string steamId64)
    {
        if (!long.TryParse(steamId64, out long steamId64Long))
        {
            throw new ArgumentException("Invalid SteamID64 format", nameof(steamId64));
        }

        long z = (steamId64Long - 76561197960265728) / 2;
        int y = (steamId64Long - 76561197960265728) % 2 == 0 ? 0 : 1;

        return $"STEAM_0:{y}:{z}";
    }

    public string ConvertSteamId2ToSteamId64(string steamId2)
    {
        var parts = steamId2.Split(':');
        if (parts.Length != 3 || !parts[0].Equals("STEAM_0", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid SteamID2 format", nameof(steamId2));
        }

        if (!int.TryParse(parts[1], out var y) || !long.TryParse(parts[2], out var z))
        {
            throw new ArgumentException("Invalid SteamID2 format", nameof(steamId2));
        }

        var steamId64 = 76561197960265728 + z * 2 + y;
        return steamId64.ToString();
    }

    public bool IsValidSteamId64(string steamId64)
    {
        if (string.IsNullOrWhiteSpace(steamId64))
            return false;

        if (!long.TryParse(steamId64, out long steamId64Long))
            return false;

        // SteamID64 should be in the valid range
        return steamId64Long >= 76561197960265729 && steamId64Long <= 76561202255233023;
    }

    public bool IsValidSteamId2(string steamId2)
    {
        if (string.IsNullOrWhiteSpace(steamId2))
            return false;

        var parts = steamId2.Split(':');
        if (parts.Length != 3)
            return false;

        if (!parts[0].Equals("STEAM_0", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!int.TryParse(parts[1], out var y) || (y != 0 && y != 1))
            return false;

        if (!long.TryParse(parts[2], out var z) || z < 0)
            return false;

        return true;
    }

    public void ClearCache()
    {
        _playerCache.Clear();
        _logger.LogInformation("Steam player cache cleared");
    }

    // Clean up expired cache entries periodically
    public void CleanupExpiredCache()
    {
        var expiredKeys = _playerCache
            .Where(kvp => DateTime.UtcNow - kvp.Value.LastUpdated > _cacheExpiration)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _playerCache.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired cache entries", expiredKeys.Count);
        }
    }
}
