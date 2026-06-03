using BBR_Ban_Sync.Interfaces;
using BBR_Ban_Sync.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace BBR_Ban_Sync.Services;

public class SteamService : ISteamService
{
    private const long SteamId64Base = 76561197960265728L;
    private const int SteamApiBatchSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<SteamService> _logger;
    private readonly string _steamApiKey;
    private readonly ConcurrentDictionary<string, SteamPlayer> _playerCache = new();
    private readonly TimeSpan _cacheExpiration;

    public SteamService(HttpClient httpClient, ILogger<SteamService> logger, string steamApiKey, TimeSpan cacheExpiration)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _steamApiKey = steamApiKey ?? throw new ArgumentNullException(nameof(steamApiKey));
        _cacheExpiration = cacheExpiration;
    }

    public async Task<string?> GetPlayerNameAsync(string steamId64, CancellationToken cancellationToken = default)
    {
        var playerInfo = await GetPlayerInfoAsync(steamId64, cancellationToken);
        return playerInfo?.PersonaName;
    }

    public async Task<SteamPlayer?> GetPlayerInfoAsync(string steamId64, CancellationToken cancellationToken = default)
    {
        if (!IsValidSteamId64(steamId64))
        {
            _logger.LogWarning("Invalid SteamID64 format: {SteamId64}", steamId64);
            return null;
        }

        if (TryGetCached(steamId64, out var cached))
            return cached;

        var batch = await GetPlayerInfoBatchAsync(new[] { steamId64 }, cancellationToken);
        return batch.TryGetValue(steamId64, out var player) ? player : null;
    }

    public async Task<IReadOnlyDictionary<string, SteamPlayer>> GetPlayerInfoBatchAsync(IEnumerable<string> steamId64s, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, SteamPlayer>();
        var toFetch = new List<string>();

        foreach (var id in steamId64s.Distinct())
        {
            if (!IsValidSteamId64(id))
            {
                _logger.LogWarning("Skipping invalid SteamID64: {SteamId64}", id);
                continue;
            }

            if (TryGetCached(id, out var cached) && cached is not null)
                result[id] = cached;
            else
                toFetch.Add(id);
        }

        foreach (var chunk in toFetch.Chunk(SteamApiBatchSize))
        {
            var fetched = await FetchBatchAsync(chunk, cancellationToken);
            foreach (var kvp in fetched)
                result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    private async Task<Dictionary<string, SteamPlayer>> FetchBatchAsync(IReadOnlyCollection<string> steamIds, CancellationToken cancellationToken)
    {
        var fetched = new Dictionary<string, SteamPlayer>();
        var ids = string.Join(',', steamIds);
        var url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={_steamApiKey}&steamids={ids}";

        try
        {
            _logger.LogDebug("Fetching {Count} Steam player(s) in batch", steamIds.Count);

            using var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Steam API rate limit exceeded (batch size {Count})", steamIds.Count);
                return fetched;
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var apiResponse = await JsonSerializer.DeserializeAsync<SteamApiResponse>(stream, JsonOptions, cancellationToken);

            if (apiResponse?.Response?.Players is null)
                return fetched;

            foreach (var summary in apiResponse.Response.Players)
            {
                if (string.IsNullOrWhiteSpace(summary.SteamId))
                    continue;

                var player = new SteamPlayer
                {
                    SteamId64 = summary.SteamId,
                    SteamId2 = ConvertSteamId64ToSteamId2(summary.SteamId),
                    PersonaName = summary.PersonaName ?? "Unknown",
                    LastUpdated = DateTime.UtcNow
                };

                _playerCache[summary.SteamId] = player;
                fetched[summary.SteamId] = player;
            }
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("429"))
        {
            _logger.LogWarning("Steam API rate limit exceeded (batch size {Count})", steamIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Steam batch (size {Count})", steamIds.Count);
        }

        return fetched;
    }

    private bool TryGetCached(string steamId64, out SteamPlayer? player)
    {
        if (_playerCache.TryGetValue(steamId64, out var cached))
        {
            if (DateTime.UtcNow - cached.LastUpdated < _cacheExpiration)
            {
                player = cached;
                return true;
            }
            _playerCache.TryRemove(steamId64, out _);
        }

        player = null;
        return false;
    }

    public string ConvertSteamId64ToSteamId2(string steamId64)
    {
        if (!long.TryParse(steamId64, out long id))
            throw new ArgumentException("Invalid SteamID64 format", nameof(steamId64));

        long z = (id - SteamId64Base) / 2;
        int y = (int)((id - SteamId64Base) % 2);
        return $"STEAM_0:{y}:{z}";
    }

    public string ConvertSteamId2ToSteamId64(string steamId2)
    {
        var parts = steamId2.Split(':');
        if (parts.Length != 3 || !parts[0].Equals("STEAM_0", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid SteamID2 format", nameof(steamId2));

        if (!int.TryParse(parts[1], out var y) || !long.TryParse(parts[2], out var z))
            throw new ArgumentException("Invalid SteamID2 format", nameof(steamId2));

        return (SteamId64Base + z * 2 + y).ToString();
    }

    public bool IsValidSteamId64(string steamId64)
    {
        if (string.IsNullOrWhiteSpace(steamId64))
            return false;

        return long.TryParse(steamId64, out long id) && id > SteamId64Base;
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

        return long.TryParse(parts[2], out var z) && z >= 0;
    }

    public void ClearCache()
    {
        _playerCache.Clear();
        _logger.LogInformation("Steam player cache cleared");
    }

    public void CleanupExpiredCache()
    {
        var now = DateTime.UtcNow;
        var removed = 0;
        foreach (var kvp in _playerCache)
        {
            if (now - kvp.Value.LastUpdated > _cacheExpiration && _playerCache.TryRemove(kvp.Key, out _))
                removed++;
        }

        if (removed > 0)
            _logger.LogDebug("Cleaned up {Count} expired cache entries", removed);
    }
}
