namespace BBR_Ban_Sync.Interfaces;

public interface IFileWatcherService
{
    event Func<IEnumerable<string>, Task> OnNewLinesDetected;
    event Func<IEnumerable<string>, Task> OnLinesRemoved;
    
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> ReadFileAsync(CancellationToken cancellationToken = default);
    Task WriteFileAsync(IEnumerable<string> lines, CancellationToken cancellationToken = default);
    bool IsFileAccessible();
}
