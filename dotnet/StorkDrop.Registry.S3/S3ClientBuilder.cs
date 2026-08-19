using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Registry.S3;

/// <summary>
/// Builds an <see cref="IAmazonS3"/> from feed settings. Handles both AWS (region only) and
/// S3-compatible services (explicit <see cref="S3FeedSettings.ServiceUrl"/> + path-style addressing).
/// </summary>
public static class S3ClientBuilder
{
    public static IAmazonS3 Build(S3FeedSettings settings, AWSCredentials credentials)
    {
        AmazonS3Config config = new AmazonS3Config { ForcePathStyle = settings.UsePathStyle };

        if (!string.IsNullOrWhiteSpace(settings.ServiceUrl))
        {
            config.ServiceURL = settings.ServiceUrl;
            // SigV4 still needs a region name even against a custom endpoint.
            config.AuthenticationRegion = string.IsNullOrWhiteSpace(settings.Region)
                ? "us-east-1"
                : settings.Region;
        }
        else if (!string.IsNullOrWhiteSpace(settings.Region))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region);
        }

        return new AmazonS3Client(credentials, config);
    }
}
