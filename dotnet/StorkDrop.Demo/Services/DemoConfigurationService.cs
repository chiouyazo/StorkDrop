using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Demo.Scenarios;

namespace StorkDrop.Demo.Services;

internal sealed class DemoConfigurationService : IConfigurationService
{
    private AppConfiguration _config = new(
        Feeds:
        [
            Feed(DemoIds.FeedInternal, "Internal", "https://demo.internal"),
            Feed(DemoIds.FeedPartner, "Partner", "https://demo.partner"),
            Feed(
                DemoIds.FeedLocked,
                "Demo: Locked feed",
                "https://demo.locked",
                PasswordHasher.Hash(DemoIds.LockedFeedPassword)
            ),
            Feed(DemoIds.FeedDown, "Demo: Feed unreachable", "https://demo.down"),
            Feed(DemoIds.FeedUnauthorized, "Demo: Feed unauthorized (401)", "https://demo.unauth"),
        ],
        AutoStart: false,
        AutoCheckForUpdates: true,
        CheckInterval: TimeSpan.FromSeconds(20),
        Language: "en",
        CheckForStorkDropUpdates: false
    );

    public bool ConfigurationExists() => true;

    public Task<AppConfiguration?> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<AppConfiguration?>(_config);

    public Task SaveAsync(
        AppConfiguration configuration,
        CancellationToken cancellationToken = default
    )
    {
        _config = configuration;
        return Task.CompletedTask;
    }

    public Task ExportAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ImportAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    private static FeedConfiguration Feed(
        string id,
        string name,
        string url,
        string? lockHash = null
    ) => new FeedConfiguration(id, name, url, null, null, null, null, LockPasswordHash: lockHash);
}
