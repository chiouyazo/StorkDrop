using System.IO.Compression;
using FluentAssertions;
using StorkDrop.Contracts.Models;
using Xunit;

namespace StorkDrop.Tests.Installer;

[Collection(StorkPathsCollection.Name)]
public sealed class InstallationEngineUpdateBackupTests
{
    private static readonly Dictionary<string, string> NewPackage = new Dictionary<string, string>
    {
        ["app.dll"] = "new-app",
    };

    private static async Task<List<string>> BackupEntriesAsync(EngineTestHarness harness)
    {
        IReadOnlyList<string> backups = await harness.Backups.ListBackupsAsync(harness.ProductId);
        backups.Should().ContainSingle();
        using ZipArchive archive = ZipFile.OpenRead(backups[0]);
        return archive.Entries.Select(e => e.FullName).Order().ToList();
    }

    [Fact]
    public async Task Update_WithFileManifest_BacksUpTrackedAndExcludedFiles()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.WriteInstalledFile("app.dll", "old-app");
        harness.WriteInstalledFile("config.json", "customer-config");
        harness.WriteInstalledFile("untracked.log", "log");
        await harness.WriteFileManifestAsync("app.dll");
        harness.SetPackage(NewPackage);

        await harness.Engine.UpdateAsync(
            harness.Installed,
            harness.Manifest("2.0.0", excludeFiles: ["config.json"]),
            harness.Options,
            new CallbackProgress(_ => { }),
            CancellationToken.None
        );

        (await BackupEntriesAsync(harness)).Should().Equal("app.dll", "config.json");
    }

    [Fact]
    public async Task Update_WithoutFileManifest_BacksUpTheInstallFolderInsteadOfAnEmptyZip()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.WriteInstalledFile("app.dll", "old-app");
        harness.WriteInstalledFile("sub/data.bin", "data");
        harness.SetPackage(NewPackage);

        await harness.Engine.UpdateAsync(
            harness.Installed,
            harness.Manifest("2.0.0"),
            harness.Options,
            new CallbackProgress(_ => { }),
            CancellationToken.None
        );

        (await BackupEntriesAsync(harness)).Should().Equal("app.dll", "sub/data.bin");
    }

    [Fact]
    public async Task Update_WithoutFileManifestInSharedLocation_DoesNotBackUpForeignFiles()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.WriteInstalledFile("foreign.dll", "other product");
        harness.SetPackage(NewPackage);

        await harness.Engine.UpdateAsync(
            harness.Installed,
            harness.Manifest("2.0.0") with
            {
                SharedInstallLocation = true,
            },
            harness.Options,
            new CallbackProgress(_ => { }),
            CancellationToken.None
        );

        (await harness.Backups.ListBackupsAsync(harness.ProductId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Update_BacksUpStorkMetadataSeparately()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.WriteInstalledFile("app.dll", "old-app");
        await harness.WriteFileManifestAsync("app.dll");
        harness.WriteMetadataFile("manifest.json", "old-manifest");
        harness.SetPackage(NewPackage);

        await harness.Engine.UpdateAsync(
            harness.Installed,
            harness.Manifest("2.0.0"),
            harness.Options,
            new CallbackProgress(_ => { }),
            CancellationToken.None
        );

        IReadOnlyList<string> metadataBackups = await harness.Backups.ListBackupsAsync(
            $"{harness.ProductId}_{harness.UniqueId}.stork"
        );
        metadataBackups.Should().ContainSingle();
        using ZipArchive archive = ZipFile.OpenRead(metadataBackups[0]);
        archive
            .Entries.Select(e => e.FullName)
            .Should()
            .Contain($"metadata/{harness.ProductId}_{harness.UniqueId}/manifest.json")
            .And.Contain($"{harness.ProductId}_{harness.UniqueId}.files.json");
    }
}
