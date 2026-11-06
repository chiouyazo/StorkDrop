using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Registry.S3;
using Xunit;

namespace StorkDrop.Tests.Registry;

public sealed class S3DownloadTempFileTests
{
    private static readonly byte[] Package = "zip-bytes"u8.ToArray();

    private static S3RegistryClient Create(string? sha256, bool allowUnverified = false)
    {
        IAmazonS3 s3 = Substitute.For<IAmazonS3>();
        ProductManifest manifest = new ProductManifest(
            "p",
            "P",
            "1.0.0",
            new DateOnly(2026, 1, 1),
            InstallType.Plugin,
            ContentSha256: sha256
        );
        s3.GetObjectAsync(
                Arg.Is<GetObjectRequest>(r => r.Key.EndsWith("manifest.json")),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
                Task.FromResult(
                    new GetObjectResponse
                    {
                        ResponseStream = new MemoryStream(
                            JsonSerializer.SerializeToUtf8Bytes(manifest, S3Json.Options)
                        ),
                    }
                )
            );
        s3.GetObjectAsync(
                Arg.Is<GetObjectRequest>(r => r.Key.EndsWith(".zip")),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
                Task.FromResult(
                    new GetObjectResponse { ResponseStream = new MemoryStream(Package) }
                )
            );

        return new S3RegistryClient(
            s3,
            "bucket",
            new S3Layout(null),
            "prod",
            NullLogger<S3RegistryClient>.Instance,
            allowUnverified
        );
    }

    private static string DownloadsDirectory => Path.Combine(StorkPaths.TempDir, "downloads");

    private static string[] Snapshot() =>
        Directory.Exists(DownloadsDirectory) ? Directory.GetFiles(DownloadsDirectory) : [];

    [Fact]
    public async Task Download_StagesThePackageUnderTheStorkDropTempDirectory_AndRemovesItOnDispose()
    {
        string[] before = Snapshot();
        string checksum = Convert
            .ToHexString(System.Security.Cryptography.SHA256.HashData(Package))
            .ToLowerInvariant();

        FileStream stream = (FileStream)await Create(checksum).DownloadProductAsync("p", "1.0.0");
        string staged = stream.Name;

        staged
            .Should()
            .StartWith(DownloadsDirectory + Path.DirectorySeparatorChar)
            .And.Contain("s3-");
        File.Exists(staged).Should().BeTrue();

        await stream.DisposeAsync();

        File.Exists(staged).Should().BeFalse();
        Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Download_ChecksumMismatch_LeavesNoTempFileBehind()
    {
        string[] before = Snapshot();

        Func<Task> act = () => Create(new string('0', 64)).DownloadProductAsync("p", "1.0.0");

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Checksum mismatch*");
        Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Download_WithoutChecksum_IsRefusedAndLeavesNoTempFile()
    {
        string[] before = Snapshot();

        Func<Task> act = () => Create(null).DownloadProductAsync("p", "1.0.0");

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*no contentSha256*");
        Snapshot().Should().BeEquivalentTo(before);
    }
}
