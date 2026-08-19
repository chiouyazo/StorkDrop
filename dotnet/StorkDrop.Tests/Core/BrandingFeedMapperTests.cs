using FluentAssertions;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using Xunit;

namespace StorkDrop.Tests.Core;

public sealed class BrandingFeedMapperTests
{
    [Fact]
    public void Nexus_branding_feed_maps_to_null()
    {
        BrandingFeed feed = new BrandingFeed(Name: "Acme", Url: "https://nexus.acme.com");

        BrandingFeedMapper.ToS3Settings(feed, "ak", "enc-secret").Should().BeNull();
    }

    [Fact]
    public void S3_branding_feed_maps_coordinates_and_supplied_credentials()
    {
        BrandingFeed feed = new BrandingFeed(
            Provider: FeedProvider.S3,
            S3: new BrandingS3(
                Bucket: "acme-bucket",
                Region: "eu-central-1",
                ServiceUrl: "https://minio.acme.com",
                UsePathStyle: true,
                Prefix: "tenant-1",
                Channels: ["prod"]
            )
        );

        S3FeedSettings? settings = BrandingFeedMapper.ToS3Settings(feed, "AKIA", "enc-secret");

        settings.Should().NotBeNull();
        settings!.Bucket.Should().Be("acme-bucket");
        settings.Region.Should().Be("eu-central-1");
        settings.ServiceUrl.Should().Be("https://minio.acme.com");
        settings.UsePathStyle.Should().BeTrue();
        settings.Prefix.Should().Be("tenant-1");
        settings.Channels.Should().Equal("prod");
        settings.AccessKeyId.Should().Be("AKIA");
        settings.EncryptedSecretKey.Should().Be("enc-secret");
    }

    [Fact]
    public void HasFeed_is_true_for_an_s3_branding_with_a_bucket()
    {
        BrandingInfo branding = new BrandingInfo(
            Feed: new BrandingFeed(Provider: FeedProvider.S3, S3: new BrandingS3("acme-bucket"))
        );

        branding.HasFeed.Should().BeTrue();
    }

    [Fact]
    public void HasFeed_is_false_for_s3_provider_without_settings()
    {
        BrandingInfo branding = new BrandingInfo(Feed: new BrandingFeed(Provider: FeedProvider.S3));

        branding.HasFeed.Should().BeFalse();
    }
}
