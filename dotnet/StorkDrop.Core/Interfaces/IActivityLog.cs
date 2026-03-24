using StorkDrop.Core.Models;

namespace StorkDrop.Core.Interfaces;

public interface IActivityLog
{
    Task LogAsync(ActivityLogEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityLogEntry>> GetEntriesAsync(
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<ActivityLogEntry>> GetEntriesByProductAsync(
        string productId,
        CancellationToken cancellationToken = default
    );
    Task ClearAsync(CancellationToken cancellationToken = default);
}
