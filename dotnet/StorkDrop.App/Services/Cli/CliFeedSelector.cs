using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.Services.Cli;

internal static class CliFeedSelector
{
    public static IReadOnlyList<FeedInfo> Select(IFeedRegistry registry, string? token)
    {
        IReadOnlyList<FeedInfo> feeds = registry.GetFeeds();
        if (string.IsNullOrWhiteSpace(token))
            return feeds;

        List<FeedInfo> byId = feeds
            .Where(f => string.Equals(f.Id, token, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byId.Count > 0)
            return byId;

        List<FeedInfo> byName = feeds
            .Where(f => string.Equals(f.Name, token, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byName.Count > 0)
            return byName;

        string configured = feeds.Count == 0 ? "none" : string.Join(", ", feeds.Select(f => f.Id));
        throw new CliException(
            CliExitCodes.NotFound,
            $"Feed '{token}' not found. Configured feeds: {configured}."
        );
    }
}
