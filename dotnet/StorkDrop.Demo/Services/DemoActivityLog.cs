using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Demo.Services;

internal sealed class DemoActivityLog : IActivityLog
{
    private readonly List<ActivityLogEntry> _entries =
    [
        new(
            "1",
            new DateTime(2026, 3, 28, 14, 30, 0),
            "Install",
            "nova-cli-tools",
            "Installed Nova CLI Tools v1.0.0 to C:\\Users\\Demo\\StorkDrop\\CLI",
            true
        ),
        new(
            "2",
            new DateTime(2026, 3, 29, 9, 15, 0),
            "Install",
            "nova-dashboard",
            "Installed Nova Dashboard v2.0.0 to C:\\Users\\Demo\\NovaApps\\Dashboard",
            true
        ),
        new(
            "3",
            new DateTime(2026, 3, 29, 9, 18, 0),
            "Uninstall",
            "nova-dashboard",
            "Uninstalled Nova Dashboard v2.0.0",
            true
        ),
    ];

    public Task LogAsync(ActivityLogEntry entry, CancellationToken cancellationToken = default)
    {
        _entries.Insert(0, entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActivityLogEntry>> GetEntriesAsync(
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult<IReadOnlyList<ActivityLogEntry>>(
            _entries.Skip(offset).Take(limit).ToList()
        );

    public Task<IReadOnlyList<ActivityLogEntry>> GetEntriesByProductAsync(
        string productId,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult<IReadOnlyList<ActivityLogEntry>>(
            _entries.Where(e => e.ProductId == productId).ToList()
        );

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _entries.Clear();
        return Task.CompletedTask;
    }
}
