using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Interfaces;

public interface ISelfUpdateChecker
{
    Task<UpdateInfo?> CheckForUpdateAsync(
        bool includeDevVersions,
        CancellationToken cancellationToken = default
    );
}
