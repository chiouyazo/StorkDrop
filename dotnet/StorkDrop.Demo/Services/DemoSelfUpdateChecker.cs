using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Demo.Services;

internal sealed class DemoSelfUpdateChecker : ISelfUpdateChecker
{
    public Task<UpdateInfo?> CheckForUpdateAsync(
        bool includeDevVersions,
        CancellationToken ct = default
    ) => Task.FromResult<UpdateInfo?>(null);
}
