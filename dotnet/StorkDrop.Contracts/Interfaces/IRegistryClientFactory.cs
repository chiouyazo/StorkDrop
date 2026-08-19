using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Interfaces;

/// <summary>
/// Secrets belonging to a feed, already decrypted by the caller (the registry) so factories never need
/// the encryption service. Empty/null fields mean "not configured".
/// </summary>
public sealed record DecryptedFeedSecrets(
    string? Password = null,
    string? S3SecretKey = null,
    string? S3SessionToken = null
);

/// <summary>
/// One ready-to-use registry client produced by a factory, together with its display identity and any
/// resource whose lifetime the registry must own (an HttpClient for Nexus, the S3 client for S3).
/// </summary>
public sealed record RegistryClientRegistration(
    string FeedId,
    string FeedName,
    IRegistryClient Client,
    IDisposable? OwnedResource = null
);

/// <summary>
/// Creates <see cref="IRegistryClient"/> instances for a single storage backend. One configured feed
/// may expand into several clients: Nexus discovery mode yields one per repository, S3 yields one per
/// visible channel. This is the single seam that decouples the registry from a specific backend.
/// </summary>
public interface IRegistryClientFactory
{
    /// <summary>The backend this factory handles.</summary>
    FeedProvider Provider { get; }

    /// <summary>
    /// Builds every client a single feed configuration expands into. Implementations may perform I/O
    /// (e.g. Nexus repository discovery). Callers dispose each returned <see cref="RegistryClientRegistration.OwnedResource"/>.
    /// </summary>
    Task<IReadOnlyList<RegistryClientRegistration>> CreateAsync(
        FeedConfiguration config,
        DecryptedFeedSecrets secrets,
        IReadOnlyList<string> visibleChannels,
        CancellationToken cancellationToken = default
    );
}
