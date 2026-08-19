using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Registry.Nexus;

/// <summary>
/// Builds Nexus registry clients. Pinned mode (a repository is configured) yields a single client;
/// discovery mode (no repository) enumerates the raw hosted/group repositories and yields one client
/// per repository, mirroring the legacy behaviour that lived in <c>FeedRegistry</c>.
/// </summary>
public sealed class NexusRegistryClientFactory : IRegistryClientFactory
{
    private readonly IFeedConnectionService _connectionService;
    private readonly ILoggerFactory _loggerFactory;

    public NexusRegistryClientFactory(
        IFeedConnectionService connectionService,
        ILoggerFactory loggerFactory
    )
    {
        _connectionService = connectionService;
        _loggerFactory = loggerFactory;
    }

    public FeedProvider Provider => FeedProvider.Nexus;

    public async Task<IReadOnlyList<RegistryClientRegistration>> CreateAsync(
        FeedConfiguration config,
        DecryptedFeedSecrets secrets,
        IReadOnlyList<string> visibleChannels,
        CancellationToken cancellationToken = default
    )
    {
        string password = secrets.Password ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(config.Repository))
        {
            HttpClient pinnedClient = _connectionService.CreateAuthenticatedClient(
                config.Url,
                config.Username,
                password
            );
            return
            [
                BuildEntry(config.Id, config.Name, config.Url, config.Repository, pinnedClient),
            ];
        }

        // Discovery mode: enumerate raw repositories, one client each.
        HttpClient discoveryClient = _connectionService.CreateAuthenticatedClient(
            config.Url,
            config.Username,
            password
        );
        try
        {
            IReadOnlyList<NexusRepositoryInfo> repos = await NexusRegistryClient
                .ListRawHostedRepositoriesAsync(discoveryClient, config.Url, cancellationToken)
                .ConfigureAwait(false);

            List<RegistryClientRegistration> registrations = [];
            foreach (NexusRepositoryInfo repo in repos)
            {
                HttpClient repoClient = _connectionService.CreateAuthenticatedClient(
                    config.Url,
                    config.Username,
                    password
                );
                registrations.Add(
                    BuildEntry(
                        $"{config.Id}:{repo.Name}",
                        $"{config.Name} / {repo.Name}",
                        config.Url,
                        repo.Name,
                        repoClient
                    )
                );
            }
            return registrations;
        }
        finally
        {
            discoveryClient.Dispose();
        }
    }

    private RegistryClientRegistration BuildEntry(
        string feedId,
        string feedName,
        string baseUrl,
        string repository,
        HttpClient httpClient
    )
    {
        NexusOptions options = new NexusOptions { BaseUrl = baseUrl, Repository = repository };
        NexusRegistryClient client = new NexusRegistryClient(
            httpClient,
            Options.Create(options),
            _loggerFactory.CreateLogger<NexusRegistryClient>()
        );
        return new RegistryClientRegistration(feedId, feedName, client, httpClient);
    }
}
