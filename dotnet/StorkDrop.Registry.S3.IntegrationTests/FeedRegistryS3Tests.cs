using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Publisher;
using StorkDrop.Registry;
using Xunit;

namespace StorkDrop.Registry.S3.IntegrationTests;

/// <summary>
/// End-to-end through <see cref="FeedRegistry"/>: a single S3 feed configuration expands into one
/// client per visible channel, exactly as the app composes it via DI.
/// </summary>
[Collection("minio")]
public sealed class FeedRegistryS3Tests
{
    private readonly MinioFixture _fixture;

    public FeedRegistryS3Tests(MinioFixture fixture) => _fixture = fixture;

    private FeedRegistry BuildRegistry(AppConfiguration config)
    {
        IRegistryClientFactory s3Factory = new S3RegistryClientFactory(
            new StaticKeysCredentialProvider(),
            NullLoggerFactory.Instance
        );
        return new FeedRegistry(
            new FixedConfigurationService(config),
            new PassthroughEncryptionService(),
            [s3Factory],
            NullLoggerFactory.Instance
        );
    }

    [Fact]
    public async Task Reload_expands_one_S3_feed_into_visible_channels_and_serves_each()
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

        FeedConfiguration feed = new FeedConfiguration(
            Id: "acme",
            Name: "Acme",
            Url: "s3://acme",
            Repository: null,
            Username: null,
            EncryptedPassword: null,
            PluginId: null,
            Provider: FeedProvider.S3,
            S3: _fixture.Settings(bucket)
        );
        AppConfiguration config = new AppConfiguration(
            Feeds: [feed],
            AutoStart: false,
            AutoCheckForUpdates: false,
            CheckInterval: TimeSpan.FromHours(4),
            VisibleChannels: ["prod", "dev"]
        );

        using FeedRegistry registry = BuildRegistry(config);
        await registry.ReloadAsync();

        registry.GetFeeds().Select(f => f.Id).Should().Contain(["acme:prod", "acme:dev"]);

        IReadOnlyList<ProductManifest> prodProducts = await registry
            .GetClient("acme:prod")
            .GetAllProductsAsync();
        prodProducts.Should().ContainSingle(p => p.ProductId == "acme.a");

        IReadOnlyList<ProductManifest> devProducts = await registry
            .GetClient("acme:dev")
            .GetAllProductsAsync();
        devProducts.Should().ContainSingle(p => p.ProductId == "acme.b");

        // Base id resolves to a channel client via the composite-id fallback.
        registry.GetClient("acme").Should().NotBeNull();

        (await registry.TestConnectionAsync("acme:prod")).Should().BeTrue();
    }

    [Fact]
    public async Task Reload_with_default_visible_channels_only_exposes_prod()
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

        FeedConfiguration feed = new FeedConfiguration(
            Id: "acme",
            Name: "Acme",
            Url: "s3://acme",
            Repository: null,
            Username: null,
            EncryptedPassword: null,
            PluginId: null,
            Provider: FeedProvider.S3,
            S3: _fixture.Settings(bucket)
        );
        // No VisibleChannels configured -> defaults to prod only.
        AppConfiguration config = new AppConfiguration(
            Feeds: [feed],
            AutoStart: false,
            AutoCheckForUpdates: false,
            CheckInterval: TimeSpan.FromHours(4)
        );

        using FeedRegistry registry = BuildRegistry(config);
        await registry.ReloadAsync();

        List<string> feedIds = registry.GetFeeds().Select(f => f.Id).ToList();
        feedIds.Should().ContainSingle().Which.Should().Be("acme:prod");
    }
}
