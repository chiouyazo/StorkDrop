namespace StorkDrop.Contracts.Services;

/// <summary>
/// The non-secret S3 coordinates a white-label edition fixes for its pre-configured feed. Access keys
/// are never baked into a distributed edition; the customer supplies them per install (like feed
/// credentials), so they are absent here.
/// </summary>
public sealed record BrandingS3(
    string Bucket,
    string? Region = null,
    string? ServiceUrl = null,
    bool UsePathStyle = false,
    string? Prefix = null,
    string[]? Channels = null
);
