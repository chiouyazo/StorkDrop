using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Registry.Nexus;

namespace StorkDrop.Registry;

/// <summary>
/// Manages multiple feeds, creating a dedicated IRegistryClient per configured feed.
/// </summary>
public sealed class FeedRegistry : IFeedRegistry, IDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly IEncryptionService _encryptionService;
    private readonly IFeedConnectionService _connectionService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<FeedRegistry> _logger;
    private readonly object _lock = new object();

    private Dictionary<string, FeedEntry> _feeds = new Dictionary<string, FeedEntry>();

    private sealed record FeedEntry(FeedInfo Info, IRegistryClient Client, HttpClient HttpClient);

    public FeedRegistry(
        IConfigurationService configurationService,
        IEncryptionService encryptionService,
        IFeedConnectionService connectionService,
        ILoggerFactory loggerFactory
    )
    {
        _configurationService = configurationService;
        _encryptionService = encryptionService;
        _connectionService = connectionService;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<FeedRegistry>();
    }

    public IReadOnlyList<FeedInfo> GetFeeds()
    {
        lock (_lock)
        {
            return _feeds.Values.Select(f => f.Info).ToList();
        }
    }

    public IRegistryClient GetClient(string feedId)
    {
        _logger.LogDebug("GetClient requested for feed {FeedId}", feedId);
        lock (_lock)
        {
            if (_feeds.TryGetValue(feedId, out FeedEntry? entry))
                return entry.Client;

            // Fallback: if feedId was a base config ID that is now expanded into composite IDs,
            // find the first match with that prefix (handles migration from pinned to discovery mode)
            string prefix = feedId + ":";
            FeedEntry? fallback = _feeds.Values.FirstOrDefault(f => f.Info.Id.StartsWith(prefix));
            if (fallback is not null)
            {
                _logger.LogDebug(
                    "Feed {FeedId} not found directly, falling back to {FallbackId}",
                    feedId,
                    fallback.Info.Id
                );
                return fallback.Client;
            }

            _logger.LogError("Feed {FeedId} not found in registry", feedId);
            throw new KeyNotFoundException($"Feed '{feedId}' not found.");
        }
    }

    public async Task<bool> TestConnectionAsync(
        string feedId,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation("Testing connection for feed {FeedId}", feedId);
        bool result = await GetClient(feedId).TestConnectionAsync(cancellationToken);
        _logger.LogInformation(
            "Connection test for feed {FeedId}: {Result}",
            feedId,
            result ? "success" : "failed"
        );
        return result;
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reloading feed registry");
        AppConfiguration? config = await _configurationService.LoadAsync(cancellationToken);
        FeedConfiguration[] feedConfigs = config?.Feeds ?? [];
        _logger.LogInformation("Found {Count} feed configurations", feedConfigs.Length);

        Dictionary<string, FeedEntry> newFeeds = new Dictionary<string, FeedEntry>();

        foreach (FeedConfiguration fc in feedConfigs)
        {
            _logger.LogDebug("Loading feed {FeedName} ({FeedId}) at {Url}", fc.Name, fc.Id, fc.Url);
            string password = DecryptPassword(fc);

            HttpClient baseHttpClient = _connectionService.CreateAuthenticatedClient(
                fc.Url,
                fc.Username,
                password
            );

            if (!string.IsNullOrWhiteSpace(fc.Repository))
            {
                // Pinned mode
                FeedEntry entry = CreateFeedEntry(
                    fc.Id,
                    fc.Name,
                    fc.Url,
                    fc.Repository,
                    baseHttpClient
                );
                newFeeds[fc.Id] = entry;
            }
            else
            {
                // Discovery Mode
                try
                {
                    IReadOnlyList<NexusRepositoryInfo> repos =
                        await NexusRegistryClient.ListRawHostedRepositoriesAsync(
                            baseHttpClient,
                            fc.Url,
                            cancellationToken
                        );

                    _logger.LogInformation(
                        "Discovered {Count} raw hosted repositories on {FeedName}",
                        repos.Count,
                        fc.Name
                    );

                    foreach (NexusRepositoryInfo repo in repos)
                    {
                        string feedId = $"{fc.Id}:{repo.Name}";
                        string feedName = $"{fc.Name} / {repo.Name}";

                        // Each discovered repo needs its own HttpClient (same auth)
                        HttpClient repoHttpClient = _connectionService.CreateAuthenticatedClient(
                            fc.Url,
                            fc.Username,
                            password
                        );

                        FeedEntry entry = CreateFeedEntry(
                            feedId,
                            feedName,
                            fc.Url,
                            repo.Name,
                            repoHttpClient
                        );
                        newFeeds[feedId] = entry;
                    }

                    // Dispose the base client used only for discovery
                    baseHttpClient.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to discover repositories for feed {FeedName} ({FeedId}), skipping",
                        fc.Name,
                        fc.Id
                    );
                    baseHttpClient.Dispose();
                }
            }
        }

        Dictionary<string, FeedEntry> oldFeeds;
        lock (_lock)
        {
            oldFeeds = _feeds;
            _feeds = newFeeds;
        }

        foreach (FeedEntry entry in oldFeeds.Values)
            entry.HttpClient.Dispose();

        _logger.LogInformation("Feed registry reloaded with {Count} feeds", newFeeds.Count);
    }

    private string DecryptPassword(FeedConfiguration fc)
    {
        if (string.IsNullOrEmpty(fc.EncryptedPassword))
            return string.Empty;

        try
        {
            return _encryptionService.Decrypt(fc.EncryptedPassword);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to decrypt password for feed {FeedName} ({FeedId})",
                fc.Name,
                fc.Id
            );
            return string.Empty;
        }
    }

    private FeedEntry CreateFeedEntry(
        string feedId,
        string feedName,
        string baseUrl,
        string repository,
        HttpClient httpClient
    )
    {
        NexusOptions opts = new NexusOptions { BaseUrl = baseUrl, Repository = repository };

        ILogger<NexusRegistryClient> logger = _loggerFactory.CreateLogger<NexusRegistryClient>();
        NexusRegistryClient client = new NexusRegistryClient(
            httpClient,
            Options.Create(opts),
            logger
        );

        return new FeedEntry(new FeedInfo(feedId, feedName), client, httpClient);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (FeedEntry entry in _feeds.Values)
                entry.HttpClient.Dispose();
            _feeds.Clear();
        }
    }
}
