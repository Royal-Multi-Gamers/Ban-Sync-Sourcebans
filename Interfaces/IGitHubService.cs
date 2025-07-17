namespace BBR_Ban_Sync.Interfaces;

public interface IGitHubService
{
    Task<string?> CheckForNewReleaseAsync(CancellationToken cancellationToken = default);
    Task<bool> IsNewVersionAvailableAsync(CancellationToken cancellationToken = default);
}
