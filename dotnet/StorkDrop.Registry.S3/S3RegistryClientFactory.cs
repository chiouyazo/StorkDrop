using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Registry.S3;

/// <summary>
/// Expands one S3 feed configuration into one <see cref="S3RegistryClient"/> per visible channel. The
/// channel set comes from the feed's own <see cref="S3FeedSettings.Channels"/> if given, otherwise from
/// the caller's visible-channel list, otherwise defaults to <c>prod</c>.
/// </summary>
public sealed class S3RegistryClientFactory : IRegistryClientFactory
{
    private readonly IS3CredentialProvider _credentialProvider;
    private readonly ILoggerFactory _loggerFactory;

    public S3RegistryClientFactory(
        IS3CredentialProvider credentialProvider,
        ILoggerFactory loggerFactory
    )
    {
        _credentialProvider = credentialProvider;
        _loggerFactory = loggerFactory;
    }

    public FeedProvider Provider => FeedProvider.S3;

    public Task<IReadOnlyList<RegistryClientRegistration>> CreateAsync(
        FeedConfiguration config,
        DecryptedFeedSecrets secrets,
        IReadOnlyList<string> visibleChannels,
        CancellationToken cancellationToken = default
    )
    {
        if (config.S3 is null)
        {
            throw new InvalidOperationException(
                $"Feed '{config.Id}' uses the S3 provider but has no S3 settings."
            );
        }

        S3FeedSettings settings = config.S3;
        string[] channels = ResolveChannels(settings, visibleChannels);
        S3Layout layout = new S3Layout(settings.Prefix);
        AWSCredentials credentials = _credentialProvider.GetCredentials(settings, secrets);

        // One S3 client shared across this feed's channel clients (config/credentials are identical).
        // Its lifetime is owned by the first registration so the registry disposes it exactly once.
        IAmazonS3 shared = S3ClientBuilder.Build(settings, credentials);

        List<RegistryClientRegistration> registrations = [];
        bool first = true;
        foreach (string channel in channels)
        {
            S3RegistryClient client = new S3RegistryClient(
                shared,
                settings.Bucket,
                layout,
                channel,
                _loggerFactory.CreateLogger<S3RegistryClient>(),
                settings.AllowUnverified
            );
            registrations.Add(
                new RegistryClientRegistration(
                    $"{config.Id}:{channel}",
                    $"{config.Name} / {channel}",
                    client,
                    first ? shared : null
                )
            );
            first = false;
        }

        if (registrations.Count == 0)
            shared.Dispose();

        return Task.FromResult<IReadOnlyList<RegistryClientRegistration>>(registrations);
    }

    private static string[] ResolveChannels(
        S3FeedSettings settings,
        IReadOnlyList<string> visibleChannels
    )
    {
        IEnumerable<string> source =
            settings.Channels is { Length: > 0 } configured ? configured
            : visibleChannels.Count > 0 ? visibleChannels
            : ["prod"];

        // Only accept channels that are safe single key segments; a bad channel name would corrupt
        // the key layout and the composite feed id.
        return source
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Where(S3Names.IsValidSegment)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
