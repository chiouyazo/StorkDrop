using FluentAssertions;
using NSubstitute;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using Xunit;

namespace StorkDrop.Tests.Installer;

[Collection(StorkPathsCollection.Name)]
public sealed class InstallationEngineUpdateLockedFilesTests
{
    private static readonly Dictionary<string, string> NewPackage = new Dictionary<string, string>
    {
        ["app.dll"] = "new-app",
    };

    private static IReadOnlyList<LockedFileInfo> Locked(EngineTestHarness harness) =>
        [new LockedFileInfo { FilePath = harness.InstalledFile("app.dll"), FileName = "app.dll" }];

    private static async Task<ProductManifest> PrepareAsync(EngineTestHarness harness)
    {
        harness.WriteInstalledFile("app.dll", "old-app");
        await harness.WriteFileManifestAsync("app.dll");
        harness.SetPackage(NewPackage);
        return harness.ManifestWithCleanupProbe("2.0.0");
    }

    private static string PreInstallMarker(EngineTestHarness harness) =>
        CleanupProbePlugin.InstancesMarkerPath(harness.ProductId);

    private static Task Update(EngineTestHarness harness, ProductManifest manifest) =>
        harness.Engine.UpdateAsync(
            harness.Installed,
            manifest,
            harness.Options,
            new CallbackProgress(_ => { }),
            CancellationToken.None
        );

    private static string InstalledAppContent(EngineTestHarness harness) =>
        File.ReadAllText(harness.InstalledFile("app.dll"));

    [Fact]
    public async Task Update_FilesAreLockedOnlyUntilPreInstallStopsTheServices_DoesNotPrompt()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        ProductManifest manifest = await PrepareAsync(harness);
        string marker = PreInstallMarker(harness);
        harness
            .LockDetector.GetLockedFiles(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => File.Exists(marker) ? [] : Locked(harness));
        int prompts = 0;
        harness.Engine.OnLockedFilesDetected = (_, _, _) =>
        {
            prompts++;
            return LockedFilesAction.Skip;
        };

        await Update(harness, manifest);

        prompts.Should().Be(0);
        InstalledAppContent(harness).Should().Be("new-app");
    }

    [Fact]
    public async Task Update_FilesStayLockedAfterPreInstall_DismissedDialog_CancelsBeforeFilesAreRemoved()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        ProductManifest manifest = await PrepareAsync(harness);
        string marker = PreInstallMarker(harness);
        harness
            .LockDetector.GetLockedFiles(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Locked(harness));
        int prompts = 0;
        bool preInstallHadRun = false;
        harness.Engine.OnLockedFilesDetected = (_, _, _) =>
        {
            prompts++;
            preInstallHadRun = File.Exists(marker);
            return LockedFilesAction.Skip;
        };
        int failurePrompts = 0;
        harness.Engine.OnPrompt = _ =>
        {
            failurePrompts++;
            return new PluginPromptResult();
        };

        Func<Task> act = () => Update(harness, manifest);

        await act.Should().ThrowAsync<OperationCanceledException>().WithMessage("*files in use*");
        prompts.Should().Be(1);
        preInstallHadRun.Should().BeTrue();
        failurePrompts.Should().Be(0);
        InstalledAppContent(harness).Should().Be("old-app");
    }

    [Fact]
    public async Task Update_FilesStayLockedAfterPreInstall_RetryWhileStillLocked_GivesUpAfterBoundedPrompts()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        ProductManifest manifest = await PrepareAsync(harness);
        harness
            .LockDetector.GetLockedFiles(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Locked(harness));
        int prompts = 0;
        harness.Engine.OnLockedFilesDetected = (_, _, _) =>
        {
            prompts++;
            return LockedFilesAction.Retry;
        };

        Func<Task> act = () => Update(harness, manifest);

        await act.Should().ThrowAsync<OperationCanceledException>();
        prompts.Should().Be(5);
        InstalledAppContent(harness).Should().Be("old-app");
    }

    [Fact]
    public async Task Update_FilesReleasedAfterRetry_Proceeds()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        ProductManifest manifest = await PrepareAsync(harness);
        int scans = 0;
        harness
            .LockDetector.GetLockedFiles(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => scans++ == 0 ? Locked(harness) : []);
        harness.Engine.OnLockedFilesDetected = (_, _, _) => LockedFilesAction.Retry;

        await Update(harness, manifest);

        InstalledAppContent(harness).Should().Be("new-app");
    }

    [Fact]
    public async Task Update_LockedFiles_RenameAndContinue_Proceeds()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        ProductManifest manifest = await PrepareAsync(harness);
        harness
            .LockDetector.GetLockedFiles(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Locked(harness));
        harness.Engine.OnLockedFilesDetected = (_, _, _) => LockedFilesAction.RenameAndContinue;

        await Update(harness, manifest);

        InstalledAppContent(harness).Should().Be("new-app");
    }

    [Fact]
    public async Task Update_LockedFiles_HeadlessWithoutCallback_ProceedsAsBefore()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        ProductManifest manifest = await PrepareAsync(harness);
        harness
            .LockDetector.GetLockedFiles(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Locked(harness));

        await Update(harness, manifest);

        InstalledAppContent(harness).Should().Be("new-app");
    }
}
