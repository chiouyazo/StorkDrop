using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Registry;

/// <summary>
/// Manages multiple feeds, creating a dedicated IRegistryClient per configured feed.
/// </summary>
public sealed class FeedRegistry : IFeedRegistry, IDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly IEncryptionService _encryptionService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly object _lock = new();

    private Dictionary<string, FeedEntry> _feeds = new();

    private sealed record FeedEntry(FeedInfo Info, IRegistryClient Client, HttpClient HttpClient);

    public FeedRegistry(
        IConfigurationService configurationService,
        IEncryptionService encryptionService,
        ILoggerFactory loggerFactory
    )
    {
        _configurationService = configurationService;
        _encryptionService = encryptionService;
        _loggerFactory = loggerFactory;
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
        lock (_lock)
        {
            if (_feeds.TryGetValue(feedId, out FeedEntry? entry))
                return entry.Client;
            throw new KeyNotFoundException($"Feed '{feedId}' not found.");
        }
    }

    public async Task<bool> TestConnectionAsync(
        string feedId,
        CancellationToken cancellationToken = default
    )
    {
        return await GetClient(feedId).TestConnectionAsync(cancellationToken);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        AppConfiguration? config = await _configurationService.LoadAsync(cancellationToken);
        FeedConfiguration[] feedConfigs = config?.Feeds ?? [];

        Dictionary<string, FeedEntry> newFeeds = new();

        foreach (FeedConfiguration fc in feedConfigs)
        {
            string password = string.Empty;
            if (!string.IsNullOrEmpty(fc.EncryptedPassword))
            {
                try
                {
                    password = _encryptionService.Decrypt(fc.EncryptedPassword);
                }
                catch
                {
                    // Best-effort decryption
                }
            }

            NexusOptions opts = new()
            {
                BaseUrl = fc.Url,
                Repository = fc.Repository,
                Username = fc.Username ?? string.Empty,
                Password = password,
            };

            HttpClientHandler handler = new()
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            };

            HttpClient httpClient = new(handler)
            {
                BaseAddress = new Uri(opts.BaseUrl),
                Timeout = opts.Timeout,
            };

            if (!string.IsNullOrEmpty(opts.Username) && !string.IsNullOrEmpty(opts.Password))
            {
                string creds = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{opts.Username}:{opts.Password}")
                );
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    creds
                );
            }

            ILogger<NexusRegistryClient> logger =
                _loggerFactory.CreateLogger<NexusRegistryClient>();
            NexusRegistryClient client = new(httpClient, Options.Create(opts), logger);

            newFeeds[fc.Id] = new FeedEntry(new FeedInfo(fc.Id, fc.Name), client, httpClient);
        }

        Dictionary<string, FeedEntry> oldFeeds;
        lock (_lock)
        {
            oldFeeds = _feeds;
            _feeds = newFeeds;
        }

        foreach (FeedEntry entry in oldFeeds.Values)
            entry.HttpClient.Dispose();
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
