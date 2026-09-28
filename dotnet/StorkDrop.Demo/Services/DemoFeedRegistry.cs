using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Demo.Data;

namespace StorkDrop.Demo.Services;

internal sealed class DemoFeedRegistry : IFeedRegistry
{
    private readonly Dictionary<string, IRegistryClient> _clients = new Dictionary<
        string,
        IRegistryClient
    >
    {
        ["internal"] = new DemoRegistryClient(DemoProducts.InternalFeedProducts),
        ["partner"] = new DemoRegistryClient(DemoProducts.PartnerFeedProducts),
        ["cli-stable"] = new DemoRegistryClient(
            [DemoProducts.NovaCliTools],
            ["2.0.0", "1.9.2", "1.9.1", "1.9.0", "1.8.5"],
            "Stable",
            "#2E7D32"
        ),
        ["cli-beta"] = new DemoRegistryClient(
            [DemoProducts.NovaCliTools],
            ["2.2.0-beta.1", "2.1.0-beta.3", "2.1.0-beta.2", "2.1.0-beta.1", "2.0.0-beta.4"],
            "Beta",
            "#1565C0"
        ),
        ["cli-epic"] = new DemoRegistryClient(
            [DemoProducts.NovaCliTools],
            [
                "2.35.0-2401-20261001-0900",
                "2.34.7-2390-20260920-2455",
                "2.34.7-2386-20260915-2438",
                "2.34.7-2386-20260915-2401",
                "2.34.7-2386-20260901-2210",
            ],
            "EPIC",
            "#8E24AA"
        ),
        ["cli-nightly"] = new DemoRegistryClient(
            [DemoProducts.NovaCliTools],
            ["2.3.0-nightly.20261005", "2.3.0-nightly.20261004", "2.3.0-nightly.20261003"],
            "Nightly",
            "#EF6C00"
        ),
    };

    private readonly List<FeedInfo> _feeds =
    [
        new FeedInfo("internal", "Internal"),
        new FeedInfo("partner", "Partner"),
        new FeedInfo("cli-stable", "CLI · Stable"),
        new FeedInfo("cli-beta", "CLI · Beta"),
        new FeedInfo("cli-epic", "CLI · EPIC"),
        new FeedInfo("cli-nightly", "CLI · Nightly"),
    ];

    public IReadOnlyList<FeedInfo> GetFeeds() => _feeds;

    public IRegistryClient GetClient(string feedId) =>
        _clients.TryGetValue(feedId, out IRegistryClient? client)
            ? client
            : throw new KeyNotFoundException($"Feed '{feedId}' not found");

    public Task<bool> TestConnectionAsync(
        string feedId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(_clients.ContainsKey(feedId));

    public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
