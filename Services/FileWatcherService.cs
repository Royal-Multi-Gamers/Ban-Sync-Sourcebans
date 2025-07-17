using BBR_Ban_Sync.Interfaces;
using Microsoft.Extensions.Logging;

namespace BBR_Ban_Sync.Services;

public class FileWatcherService : IFileWatcherService, IDisposable
{
    private readonly string _filePath;
    private readonly ILogger<FileWatcherService> _logger;
    private FileSystemWatcher? _watcher;
    private List<string> _lastLines = new();
    private bool _isProcessing = false;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private bool _disposed = false;

    public event Func<IEnumerable<string>, Task>? OnNewLinesDetected;
    public event Func<IEnumerable<string>, Task>? OnLinesRemoved;

    public FileWatcherService(string filePath, ILogger<FileWatcherService> logger)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogError("File not found: {FilePath}", _filePath);
            throw new FileNotFoundException($"File not found: {_filePath}");
        }

        // Initialize with current file content
        _lastLines = new List<string>(await ReadFileAsync(cancellationToken));
        _logger.LogInformation("Initialized file watcher with {LineCount} lines from {FilePath}", _lastLines.Count, _filePath);

        // Setup file watcher
        var directory = Path.GetDirectoryName(_filePath);
        var fileName = Path.GetFileName(_filePath);

        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentException($"Invalid file path: {_filePath}");
        }

        _watcher = new FileSystemWatcher(directory)
        {
            Filter = fileName,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Error += OnWatcherError;

        _watcher.EnableRaisingEvents = true;

        _logger.LogInformation("File watcher started for: {FilePath}", _filePath);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileChanged;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;
        }

        // Wait for any ongoing processing to complete
        await _processingLock.WaitAsync(cancellationToken);
        _processingLock.Release();

        _logger.LogInformation("File watcher stopped for: {FilePath}", _filePath);
    }

    public async Task<IEnumerable<string>> ReadFileAsync(CancellationToken cancellationToken = default)
    {
        const int maxRetries = 5;
        const int retryDelayMs = 100;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                
                var content = await reader.ReadToEndAsync(cancellationToken);
                var lines = content.Split(new[] { Environment.NewLine, "\n", "\r\n" }, StringSplitOptions.None)
                                  .Where(line => !string.IsNullOrWhiteSpace(line))
                                  .ToList();

                _logger.LogDebug("Successfully read {LineCount} lines from file on attempt {Attempt}", lines.Count, attempt);
                return lines;
            }
            catch (IOException ex) when (attempt < maxRetries)
            {
                _logger.LogDebug("IOException on attempt {Attempt}/{MaxRetries}: {Message}", attempt, maxRetries, ex.Message);
                await Task.Delay(retryDelayMs * attempt, cancellationToken);
            }
        }

        _logger.LogError("Failed to read file after {MaxRetries} attempts: {FilePath}", maxRetries, _filePath);
        throw new IOException($"Failed to read file after {maxRetries} attempts: {_filePath}");
    }

    public async Task WriteFileAsync(IEnumerable<string> lines, CancellationToken cancellationToken = default)
    {
        if (lines == null)
            throw new ArgumentNullException(nameof(lines));

        var linesList = lines.ToList();

        try
        {
            // Temporarily disable file watcher to avoid triggering events during write
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
            }

            await File.WriteAllLinesAsync(_filePath, linesList, cancellationToken);
            
            // Update our internal state
            _lastLines = new List<string>(linesList);

            _logger.LogInformation("Successfully wrote {LineCount} lines to file: {FilePath}", linesList.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing to file: {FilePath}", _filePath);
            throw;
        }
        finally
        {
            // Re-enable file watcher
            if (_watcher != null)
            {
                await Task.Delay(500, cancellationToken); // Small delay to avoid immediate trigger
                _watcher.EnableRaisingEvents = true;
            }
        }
    }

    public bool IsFileAccessible()
    {
        try
        {
            using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        await ProcessFileChangeAsync(e);
    }

    private async void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        await ProcessFileChangeAsync(e);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "File watcher error for: {FilePath}", _filePath);
    }

    private async Task ProcessFileChangeAsync(FileSystemEventArgs e)
    {
        if (_isProcessing)
        {
            _logger.LogDebug("Ignoring file change event - already processing");
            return;
        }

        await _processingLock.WaitAsync();
        try
        {
            _isProcessing = true;

            _logger.LogDebug("File change detected: {ChangeType} - {FilePath}", e.ChangeType, e.FullPath);

            // Small delay to ensure file write is complete
            await Task.Delay(200);

            var currentLines = (await ReadFileAsync()).ToList();

            _logger.LogDebug("Comparing {CurrentCount} current lines with {LastCount} previous lines", 
                currentLines.Count, _lastLines.Count);

            var newLines = currentLines.Except(_lastLines).ToList();
            var removedLines = _lastLines.Except(currentLines).ToList();

            if (newLines.Any())
            {
                _logger.LogInformation("Detected {Count} new lines in file", newLines.Count);
                if (OnNewLinesDetected != null)
                {
                    await OnNewLinesDetected(newLines);
                }
            }

            if (removedLines.Any())
            {
                _logger.LogInformation("Detected {Count} removed lines in file", removedLines.Count);
                if (OnLinesRemoved != null)
                {
                    await OnLinesRemoved(removedLines);
                }
            }

            if (!newLines.Any() && !removedLines.Any())
            {
                _logger.LogDebug("No changes detected in file content");
            }

            _lastLines = currentLines;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file change for: {FilePath}", _filePath);
        }
        finally
        {
            _isProcessing = false;
            _processingLock.Release();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _watcher?.Dispose();
            _processingLock?.Dispose();
            _disposed = true;
        }
    }
}
