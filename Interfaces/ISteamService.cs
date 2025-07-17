using BBR_Ban_Sync.Models;

namespace BBR_Ban_Sync.Interfaces;

public interface ISteamService
{
    Task<string?> GetPlayerNameAsync(string steamId64, CancellationToken cancellationToken = default);
    Task<SteamPlayer?> GetPlayerInfoAsync(string steamId64, CancellationToken cancellationToken = default);
    string ConvertSteamId64ToSteamId2(string steamId64);
    string ConvertSteamId2ToSteamId64(string steamId2);
    bool IsValidSteamId64(string steamId64);
    bool IsValidSteamId2(string steamId2);
    void ClearCache();
}
