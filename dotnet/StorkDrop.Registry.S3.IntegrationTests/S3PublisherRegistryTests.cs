using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using StorkDrop.Contracts.Models;
using StorkDrop.Publisher;
using Xunit;

namespace StorkDrop.Registry.S3.IntegrationTests;

[Collection("minio")]
public sealed class S3PublisherRegistryTests
{
    private readonly MinioFixture _fixture;

    public S3PublisherRegistryTests(MinioFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Publish_makes_product_listable_with_manifest_fields()
    {
        string bucket = await _fixture.CreateBucketAsync();
        using IAmazonS3 s3 = _fixture.CreateAdminClient();
        S3Publisher publisher = new S3Publisher(s3, bucket);

        PublishResult result = await publisher.PublishAsync(
            "prod",
            TestData.Manifest("acme.app", "1.0.0", "STABLE"),
            new MemoryStream(TestData.Zip("payload"))
        );
        ProductManifest stored = result.Manifest;

        stored.ContentSha256.Should().NotBeNullOrWhiteSpace();

        S3RegistryClient client = TestData.Client(s3, bucket, "prod");
        IReadOnlyList<ProductManifest> all = await client.GetAllProductsAsync();

        all.Should().ContainSingle();
        all[0].ProductId.Should().Be("acme.app");
        all[0].Version.Should().Be("1.0.0");
        all[0].BadgeText.Should().Be("STABLE");
        all[0].Title.Should().Be("Title acme.app");
    }

    [Fact]
    public async Task Download_returns_exact_published_bytes_when_checksum_matches()
    {
        string bucket = await _fixture.CreateBucketAsync();
        using IAmazonS3 s3 = _fixture.CreateAdminClient();
        S3Publisher publisher = new S3Publisher(s3, bucket);

        byte[] payload = TestData.Zip("the real package");
        await publisher.PublishAsync(
            "prod",
            TestData.Manifest("acme.app", "2.3.4"),
            new MemoryStream(payload)
        );

        S3RegistryClient client = TestData.Client(s3, bucket, "prod");
        await using Stream stream = await client.DownloadProductAsync("acme.app", "2.3.4");
        byte[] downloaded = await TestData.ReadAllAsync(stream);

        downloaded.Should().Equal(payload);
    }

    [Fact]
    public async Task Download_throws_when_package_was_tampered_after_publish()
    {
        string bucket = await _fixture.CreateBucketAsync();
        using IAmazonS3 s3 = _fixture.CreateAdminClient();
        S3Publisher publisher = new S3Publisher(s3, bucket);

        await publisher.PublishAsync(
            "prod",
            TestData.Manifest("acme.app", "1.0.0"),
            new MemoryStream(TestData.Zip("original"))
        );

        // Overwrite the package object but leave the published .sha256 sidecar untouched.
        S3Layout layout = new S3Layout(null);
        await s3.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = bucket,
                Key = layout.PackageKey("prod", "acme.app", "1.0.0"),
                InputStream = new MemoryStream(TestData.Zip("tampered")),
                AutoCloseStream = true,
            }
        );

        S3RegistryClient client = TestData.Client(s3, bucket, "prod");
        Func<Task> act = async () =>
        {
            await using Stream _ = await client.DownloadProductAsync("acme.app", "1.0.0");
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*mismatch*");
    }

    [Fact]
    public async Task Versions_are_listed_sorted_and_latest_is_the_highest_published_version()
    {
        string bucket = await _fixture.CreateBucketAsync();
        using IAmazonS3 s3 = _fixture.CreateAdminClient();
        S3Publisher publisher = new S3Publisher(s3, bucket);

        await publisher.PublishAsync(
            "prod",
            TestData.Manifest("acme.app", "1.2.0"),
            new MemoryStream(TestData.Zip("v120"))
        );
        await publisher.PublishAsync(
            "prod",
            TestData.Manifest("acme.app", "1.0.0"),
            new MemoryStream(TestData.Zip("v100"))
        );
        await publisher.PublishAsync(
            "prod",
            TestData.Manifest("acme.app", "1.1.0"),
            new MemoryStream(TestData.Zip("v110"))
        );

        S3RegistryClient client = TestData.Client(s3, bucket, "prod");

        // All versions are still discoverable and sorted (listing, independent of "latest").
        IReadOnlyList<string> versions = await client.GetAvailableVersionsAsync("acme.app");
        versions.Should().Equal("1.0.0", "1.1.0", "1.2.0");

        ProductManifest? latest = await client.GetProductManifestAsync("acme.app");
        latest.Should().NotBeNull();
        latest!.Version.Should().Be("1.2.0");

        ProductManifest? specific = await client.GetProductManifestAsync("acme.app", "1.2.0");
        specific.Should().NotBeNull();
        specific!.Version.Should().Be("1.2.0");
    }

