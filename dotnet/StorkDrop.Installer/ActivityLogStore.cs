using System.Text.Json;
using System.Text.Json.Serialization;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

public sealed class ActivityLogStore : IActivityLog, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private List<ActivityLogEntry>? _entries;
    private const int MaxEntries = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ActivityLogStore()
        : this(StorkPaths.ActivityLogFile) { }

    public ActivityLogStore(string filePath)
    {
        _filePath = filePath;
        string? directory = Path.GetDirectoryName(_filePath);
        if (directory is not null)
            Directory.CreateDirectory(directory);
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_entries is not null)
            return;

        await LoadFromDiskAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadFromDiskAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_filePath))
        {
            string json = await File.ReadAllTextAsync(_filePath, cancellationToken)
                .ConfigureAwait(false);
            List<ActivityLogEntry>? deserialized = JsonSerializer.Deserialize<
                List<ActivityLogEntry>
            >(json, JsonOptions);
            _entries = deserialized ?? [];
        }
        else
        {
            _entries = [];
        }
    }

    public async Task LogAsync(
        ActivityLogEntry entry,
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            _entries!.Insert(0, entry);
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
            }

            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ActivityLogEntry>> GetEntriesAsync(
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            return _entries!.OrderByDescending(e => e.Timestamp).Skip(offset).Take(limit).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ActivityLogEntry>> GetEntriesByProductAsync(
        string productId,
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            return _entries!
                .Where(e => e.ProductId == productId)
                .OrderByDescending(e => e.Timestamp)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            _entries!.Clear();
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(_entries, JsonOptions);
        await SafeFileWriter
            .WriteAtomicAsync(_filePath, json, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
