using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using StorkDrop.Contracts.Models;
using Xunit;

namespace StorkDrop.Registry.S3.IntegrationTests;

/// <summary>
/// Spins up a real MinIO (S3-compatible) server in a container for the whole test collection. Every
/// test creates its own bucket for isolation.
/// </summary>
public sealed class MinioFixture : IAsyncLifetime
{
    private const string RootUser = "storkdroptest";
    private const string RootPassword = "storkdroptest-secret";
    private const ushort MinioPort = 9000;

    private IContainer? _container;

    public string ServiceUrl { get; private set; } = string.Empty;

    public string AccessKey { get; private set; } = RootUser;

    public string SecretKey { get; private set; } = RootPassword;

    public async Task InitializeAsync()
    {
        // Environment override: point at an already-running MinIO/S3-compatible endpoint. Used where
        // the Docker daemon is not directly drivable by Testcontainers (e.g. remote-only over SSH).
        string? endpoint = Environment.GetEnvironmentVariable("STORKDROP_TEST_S3_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            ServiceUrl = endpoint;
            AccessKey =
                Environment.GetEnvironmentVariable("STORKDROP_TEST_S3_ACCESSKEY") ?? RootUser;
            SecretKey =
                Environment.GetEnvironmentVariable("STORKDROP_TEST_S3_SECRETKEY") ?? RootPassword;
            return;
        }

        _container = new ContainerBuilder()
            .WithImage("minio/minio:latest")
            .WithEnvironment("MINIO_ROOT_USER", RootUser)
            .WithEnvironment("MINIO_ROOT_PASSWORD", RootPassword)
            .WithCommand("server", "/data")
            .WithPortBinding(MinioPort, true)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r =>
                        r.ForPort(MinioPort).ForPath("/minio/health/live")
                    )
            )
            .Build();

        await _container.StartAsync();
        ServiceUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(MinioPort)}";
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    public IAmazonS3 CreateAdminClient()
    {
        AmazonS3Config config = new AmazonS3Config
        {
            ServiceURL = ServiceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
        };
        return new AmazonS3Client(new BasicAWSCredentials(AccessKey, SecretKey), config);
    }

    public async Task<string> CreateBucketAsync()
    {
        string bucket = "sd-" + Guid.NewGuid().ToString("N")[..12];
        using IAmazonS3 s3 = CreateAdminClient();
        await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
        return bucket;
    }

    /// <summary>Feed settings whose EncryptedSecretKey is the plaintext secret (paired with a passthrough encryption service).</summary>
    public S3FeedSettings Settings(
        string bucket,
        string? prefix = null,
        string[]? channels = null
    ) =>
        new S3FeedSettings(
            Bucket: bucket,
            Region: "us-east-1",
            ServiceUrl: ServiceUrl,
            UsePathStyle: true,
            AccessKeyId: AccessKey,
            EncryptedSecretKey: SecretKey,
            Prefix: prefix,
            Channels: channels
        );
}

[CollectionDefinition("minio")]
public sealed class MinioCollection : ICollectionFixture<MinioFixture> { }
