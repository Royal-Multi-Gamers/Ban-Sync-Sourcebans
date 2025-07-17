namespace BBR_Ban_Sync.Interfaces;

public interface IDiscordService
{
    Task SendBanNotificationAsync(string steamId64, string playerName, CancellationToken cancellationToken = default);
    Task SendBulkBanNotificationAsync(IEnumerable<(string steamId64, string playerName)> bans, CancellationToken cancellationToken = default);
    Task<bool> TestWebhookAsync(CancellationToken cancellationToken = default);
}
