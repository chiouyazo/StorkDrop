using Amazon.S3;
using FluentAssertions;
using StorkDrop.Contracts.Models;
using StorkDrop.Publisher;
using Xunit;

namespace StorkDrop.Registry.S3.IntegrationTests;

[Collection("minio")]
public sealed class S3ChannelScopingTests
{
    private readonly MinioFixture _fixture;

    public S3ChannelScopingTests(MinioFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Channels_are_isolated_each_only_lists_its_own_products()
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

        S3RegistryClient prod = TestData.Client(s3, bucket, "prod");
        S3RegistryClient dev = TestData.Client(s3, bucket, "dev");

        (await prod.GetAllProductsAsync()).Should().ContainSingle(p => p.ProductId == "acme.a");
        (await dev.GetAllProductsAsync()).Should().ContainSingle(p => p.ProductId == "acme.b");

        // The prod client must not be able to resolve a product that only exists in dev.
        (await prod.GetProductManifestAsync("acme.b"))
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task Same_product_can_have_different_latest_per_channel()
    {
        string bucket = await _fixture.CreateBucketAsync();
        using IAmazonS3 s3 = _fixture.CreateAdminClient();
        S3Publisher publisher = new S3Publisher(s3, bucket);

        await publisher.PublishAsync(
            "prod",
            TestData.Manifest("acme.app", "1.0.0"),
            new MemoryStream(TestData.Zip("stable"))
        );
        await publisher.PublishAsync(
            "dev",
            TestData.Manifest("acme.app", "2.0.0-dev"),
            new MemoryStream(TestData.Zip("preview"))
        );

        S3RegistryClient prod = TestData.Client(s3, bucket, "prod");
        S3RegistryClient dev = TestData.Client(s3, bucket, "dev");

        (await prod.GetProductManifestAsync("acme.app"))!.Version.Should().Be("1.0.0");
        (await dev.GetProductManifestAsync("acme.app"))!.Version.Should().Be("2.0.0-dev");
    }

    [Fact]
    public async Task TestConnection_is_true_for_existing_bucket_and_false_for_missing()
    {
        string bucket = await _fixture.CreateBucketAsync();
        using IAmazonS3 s3 = _fixture.CreateAdminClient();

        S3RegistryClient existing = TestData.Client(s3, bucket, "prod");
        S3RegistryClient missing = TestData.Client(
            s3,
            "does-not-exist-" + Guid.NewGuid().ToString("N")[..8],
            "prod"
        );

        (await existing.TestConnectionAsync()).Should().BeTrue();
        (await missing.TestConnectionAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Prefix_isolates_two_logical_stores_in_one_bucket()
    {
        string bucket = await _fixture.CreateBucketAsync();
        using IAmazonS3 s3 = _fixture.CreateAdminClient();

        S3Publisher tenantA = new S3Publisher(s3, bucket, prefix: "tenant-a");
        S3Publisher tenantB = new S3Publisher(s3, bucket, prefix: "tenant-b");
        await tenantA.PublishAsync(
            "prod",
            TestData.Manifest("acme.a", "1.0.0"),
            new MemoryStream(TestData.Zip("a"))
        );
        await tenantB.PublishAsync(
            "prod",
            TestData.Manifest("acme.b", "1.0.0"),
            new MemoryStream(TestData.Zip("b"))
        );

        S3RegistryClient clientA = TestData.Client(s3, bucket, "prod", prefix: "tenant-a");
        S3RegistryClient clientB = TestData.Client(s3, bucket, "prod", prefix: "tenant-b");

        (await clientA.GetAllProductsAsync()).Should().ContainSingle(p => p.ProductId == "acme.a");
        (await clientB.GetAllProductsAsync()).Should().ContainSingle(p => p.ProductId == "acme.b");
    }
}
