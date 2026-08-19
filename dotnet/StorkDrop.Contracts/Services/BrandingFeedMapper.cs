using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Services;

/// <summary>
/// Maps a white-label <see cref="BrandingFeed"/> into concrete <see cref="S3FeedSettings"/>, combining
/// the vendor-fixed coordinates with the customer-supplied (already encrypted) access credentials.
/// </summary>
public static class BrandingFeedMapper
{
    public static S3FeedSettings? ToS3Settings(
        BrandingFeed feed,
        string? accessKeyId,
        string? encryptedSecretKey
    )
    {
        if (feed.Provider != FeedProvider.S3 || feed.S3 is null)
            return null;

        BrandingS3 s3 = feed.S3;
        return new S3FeedSettings(
            Bucket: s3.Bucket,
            Region: s3.Region,
            ServiceUrl: s3.ServiceUrl,
            UsePathStyle: s3.UsePathStyle,
            AccessKeyId: string.IsNullOrWhiteSpace(accessKeyId) ? null : accessKeyId,
            EncryptedSecretKey: string.IsNullOrWhiteSpace(encryptedSecretKey)
                ? null
                : encryptedSecretKey,
            Prefix: s3.Prefix,
            Channels: s3.Channels
        );
    }
}