    [Fact]
    public async Task Lower_version_does_not_replace_latest_unless_forced()
    {
        string bucket = await _fixture.CreateBucketAsync();
        using IAmazonS3 s3 = _fixture.CreateAdminClient();
        S3Publisher publisher = new S3Publisher(s3, bucket);
        S3RegistryClient client = TestData.Client(s3, bucket, "prod");

        await publisher.PublishAsync(
            "prod",
            TestData.Manifest("acme.app", "2.0.0"),
            new MemoryStream(TestData.Zip("v200"))
        );
        PublishResult hotfix = await publisher.PublishAsync(
            "prod",
            TestData.Manifest("acme.app", "1.9.1"),
            new MemoryStream(TestData.Zip("v191"))
        );

        hotfix.LatestUpdated.Should().BeFalse();
        hotfix.PreviousLatestVersion.Should().Be("2.0.0");
        (await client.GetProductManifestAsync("acme.app"))!.Version.Should().Be("2.0.0");

        PublishResult forced = await publisher.PublishAsync(
            "prod",
            TestData.Manifest("acme.app", "1.9.2"),
            new MemoryStream(TestData.Zip("v192")),
            forceLatest: true
        );

        forced.LatestUpdated.Should().BeTrue();
        (await client.GetProductManifestAsync("acme.app"))!.Version.Should().Be("1.9.2");
    }

    [Fact]
    public async Task Concurrent_publishes_end_with_the_highest_version_as_latest()
    {
        string bucket = await _fixture.CreateBucketAsync();
        using IAmazonS3 s3 = _fixture.CreateAdminClient();
        S3Publisher publisher = new S3Publisher(s3, bucket);

        string[] versions = ["1.0.0", "1.1.0", "1.2.0", "1.3.0"];
        await Task.WhenAll(
            versions.Select(version =>
                publisher.PublishAsync(
                    "prod",
                    TestData.Manifest("acme.app", version),
                    new MemoryStream(TestData.Zip(version))
                )
            )
        );

        S3RegistryClient client = TestData.Client(s3, bucket, "prod");
        (await client.GetProductManifestAsync("acme.app"))!.Version.Should().Be("1.3.0");
    }

    [Fact]
    public async Task Invalid_manifest_is_rejected_before_anything_is_uploaded()
    {
        string bucket = await _fixture.CreateBucketAsync();
        using IAmazonS3 s3 = _fixture.CreateAdminClient();
        S3Publisher publisher = new S3Publisher(s3, bucket);

        Func<Task> act = () =>
            publisher.PublishAsync(
                "prod",
                TestData.Manifest("acme.app", "1.0.0") with
                {
                    Title = "",
                },
                new MemoryStream(TestData.Zip("x"))
            );

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Title*");
        (await TestData.Client(s3, bucket, "prod").GetAllProductsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Download_fails_closed_when_manifest_has_no_checksum()
    {
        string bucket = await _fixture.CreateBucketAsync();
        using IAmazonS3 s3 = _fixture.CreateAdminClient();
        S3Layout layout = new S3Layout(null);

        // Upload a version manifest with no contentSha256 (raw, bypassing the publisher) + a package.
        ProductManifest manifest = TestData.Manifest("acme.app", "1.0.0");
        await s3.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = bucket,
                Key = layout.VersionManifestKey("prod", "acme.app", "1.0.0"),
                ContentBody = JsonSerializer.Serialize(manifest, S3Json.Options),
                ContentType = "application/json",
            }
        );
        await s3.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = bucket,
                Key = layout.PackageKey("prod", "acme.app", "1.0.0"),
                InputStream = new MemoryStream(TestData.Zip("unverified")),
                AutoCloseStream = true,
            }
        );

        S3RegistryClient client = TestData.Client(s3, bucket, "prod");
        Func<Task> act = async () =>
        {
            await using Stream _ = await client.DownloadProductAsync("acme.app", "1.0.0");
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*contentSha256*");
    }

    [Fact]
    public async Task Unknown_product_and_version_return_null()
    {
        string bucket = await _fixture.CreateBucketAsync();
        using IAmazonS3 s3 = _fixture.CreateAdminClient();

        S3RegistryClient client = TestData.Client(s3, bucket, "prod");

        (await client.GetProductManifestAsync("nope")).Should().BeNull();
        (await client.GetProductManifestAsync("nope", "9.9.9")).Should().BeNull();
        (await client.GetAllProductsAsync()).Should().BeEmpty();
    }
}
