using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using NSubstitute;
using StorkDrop.Contracts.Models;
using StorkDrop.Publisher;
using StorkDrop.Registry.S3;
using Xunit;

namespace StorkDrop.Tests.Publisher;

public sealed class S3PublisherTests
{
    private const string LatestKey = "prod/acme.app/manifest.json";

    private static ProductManifest Manifest(string version, string title = "Acme") =>
        new ProductManifest(
            ProductId: "acme.app",
            Title: title,
            Version: version,
            ReleaseDate: new DateOnly(2026, 1, 1),
            InstallType: InstallType.Suite,
            Description: "d"
        );

    private static MemoryStream Zip() => new MemoryStream([1, 2, 3]);

    private static ProductManifest ReadLatest(FakeS3 fake) =>
        JsonSerializer.Deserialize<ProductManifest>(fake.Objects[LatestKey].Body, S3Json.Options)!;

    [Fact]
    public async Task PublishAsync_NoRoot_WritesRootWithIfNoneMatch()
    {
        FakeS3 fake = new FakeS3();
        S3Publisher publisher = new S3Publisher(fake.Client, "bucket");

        PublishResult result = await publisher.PublishAsync("prod", Manifest("1.0.0"), Zip());

        result.LatestUpdated.Should().BeTrue();
        result.PreviousLatestVersion.Should().BeNull();
        ReadLatest(fake).Version.Should().Be("1.0.0");
        fake.Puts.Single(p => p.Key == LatestKey).IfNoneMatch.Should().Be("*");
    }

    [Fact]
    public async Task PublishAsync_HigherVersion_ReplacesRootWithIfMatch()
    {
        FakeS3 fake = new FakeS3();
        S3Publisher publisher = new S3Publisher(fake.Client, "bucket");
        await publisher.PublishAsync("prod", Manifest("1.0.0"), Zip());
        string etagBefore = fake.Objects[LatestKey].ETag;

        PublishResult result = await publisher.PublishAsync("prod", Manifest("1.1.0"), Zip());

        result.LatestUpdated.Should().BeTrue();
        result.PreviousLatestVersion.Should().Be("1.0.0");
        ReadLatest(fake).Version.Should().Be("1.1.0");
        fake.Puts.Last(p => p.Key == LatestKey).IfMatch.Should().Be(etagBefore);
    }

