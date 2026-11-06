using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Registry;

/// <summary>
/// Reloads the <see cref="IFeedRegistry"/> when the configuration file is changed by another process
/// (for example a CLI <c>add-feed</c> while the GUI is running). Bursts of file events are coalesced.
/// </summary>
public sealed class FeedRegistryConfigWatcher : IDisposable
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(750);

    private readonly IFeedRegistry _registry;
    private readonly ILogger<FeedRegistryConfigWatcher> _logger;
    private readonly TimeSpan _debounce;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly SemaphoreSlim _reloadGate = new SemaphoreSlim(1, 1);
    private bool _disposed;

    public FeedRegistryConfigWatcher(
        string configDirectory,
        string configFileName,
        IFeedRegistry registry,
        ILogger<FeedRegistryConfigWatcher> logger,
        TimeSpan? debounce = null
    )
    {
        _registry = registry;
        _logger = logger;
        _debounce = debounce ?? DefaultDebounce;

        Directory.CreateDirectory(configDirectory);
        _timer = new Timer(
            OnDebounceElapsed,
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan
        );
        _watcher = new FileSystemWatcher(configDirectory, configFileName)
        {
            NotifyFilter =
                NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
        };
        _watcher.Changed += OnConfigFileEvent;
        _watcher.Created += OnConfigFileEvent;
        _watcher.Renamed += OnConfigFileEvent;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnConfigFileEvent(object sender, FileSystemEventArgs e)
    {
        if (!_disposed)
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
    }

    private void OnDebounceElapsed(object? state)
    {
        if (_disposed)
            return;

        ReloadAsync()
            .ContinueWith(
                task =>
                    _logger.LogWarning(
                        task.Exception,
                        "Feed registry reload after external configuration change failed"
                    ),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );
    }

    private async Task ReloadAsync()
    {
        await _reloadGate.WaitAsync();
        try
        {
            _logger.LogInformation("Configuration file changed on disk, reloading feed registry");
            await _registry.ReloadAsync();
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher.Dispose();
        _timer.Dispose();
    }
}
