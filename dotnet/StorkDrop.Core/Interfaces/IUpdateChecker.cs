using StorkDrop.Core.Models;

namespace StorkDrop.Core.Interfaces;

public interface IUpdateChecker
{
    Task<IReadOnlyList<UpdateCheckResult>> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default
    );
}
