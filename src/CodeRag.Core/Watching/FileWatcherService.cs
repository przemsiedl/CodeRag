using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodeRag.Core.Watching;

public sealed class FileWatcherService : IHostedService, IDisposable
{
    private readonly IndexingPipeline _pipeline;
    private readonly string _watchPath;
    private readonly int _debounceMs;
    private readonly ILogger<FileWatcherService> _logger;
    private FileSystemWatcher? _watcher;
    private DebouncedQueue<string>? _changeQueue;
    private DebouncedQueue<string>? _deleteQueue;
    private CancellationToken _stoppingToken;

    public FileWatcherService(
        IndexingPipeline pipeline,
        string watchPath,
        int debounceMs,
        ILogger<FileWatcherService> logger)
    {
        _pipeline = pipeline;
        _watchPath = watchPath;
        _debounceMs = debounceMs;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingToken = cancellationToken;

        _changeQueue = new DebouncedQueue<string>(
            (key, path, ct) => _pipeline.IndexFileAsync(path, ct),
            _debounceMs);

        _deleteQueue = new DebouncedQueue<string>(
            (key, path, ct) => _pipeline.DeleteFileAsync(path, ct),
            _debounceMs);

        _watcher = new FileSystemWatcher(_watchPath)
        {
            Filter = "*.*",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            InternalBufferSize = 65536, // 64KB — default 8KB drops events in large repos
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Deleted += OnDeleted;
        _watcher.Error += OnError;

        _logger.LogInformation("Watching {Path}...", _watchPath);
        return Task.CompletedTask;
    }

    private void OnChanged(object _, FileSystemEventArgs e)
    {
        if (!_pipeline.IsIndexable(e.FullPath)) return;
        _logger.LogDebug("FSW event {Type}: {Path}", e.ChangeType, e.FullPath);
        _changeQueue?.Enqueue(e.FullPath, e.FullPath, _stoppingToken);
    }

    private void OnRenamed(object _, RenamedEventArgs e)
    {
        _logger.LogDebug("FSW event Renamed: {Old} -> {New}", e.OldFullPath, e.FullPath);
        if (_pipeline.IsIndexable(e.OldFullPath))
            _deleteQueue?.Enqueue(e.OldFullPath, e.OldFullPath, _stoppingToken);
        if (_pipeline.IsIndexable(e.FullPath))
            _changeQueue?.Enqueue(e.FullPath, e.FullPath, _stoppingToken);
    }

    private void OnDeleted(object _, FileSystemEventArgs e)
    {
        if (!_pipeline.IsIndexable(e.FullPath)) return;
        _logger.LogDebug("FSW event Deleted: {Path}", e.FullPath);
        _deleteQueue?.Enqueue(e.FullPath, e.FullPath, _stoppingToken);
    }

    private void OnError(object _, ErrorEventArgs e)
        => _logger.LogWarning(e.GetException(), "FileSystemWatcher error (buffer overflow?) — some changes may have been missed");

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _changeQueue?.Dispose();
        _deleteQueue?.Dispose();
    }
}
