using BBR_Ban_Sync.Models;

namespace BBR_Ban_Sync.Interfaces;

public interface IDatabaseService
{
    Task<bool> IsSteamIdInDatabaseAsync(string steamId2, CancellationToken cancellationToken = default);
    Task AddBanRecordAsync(BanRecord banRecord, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> GetActiveBanSteamIdsAsync(int serverId, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
