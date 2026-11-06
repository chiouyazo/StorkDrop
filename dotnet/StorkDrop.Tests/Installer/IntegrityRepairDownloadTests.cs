using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class IntegrityRepairDownloadTests : IDisposable
{
    private readonly string _productId = "integrity-test-" + Guid.NewGuid().ToString("N")[..10];
    private readonly string _uniqueId = "u1234567";
    private readonly TestDirectory _install = new TestDirectory();
    private readonly IFeedRegistry _feeds = Substitute.For<IFeedRegistry>();
    private readonly IRegistryClient _client = Substitute.For<IRegistryClient>();

    public IntegrityRepairDownloadTests()
    {
        _feeds.GetClient("feed").Returns(_client);
    }

    public void Dispose()
    {
        _install.Dispose();
        string manifest = StorkPaths.FileManifestPath(_productId, _uniqueId);
        foreach (string file in new[] { manifest, AtomicFile.BackupPath(manifest) })
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    private IntegrityService CreateService() =>
        new IntegrityService(
            _feeds,
            NullLogger<IntegrityService>.Instance,
            new PackageDownloader(
                NullLogger.Instance,
                new PackageDownloadOptions
                {
                    Delay = (_, _) => Task.CompletedTask,
                    AvailableBytes = _ => null,
                }
            )
        );

    private InstalledProduct Product() =>
        new InstalledProduct(
            _productId,
            "default",
            "Integrity Test",
            "1.0.0",
            _install.Path,
            DateTime.UtcNow,
            FeedId: "feed",
            InstanceUniqueId: _uniqueId
        );

    private async Task<byte[]> ArrangeInstalledFileAndPackageAsync()
    {
        string manifestPath = StorkPaths.FileManifestPath(_productId, _uniqueId);
        await FileManifestStore.WriteAsync(manifestPath, [new TrackedFile("a.txt", "x", 1)]);
        await File.WriteAllTextAsync(Path.Combine(_install.Path, "a.txt"), "damaged");
        byte[] package = EngineFixture.Zip(("a.txt", "original"));
        _client
            .DownloadProductAsync(_productId, "1.0.0", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(package)));
        return package;
    }

    private void PublishManifest(string? sha256) =>
        _client
            .GetProductManifestAsync(_productId, "1.0.0", Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<ProductManifest?>(
                    new ProductManifest(
                        _productId,
                        "Integrity Test",
                        "1.0.0",
                        new DateOnly(2026, 1, 1),
                        InstallType.Plugin,
                        ContentSha256: sha256
                    )
                )
            );

    [Fact]
    public async Task Repair_RestoresTheFile_WhenThePackageMatchesThePublishedChecksum()
    {
        byte[] package = await ArrangeInstalledFileAndPackageAsync();
        PublishManifest(EngineFixture.Sha256(package));

        int repaired = await CreateService().RepairAsync(Product(), ["a.txt"]);

        repaired.Should().Be(1);
        (await File.ReadAllTextAsync(Path.Combine(_install.Path, "a.txt"))).Should().Be("original");
    }

    [Fact]
    public async Task Repair_RefusesATamperedPackage_AndLeavesTheInstalledFileUntouched()
    {
        await ArrangeInstalledFileAndPackageAsync();
        PublishManifest(new string('0', 64));

        Func<Task> act = () => CreateService().RepairAsync(Product(), ["a.txt"]);

        await act.Should().ThrowAsync<PackageIntegrityException>();
        (await File.ReadAllTextAsync(Path.Combine(_install.Path, "a.txt"))).Should().Be("damaged");
    }

    [Fact]
    public async Task Repair_WithoutPublishedChecksum_StillWorks()
    {
        await ArrangeInstalledFileAndPackageAsync();
        PublishManifest(null);

        int repaired = await CreateService().RepairAsync(Product(), ["a.txt"]);

        repaired.Should().Be(1);
    }

    [Fact]
    public async Task Repair_ReportsTheRealDownloadFailure()
    {
        await ArrangeInstalledFileAndPackageAsync();
        PublishManifest(null);
        _client
            .DownloadProductAsync(_productId, "1.0.0", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ =>
                throw new HttpRequestException("x", null, HttpStatusCode.Forbidden)
            );

        Func<Task> act = () => CreateService().RepairAsync(Product(), ["a.txt"]);

        (await act.Should().ThrowAsync<PackageDownloadException>())
            .Which.Message.Should()
            .Contain("403");
    }

    [Fact]
    public async Task Repair_CleansUpItsWorkDirectory_AlsoOnFailure()
    {
        await ArrangeInstalledFileAndPackageAsync();
        PublishManifest(new string('0', 64));
        string repairRoot = Path.Combine(StorkPaths.TempDir, "repair");
        int before = Directory.Exists(repairRoot) ? Directory.GetDirectories(repairRoot).Length : 0;

        Func<Task> act = () => CreateService().RepairAsync(Product(), ["a.txt"]);
        await act.Should().ThrowAsync<PackageIntegrityException>();

        int after = Directory.Exists(repairRoot) ? Directory.GetDirectories(repairRoot).Length : 0;
        after.Should().Be(before);
    }
}
