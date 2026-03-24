using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Interfaces;

public interface IUpdateChecker
{
    Task<IReadOnlyList<UpdateCheckResult>> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default
    );
}
