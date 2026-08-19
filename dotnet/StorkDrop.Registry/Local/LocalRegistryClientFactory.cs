using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Registry.Local;

/// <summary>
/// Builds a <see cref="LocalRegistryClient"/> from a feed whose <see cref="FeedConfiguration.Url"/> is a
/// local folder path.
/// </summary>
public sealed class LocalRegistryClientFactory : IRegistryClientFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public LocalRegistryClientFactory(ILoggerFactory loggerFactory) =>
        _loggerFactory = loggerFactory;

    public FeedProvider Provider => FeedProvider.Local;

    public Task<IReadOnlyList<RegistryClientRegistration>> CreateAsync(
        FeedConfiguration config,
        DecryptedFeedSecrets secrets,
        IReadOnlyList<string> visibleChannels,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(config.Url))
        {
            throw new InvalidOperationException(
                $"Feed '{config.Id}' uses the Local provider but has no folder path."
            );
        }

        LocalRegistryClient client = new LocalRegistryClient(
            config.Url,
            _loggerFactory.CreateLogger<LocalRegistryClient>()
        );
        IReadOnlyList<RegistryClientRegistration> registrations =
        [
            new RegistryClientRegistration(config.Id, config.Name, client, null),
        ];
        return Task.FromResult(registrations);
    }
}
