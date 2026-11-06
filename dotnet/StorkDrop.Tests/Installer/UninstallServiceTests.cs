using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

[Collection(StorkPathsCollection.Name)]
public sealed class UninstallServiceTests
{
    private static LockedFileInfo Locked(string path) =>
        new LockedFileInfo { FilePath = path, FileName = Path.GetFileName(path) };

    [Fact]
    public async Task Uninstall_AfterRenameAndContinue_DoesNotPromptAgainForEachLockedFile()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.WriteInstalledFile("free.dll", "free");
        harness.WriteInstalledFile("locked1.dll", "one");
        harness.WriteInstalledFile("locked2.dll", "two");
        await harness.WriteFileManifestAsync("free.dll", "locked1.dll", "locked2.dll");
        string locked1 = harness.InstalledFile("locked1.dll");
        string locked2 = harness.InstalledFile("locked2.dll");
        harness
            .LockDetector.GetLockedFiles(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                call.Arg<IReadOnlyList<string>>()
                    .Where(p => p == locked1 || p == locked2)
                    .Select(Locked)
                    .ToList()
            );
        int prompts = 0;
        harness.Uninstaller.OnLockedFilesDetected = (_, _, _) =>
        {
            prompts++;
            return LockedFilesAction.RenameAndContinue;
        };

        using FileStream hold1 = new FileStream(
            locked1,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None
        );
        using FileStream hold2 = new FileStream(
            locked2,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None
        );

        await harness.Uninstaller.UninstallAsync(harness.Installed);

        prompts.Should().Be(1);
        File.Exists(harness.InstalledFile("free.dll")).Should().BeFalse();
    }

    [Fact]
    public async Task Uninstall_RunsPostUninstallOfTheStoredPlugin()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.WriteInstalledFile("app.dll", "app");
        await harness.WriteFileManifestAsync("app.dll");
        string assemblyPath = typeof(CleanupProbePlugin).Assembly.Location;
        string pluginDir = Path.Combine(harness.MetadataDir, "plugins");
        Directory.CreateDirectory(pluginDir);
        File.Copy(assemblyPath, Path.Combine(pluginDir, Path.GetFileName(assemblyPath)));
        ProductManifest manifest = harness.Manifest("1.0.0") with
        {
            Plugins =
            [
                new StorkPluginInfo(
                    "plugins/" + Path.GetFileName(assemblyPath),
                    typeof(CleanupProbePlugin).FullName!
                ),
            ],
        };
        File.WriteAllText(
            Path.Combine(harness.MetadataDir, "manifest.json"),
            JsonSerializer.Serialize(manifest)
        );

        await harness.Uninstaller.UninstallAsync(harness.Installed);

        File.Exists(harness.CleanupMarkerPath).Should().BeTrue();
        File.Exists(harness.InstalledFile("app.dll")).Should().BeFalse();
    }

    [Fact]
    public async Task CopyDirectoryRecursive_CopiesNestedFoldersToTheirRelativePosition()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "storkdrop-copy-" + Guid.NewGuid().ToString("N")
        );
        string source = Path.Combine(root, "plugins");
        string destination = Path.Combine(root, "copy");
        Directory.CreateDirectory(Path.Combine(source, "plugins", "plugins"));
        await File.WriteAllTextAsync(Path.Combine(source, "a.dll"), "a");
        await File.WriteAllTextAsync(Path.Combine(source, "plugins", "plugins", "b.dll"), "b");
        Directory.CreateDirectory(destination);
        try
        {
            UninstallService.CopyDirectoryRecursive(source, destination);

            File.ReadAllText(Path.Combine(destination, "a.dll")).Should().Be("a");
            File.ReadAllText(Path.Combine(destination, "plugins", "plugins", "b.dll"))
                .Should()
                .Be("b");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Uninstall_WhenUserRetriesAfterEndingTheProcess_DeletesTheFile()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.WriteInstalledFile("locked.dll", "x");
        await harness.WriteFileManifestAsync("locked.dll");
        string lockedPath = harness.InstalledFile("locked.dll");
        FileStream hold = new FileStream(
            lockedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None
        );
        bool released = false;
        harness
            .LockDetector.GetLockedFiles(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                call.Arg<IReadOnlyList<string>>()
                    .Where(p => !released && p == lockedPath)
                    .Select(Locked)
                    .ToList()
            );
        int prompts = 0;
        harness.Uninstaller.OnLockedFilesDetected = (_, _, _) =>
        {
            prompts++;
            hold.Dispose();
            released = true;
            return LockedFilesAction.Retry;
        };

        await harness.Uninstaller.UninstallAsync(harness.Installed);

        prompts.Should().Be(1);
        File.Exists(lockedPath).Should().BeFalse();
    }
}
