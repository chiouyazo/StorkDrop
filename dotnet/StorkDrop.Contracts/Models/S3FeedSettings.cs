namespace StorkDrop.Contracts.Models;

/// <summary>
/// Backend settings for a <see cref="FeedProvider.S3"/> feed. Works against AWS S3 (leave
/// <see cref="ServiceUrl"/> null) or any S3-compatible service (set <see cref="ServiceUrl"/> and, for
/// most of them, <see cref="UsePathStyle"/> = true).
///
/// Access keys are the v1 credential model: <see cref="AccessKeyId"/> is stored in clear (not a secret)
/// and <see cref="EncryptedSecretKey"/> is encrypted at rest with the same service that protects feed
/// passwords. <see cref="RoleArn"/> and <see cref="EncryptedSessionToken"/> exist so a future STS
/// token-vending credential provider can be added without changing this schema.
/// </summary>
public sealed record S3FeedSettings(
    string Bucket,
    string? Region = null,
    string? ServiceUrl = null,
    bool UsePathStyle = false,
    string? AccessKeyId = null,
    string? EncryptedSecretKey = null,
    string? EncryptedSessionToken = null,
    string? RoleArn = null,
    string? Prefix = null,
    string[]? Channels = null,
    bool AllowUnverified = false
);
