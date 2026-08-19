using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Publisher;
using StorkDrop.Registry;
using Xunit;

namespace StorkDrop.Registry.S3.IntegrationTests;

/// <summary>
/// Proves the white-label branding path end to end: a branded S3 <see cref="BrandingFeed"/> mapped to a
/// locked <see cref="FeedConfiguration"/> serves products through the real <see cref="FeedRegistry"/>.
/// </summary>
[Collection("minio")]
public sealed class BrandingS3E2ETests
{
    private readonly MinioFixture _fixture;

    public BrandingS3E2ETests(MinioFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Branded_s3_edition_serves_prod_products_through_the_registry()
    {
        string bucket = await _fixture.CreateBucketAsync();
        using IAmazonS3 s3 = _fixture.CreateAdminClient();
        S3Publisher publisher = new S3Publisher(s3, bucket);
        await publisher.PublishAsync(
            "prod",
            TestData.Manifest("acme.a", "1.0.0"),
            new MemoryStream(TestData.Zip("a"))
        );
        await publisher.PublishAsync(
            "dev",
            TestData.Manifest("acme.b", "0.9.0-dev"),
            new MemoryStream(TestData.Zip("b"))
        );

        // Vendor-fixed branding (no secret baked in); customer supplies access key + secret.
        BrandingFeed brandFeed = new BrandingFeed(
            Name: "Acme Products",
            Provider: FeedProvider.S3,
            S3: new BrandingS3(
                Bucket: bucket,
                Region: "us-east-1",
                ServiceUrl: _fixture.ServiceUrl,
                UsePathStyle: true,
                Channels: ["prod"]
            )
        );

        // Passthrough encryption in tests => the "encrypted" secret is the plaintext secret.
        S3FeedSettings? s3Settings = BrandingFeedMapper.ToS3Settings(
            brandFeed,
            _fixture.AccessKey,
            _fixture.SecretKey
        );
        s3Settings.Should().NotBeNull();

        FeedConfiguration feed = new FeedConfiguration(
            Id: Branding.WhitelabelFeedId,
            Name: brandFeed.Name!,
            Url: $"s3://{bucket}",
            Repository: null,
            Username: null,
            EncryptedPassword: null,
            PluginId: null,
            Provider: FeedProvider.S3,
            S3: s3Settings
        );
        AppConfiguration config = new AppConfiguration(
            Feeds: [feed],
            AutoStart: false,
            AutoCheckForUpdates: false,
            CheckInterval: TimeSpan.FromHours(4),
            VisibleChannels: ["prod"]
        );

        IRegistryClientFactory s3Factory = new S3RegistryClientFactory(
            new StaticKeysCredentialProvider(),
            NullLoggerFactory.Instance
        );
        using FeedRegistry registry = new FeedRegistry(
            new FixedConfigurationService(config),
            new PassthroughEncryptionService(),
            [s3Factory],
            NullLoggerFactory.Instance
        );
        await registry.ReloadAsync();

        // Branded editions expose only prod; dev must not appear.
        registry
            .GetFeeds()
            .Select(f => f.Id)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be($"{Branding.WhitelabelFeedId}:prod");

        IReadOnlyList<ProductManifest> products = await registry
            .GetClient(Branding.WhitelabelFeedId)
            .GetAllProductsAsync();
        products.Should().ContainSingle(p => p.ProductId == "acme.a");
    }
}
