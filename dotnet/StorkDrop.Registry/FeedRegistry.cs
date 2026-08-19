using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Registry;

/// <summary>
/// Manages multiple feeds, delegating client creation to a per-backend <see cref="IRegistryClientFactory"/>.
/// A single feed configuration may expand into several clients (Nexus repositories, S3 channels).
/// </summary>
public sealed class FeedRegistry : IFeedRegistry, IDisposable
{
    private static readonly string[] DefaultChannels = ["prod"];

    private readonly IConfigurationService _configurationService;
    private readonly IEncryptionService _encryptionService;
    private readonly IReadOnlyList<IRegistryClientFactory> _factories;
    private readonly ILogger<FeedRegistry> _logger;
    private readonly object _lock = new object();

    private Dictionary<string, FeedEntry> _feeds = new Dictionary<string, FeedEntry>();

    private sealed record FeedEntry(FeedInfo Info, IRegistryClient Client, IDisposable? Owned);

    public FeedRegistry(
        IConfigurationService configurationService,
        IEncryptionService encryptionService,
        IEnumerable<IRegistryClientFactory> factories,
        ILoggerFactory loggerFactory
    )
    {
        _configurationService = configurationService;
        _encryptionService = encryptionService;
        _factories = factories.ToList();
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

            // Fallback: a base config ID that expanded into composite IDs (discovery repos, S3 channels).
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
        string[] visibleChannels = config?.VisibleChannels is { Length: > 0 } vc
            ? vc
            : DefaultChannels;
        _logger.LogInformation(
            "Found {Count} feed configurations, visible channels: {Channels}",
            feedConfigs.Length,
            string.Join(", ", visibleChannels)
        );

        Dictionary<string, FeedEntry> newFeeds = new Dictionary<string, FeedEntry>();

        foreach (FeedConfiguration fc in feedConfigs)
        {
            _logger.LogDebug(
                "Loading feed {FeedName} ({FeedId}) provider {Provider}",
                fc.Name,
                fc.Id,
                fc.Provider
            );

            IRegistryClientFactory? factory = _factories.FirstOrDefault(f =>
                f.Provider == fc.Provider
            );
            if (factory is null)
            {
                _logger.LogWarning(
                    "No registry factory registered for provider {Provider} (feed {FeedId}), skipping",
                    fc.Provider,
                    fc.Id
                );
                continue;
            }

            try
            {
                DecryptedFeedSecrets secrets = DecryptSecrets(fc);
                IReadOnlyList<RegistryClientRegistration> registrations = await factory
                    .CreateAsync(fc, secrets, visibleChannels, cancellationToken)
                    .ConfigureAwait(false);

                foreach (RegistryClientRegistration registration in registrations)
                {
                    newFeeds[registration.FeedId] = new FeedEntry(
                        new FeedInfo(registration.FeedId, registration.FeedName),
                        registration.Client,
                        registration.OwnedResource
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to load feed {FeedName} ({FeedId}), skipping",
                    fc.Name,
                    fc.Id
                );
            }
        }

        Dictionary<string, FeedEntry> oldFeeds;
        lock (_lock)
        {
            oldFeeds = _feeds;
            _feeds = newFeeds;
        }

        DisposeAll(oldFeeds);

        _logger.LogInformation("Feed registry reloaded with {Count} feeds", newFeeds.Count);
    }

    private DecryptedFeedSecrets DecryptSecrets(FeedConfiguration fc)
    {
        return new DecryptedFeedSecrets(
            Password: DecryptOrNull(fc.EncryptedPassword, fc, "password"),
            S3SecretKey: DecryptOrNull(fc.S3?.EncryptedSecretKey, fc, "S3 secret key"),
            S3SessionToken: DecryptOrNull(fc.S3?.EncryptedSessionToken, fc, "S3 session token")
        );
    }

    private string? DecryptOrNull(string? encrypted, FeedConfiguration fc, string what)
    {
        if (string.IsNullOrEmpty(encrypted))
            return null;

        try
        {
            return _encryptionService.Decrypt(encrypted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to decrypt {What} for feed {FeedName} ({FeedId})",
                what,
                fc.Name,
                fc.Id
            );
            return null;
        }
    }

    private static void DisposeAll(Dictionary<string, FeedEntry> feeds)
    {
        foreach (FeedEntry entry in feeds.Values)
            entry.Owned?.Dispose();
    }

    public void Dispose()
    {
        Dictionary<string, FeedEntry> feeds;
        lock (_lock)
        {
            feeds = _feeds;
            _feeds = new Dictionary<string, FeedEntry>();
        }
        DisposeAll(feeds);
    }
}
