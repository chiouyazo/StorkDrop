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
    private FileStamp _loadedStamp;
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

    private async Task EnsureCurrentAsync(CancellationToken cancellationToken)
    {
        if (_entries is not null && _loadedStamp == FileStamp.Of(_filePath))
            return;

        await LoadFromDiskAsync(cancellationToken);
    }

    private async Task LoadFromDiskAsync(CancellationToken cancellationToken)
    {
        FileStamp stampBeforeRead = FileStamp.Of(_filePath);
        string? json = await AtomicFile.ReadAllTextAsync(
            _filePath,
            cancellationToken: cancellationToken
        );
        _entries = json is null
            ? []
            : JsonSerializer.Deserialize<List<ActivityLogEntry>>(json, JsonOptions) ?? [];
        _loadedStamp = stampBeforeRead;
    }

    public async Task LogAsync(
        ActivityLogEntry entry,
        CancellationToken cancellationToken = default
    )
    {
        await MutateAsync(
            entries =>
            {
                entries.Insert(0, entry);
                if (entries.Count > MaxEntries)
                {
                    entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
                }
            },
            cancellationToken
        );
    }

    public async Task<IReadOnlyList<ActivityLogEntry>> GetEntriesAsync(
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureCurrentAsync(cancellationToken);

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
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureCurrentAsync(cancellationToken);

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
        await MutateAsync(entries => entries.Clear(), cancellationToken);
    }

    private async Task MutateAsync(
        Action<List<ActivityLogEntry>> mutation,
        CancellationToken cancellationToken
    )
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            using IDisposable fileLock = await CrossProcessFileLock.AcquireAsync(
                _filePath,
                cancellationToken: cancellationToken
            );
            await LoadFromDiskAsync(cancellationToken);

            mutation(_entries!);

            string json = JsonSerializer.Serialize(_entries, JsonOptions);
            await SafeFileWriter.WriteAtomicWithBackupAsync(_filePath, json, cancellationToken);
            _loadedStamp = FileStamp.Of(_filePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
