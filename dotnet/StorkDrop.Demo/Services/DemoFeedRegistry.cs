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
    };

    private readonly List<FeedInfo> _feeds =
    [
        new FeedInfo("internal", "Internal"),
        new FeedInfo("partner", "Partner"),
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
