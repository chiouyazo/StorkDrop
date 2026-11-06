using System.IO;
using System.Net.Http;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Demo.Sandbox;
using StorkDrop.Demo.Scenarios;

namespace StorkDrop.Demo.Services;

internal sealed class DemoFeedRegistry : IFeedRegistry
{
    private readonly object _gate = new object();
    private readonly List<DemoFeedContent> _feeds = DemoCatalog.CreateFeeds().ToList();
    private readonly Dictionary<string, DemoRegistryClient> _clients = new Dictionary<
        string,
        DemoRegistryClient
    >(StringComparer.Ordinal);

    public DemoFeedRegistry()
    {
        foreach (DemoFeedContent feed in _feeds)
            _clients[feed.Id] = new DemoRegistryClient(feed);
    }

    public DemoFeedContent? Feed(string feedId)
    {
        lock (_gate)
            return _feeds.FirstOrDefault(f => f.Id == feedId);
    }

    public IReadOnlyList<FeedInfo> GetFeeds()
    {
        lock (_gate)
            return _feeds.Select(f => new FeedInfo(f.Id, f.Name)).ToList();
    }

    public IRegistryClient GetClient(string feedId)
    {
        lock (_gate)
        {
            return _clients.TryGetValue(feedId, out DemoRegistryClient? client)
                ? client
                : throw new KeyNotFoundException($"Feed '{feedId}' not found");
        }
    }

    public async Task<bool> TestConnectionAsync(
        string feedId,
        CancellationToken cancellationToken = default
    )
    {
        DemoFeedContent? feed = Feed(feedId);
        if (feed is null)
            return false;

        try
        {
            await feed.EnsureReachableAsync(cancellationToken);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (
                File.Exists(DemoSandbox.NewFeedMarkerFile)
                && _feeds.All(f => f.Id != DemoIds.FeedAddedLater)
            )
            {
                DemoFeedContent added = DemoCatalog.CreateAddedLaterFeed();
                _feeds.Add(added);
                _clients[added.Id] = new DemoRegistryClient(added);
            }
        }

        return Task.CompletedTask;
    }
}
