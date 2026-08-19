using Amazon.Runtime;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Registry.S3;

/// <summary>
/// v1 credential provider: long-lived access key + secret key (optionally a session token). Falls back
/// to anonymous credentials when nothing is configured, which supports public read-only buckets.
/// </summary>
public sealed class StaticKeysCredentialProvider : IS3CredentialProvider
{
    public AWSCredentials GetCredentials(S3FeedSettings settings, DecryptedFeedSecrets secrets)
    {
        string accessKey = settings.AccessKeyId ?? string.Empty;
        string secretKey = secrets.S3SecretKey ?? string.Empty;

        if (!string.IsNullOrEmpty(secrets.S3SessionToken))
            return new SessionAWSCredentials(accessKey, secretKey, secrets.S3SessionToken);

        if (string.IsNullOrEmpty(accessKey) && string.IsNullOrEmpty(secretKey))
            return new AnonymousAWSCredentials();

        return new BasicAWSCredentials(accessKey, secretKey);
    }
}
