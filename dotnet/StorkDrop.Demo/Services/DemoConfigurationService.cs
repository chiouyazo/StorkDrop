using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Demo.Services;

internal sealed class DemoConfigurationService : IConfigurationService
{
    private AppConfiguration _config = new(
        Feeds:
        [
            new FeedConfiguration(
                "internal",
                "Internal",
                "https://demo.internal",
                null,
                null,
                null,
                null
            ),
            new FeedConfiguration(
                "partner",
                "Partner",
                "https://demo.partner",
                null,
                null,
                null,
                null
            ),
        ],
        AutoStart: false,
        AutoCheckForUpdates: false,
        CheckInterval: TimeSpan.FromHours(24),
        Language: "en"
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
}
