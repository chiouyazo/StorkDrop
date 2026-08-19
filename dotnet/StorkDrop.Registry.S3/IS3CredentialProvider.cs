using Amazon.Runtime;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Registry.S3;

/// <summary>
/// Turns feed settings + decrypted secrets into AWS credentials. This is the seam that lets the v1
/// static-access-key model be replaced later by an STS token-vending provider without touching the
/// registry client.
/// </summary>
public interface IS3CredentialProvider
{
    AWSCredentials GetCredentials(S3FeedSettings settings, DecryptedFeedSecrets secrets);
}