    [Fact]
    public async Task PublishAsync_LowerVersion_KeepsRootButStoresVersion()
    {
        FakeS3 fake = new FakeS3();
        S3Publisher publisher = new S3Publisher(fake.Client, "bucket");
        await publisher.PublishAsync("prod", Manifest("2.0.0"), Zip());

        PublishResult result = await publisher.PublishAsync("prod", Manifest("1.9.1"), Zip());

        result.LatestUpdated.Should().BeFalse();
        result.PreviousLatestVersion.Should().Be("2.0.0");
        ReadLatest(fake).Version.Should().Be("2.0.0");
        fake.Objects.Should().ContainKey("prod/acme.app/versions/1.9.1/manifest.json");
        fake.Puts.Count(p => p.Key == LatestKey).Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_LowerVersionForced_ReplacesRoot()
    {
        FakeS3 fake = new FakeS3();
        S3Publisher publisher = new S3Publisher(fake.Client, "bucket");
        await publisher.PublishAsync("prod", Manifest("2.0.0"), Zip());

        PublishResult result = await publisher.PublishAsync(
            "prod",
            Manifest("1.9.1"),
            Zip(),
            forceLatest: true
        );

        result.LatestUpdated.Should().BeTrue();
        ReadLatest(fake).Version.Should().Be("1.9.1");
    }

    [Fact]
    public async Task PublishAsync_SameVersionAgain_RefreshesRootCopy()
    {
        FakeS3 fake = new FakeS3();
        S3Publisher publisher = new S3Publisher(fake.Client, "bucket");
        PublishResult first = await publisher.PublishAsync("prod", Manifest("1.0.0"), Zip());

        PublishResult second = await publisher.PublishAsync(
            "prod",
            Manifest("1.0.0"),
            new MemoryStream([9, 9, 9])
        );

        second.LatestUpdated.Should().BeTrue();
        ReadLatest(fake).ContentSha256.Should().Be(second.Manifest.ContentSha256);
        second.Manifest.ContentSha256.Should().NotBe(first.Manifest.ContentSha256);
    }

    [Fact]
    public async Task PublishAsync_RootChangedConcurrently_RetriesAndKeepsHigherVersion()
    {
        FakeS3 fake = new FakeS3();
        S3Publisher publisher = new S3Publisher(fake.Client, "bucket");
        await publisher.PublishAsync("prod", Manifest("1.0.0"), Zip());
        fake.BeforeConditionalPut = () =>
        {
            fake.SetObject(LatestKey, JsonSerializer.Serialize(Manifest("3.0.0"), S3Json.Options));
            fake.BeforeConditionalPut = null;
        };

        PublishResult result = await publisher.PublishAsync("prod", Manifest("2.0.0"), Zip());

        result.LatestUpdated.Should().BeFalse();
        result.PreviousLatestVersion.Should().Be("3.0.0");
        ReadLatest(fake).Version.Should().Be("3.0.0");
    }

    [Fact]
    public async Task PublishAsync_RootChangedConcurrentlyToLower_RetriesAndReplaces()
    {
        FakeS3 fake = new FakeS3();
        S3Publisher publisher = new S3Publisher(fake.Client, "bucket");
        await publisher.PublishAsync("prod", Manifest("1.0.0"), Zip());
        fake.BeforeConditionalPut = () =>
        {
            fake.SetObject(LatestKey, JsonSerializer.Serialize(Manifest("1.5.0"), S3Json.Options));
            fake.BeforeConditionalPut = null;
        };

        PublishResult result = await publisher.PublishAsync("prod", Manifest("2.0.0"), Zip());

        result.LatestUpdated.Should().BeTrue();
        result.PreviousLatestVersion.Should().Be("1.5.0");
        ReadLatest(fake).Version.Should().Be("2.0.0");
    }

    [Fact]
    public async Task PublishAsync_RootKeepsChanging_ThrowsAfterBoundedAttempts()
    {
        FakeS3 fake = new FakeS3();
        S3Publisher publisher = new S3Publisher(fake.Client, "bucket");
        await publisher.PublishAsync("prod", Manifest("1.0.0"), Zip());
        int counter = 0;
        fake.BeforeConditionalPut = () =>
            fake.SetObject(
                LatestKey,
                JsonSerializer.Serialize(Manifest($"1.0.{++counter}"), S3Json.Options)
            );

        Func<Task> act = () => publisher.PublishAsync("prod", Manifest("2.0.0"), Zip());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*kept changing*");
        counter.Should().BeLessThan(10);
    }

    [Fact]
    public async Task PublishAsync_CorruptRoot_IsReplaced()
    {
        FakeS3 fake = new FakeS3();
        fake.SetObject(LatestKey, "{ not json");
        S3Publisher publisher = new S3Publisher(fake.Client, "bucket");

        PublishResult result = await publisher.PublishAsync("prod", Manifest("1.0.0"), Zip());

        result.LatestUpdated.Should().BeTrue();
        ReadLatest(fake).Version.Should().Be("1.0.0");
    }

    [Fact]
    public async Task PublishAsync_InvalidManifest_ThrowsBeforeAnyUpload()
    {
        FakeS3 fake = new FakeS3();
        S3Publisher publisher = new S3Publisher(fake.Client, "bucket");

        Func<Task> act = () => publisher.PublishAsync("prod", Manifest("1.0.0", title: ""), Zip());

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Title*");
        fake.Puts.Should().BeEmpty();
    }

    private sealed class StoredObject
    {
        public string Body { get; init; } = string.Empty;

        public string ETag { get; init; } = string.Empty;
    }

    private sealed class FakeS3
    {
        private int _etagCounter;

        public FakeS3()
        {
            Client = Substitute.For<IAmazonS3>();
            Client
                .PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
                .Returns(call => Put(call.Arg<PutObjectRequest>()));
            Client
                .GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
                .Returns(call => Get(call.Arg<GetObjectRequest>()));
        }

        public IAmazonS3 Client { get; }

        public Dictionary<string, StoredObject> Objects { get; } = [];

        public List<PutObjectRequest> Puts { get; } = [];

        public Action? BeforeConditionalPut { get; set; }

        public void SetObject(string key, string body) =>
            Objects[key] = new StoredObject { Body = body, ETag = $"\"e{++_etagCounter}\"" };

        private Task<PutObjectResponse> Put(PutObjectRequest request)
        {
            Puts.Add(request);
            bool conditional = request.IfMatch is not null || request.IfNoneMatch is not null;
            if (conditional)
                BeforeConditionalPut?.Invoke();

            if (request.IfNoneMatch == "*" && Objects.ContainsKey(request.Key))
                throw Failure(HttpStatusCode.PreconditionFailed);
            if (
                request.IfMatch is not null
                && (
                    !Objects.TryGetValue(request.Key, out StoredObject? existing)
                    || existing.ETag != request.IfMatch
                )
            )
                throw Failure(HttpStatusCode.PreconditionFailed);

            string body = request.ContentBody ?? string.Empty;
            if (request.FilePath is not null)
                body = Convert.ToHexString(File.ReadAllBytes(request.FilePath));
            SetObject(request.Key, body);
            return Task.FromResult(new PutObjectResponse());
        }

        private Task<GetObjectResponse> Get(GetObjectRequest request)
        {
            if (!Objects.TryGetValue(request.Key, out StoredObject? stored))
                throw Failure(HttpStatusCode.NotFound);

            return Task.FromResult(
                new GetObjectResponse
                {
                    ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(stored.Body)),
                    ETag = stored.ETag,
                }
            );
        }

        private static AmazonS3Exception Failure(HttpStatusCode status) =>
            new AmazonS3Exception(status.ToString()) { StatusCode = status };
    }
}
