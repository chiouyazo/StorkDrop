using FluentAssertions;
using StorkDrop.Contracts.Models;
using Xunit;

namespace StorkDrop.Tests.Installer;

[Collection(StorkPathsCollection.Name)]
public sealed class InstallationEngineUpdateRollbackTests
{
    private static readonly Dictionary<string, string> NewPackage = new Dictionary<string, string>
    {
        ["app.dll"] = "new-app",
        ["extra.dll"] = "new-extra",
    };

    [Fact]
    public async Task Update_CancelledAfterOldFilesWereRemoved_RestoresPreviousFiles()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.WriteInstalledFile("app.dll", "old-app");
        harness.WriteInstalledFile("lib/helper.dll", "old-helper");
        await harness.WriteFileManifestAsync("app.dll", "lib/helper.dll");
        harness.SetPackage(NewPackage);
        using CancellationTokenSource cts = new CancellationTokenSource();
        CallbackProgress progress = new CallbackProgress(p =>
        {
            if (p.Message.StartsWith("Copying files to", StringComparison.Ordinal))
                cts.Cancel();
        });

        Func<Task> update = () =>
            harness.Engine.UpdateAsync(
                harness.Installed,
                harness.Manifest("2.0.0"),
                harness.Options,
                progress,
                cts.Token
            );

        await update.Should().ThrowAsync<OperationCanceledException>();
        cts.IsCancellationRequested.Should().BeTrue();
        File.ReadAllText(harness.InstalledFile("app.dll")).Should().Be("old-app");
        File.ReadAllText(harness.InstalledFile("lib/helper.dll")).Should().Be("old-helper");
        File.Exists(harness.InstalledFile("extra.dll")).Should().BeFalse();
    }

    [Fact]
    public async Task Update_RolledBack_RestoresStorkMetadataAndFileManifest()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.WriteInstalledFile("app.dll", "old-app");
        await harness.WriteFileManifestAsync("app.dll");
        harness.WriteMetadataFile("manifest.json", "old-manifest");
        harness.WriteMetadataFile("plugins/old-plugin.dll", "old-plugin");
        byte[] fileManifestBefore = File.ReadAllBytes(harness.FileManifestFile);
        harness.SetPackage(NewPackage);
        using CancellationTokenSource cts = new CancellationTokenSource();
        bool metadataGoneWhenCopyStarted = false;
        CallbackProgress progress = new CallbackProgress(p =>
        {
            if (p.Message.StartsWith("Copying files to", StringComparison.Ordinal))
            {
                metadataGoneWhenCopyStarted = !Directory.Exists(harness.MetadataDir);
                File.WriteAllText(harness.FileManifestFile, "overwritten");
                cts.Cancel();
            }
        });

        Func<Task> update = () =>
            harness.Engine.UpdateAsync(
                harness.Installed,
                harness.Manifest("2.0.0"),
                harness.Options,
                progress,
                cts.Token
            );

        await update.Should().ThrowAsync<Exception>();
        metadataGoneWhenCopyStarted.Should().BeTrue();
        File.ReadAllText(Path.Combine(harness.MetadataDir, "manifest.json"))
            .Should()
            .Be("old-manifest");
        File.ReadAllText(Path.Combine(harness.MetadataDir, "plugins", "old-plugin.dll"))
            .Should()
            .Be("old-plugin");
        File.ReadAllBytes(harness.FileManifestFile).Should().Equal(fileManifestBefore);
    }

    [Fact]
    public async Task Update_FailingWithPluginsInMetadataDir_CallsPrepareForRollback()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        string marker = RollbackProbePlugin.MarkerPath(harness.ProductId);
        try
        {
            harness.WriteInstalledFile("app.dll", "old-app");
            await harness.WriteFileManifestAsync("app.dll");
            string pluginDir = Path.Combine(harness.MetadataDir, "plugins");
            Directory.CreateDirectory(pluginDir);
            string probeAssembly = typeof(RollbackProbePlugin).Assembly.Location;
            File.Copy(probeAssembly, Path.Combine(pluginDir, Path.GetFileName(probeAssembly)));
            ProductManifest manifest = harness.Manifest("2.0.0") with
            {
                Plugins =
                [
                    new StorkPluginInfo(
                        "plugins/" + Path.GetFileName(probeAssembly),
                        typeof(RollbackProbePlugin).FullName!
                    ),
                ],
            };
            harness.SetPackage(NewPackage);

            Func<Task> update = () =>
                harness.Engine.UpdateAsync(
                    harness.Installed,
                    manifest,
                    harness.Options,
                    new CallbackProgress(_ => { }),
                    CancellationToken.None
                );

            await update.Should().ThrowAsync<Exception>();
            File.Exists(marker).Should().BeTrue();
            File.ReadAllText(marker).Should().Be("1.0.0");
            File.ReadAllText(harness.InstalledFile("app.dll")).Should().Be("old-app");
        }
        finally
        {
            if (File.Exists(marker))
                File.Delete(marker);
        }
    }
}
