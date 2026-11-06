using FluentAssertions;
using NSubstitute;
using StorkDrop.Contracts.Models;
using Xunit;

namespace StorkDrop.Tests.Installer;

[Collection(StorkPathsCollection.Name)]
public sealed class InstallationEngineFreshInstallFailureTests
{
    private static async Task<InstallResult> InstallAsync(
        EngineTestHarness harness,
        ProductManifest manifest,
        Action<InstallProgress>? onProgress = null,
        CancellationToken cancellationToken = default
    ) =>
        await harness.Engine.InstallAsync(
            manifest,
            harness.Options,
            new CallbackProgress(p => onProgress?.Invoke(p)),
            cancellationToken
        );

    [Fact]
    public async Task VerificationFailure_RunsPostUninstallAndRemovesInstallFolder()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.MakeFresh();
        ProductManifest manifest = harness.ManifestWithCleanupProbe("1.0.0");

        InstallResult result = await InstallAsync(harness, manifest);

        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("Verifying");
        File.Exists(harness.CleanupMarkerPath).Should().BeTrue();
        Directory.Exists(harness.InstallPath).Should().BeFalse();
    }

    [Fact]
    public async Task CancelDuringCopy_RunsPostUninstallAndRemovesInstallFolder()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.MakeFresh();
        harness.SetPackage(new Dictionary<string, string> { ["app.dll"] = "app" });
        ProductManifest manifest = harness.ManifestWithCleanupProbe("1.0.0");
        using CancellationTokenSource cts = new CancellationTokenSource();
        CallbackProgress? observed = null;

        InstallResult result = await harness.Engine.InstallAsync(
            manifest,
            harness.Options,
            observed = new CallbackProgress(p =>
            {
                if (p.Message.StartsWith("Copying files to", StringComparison.Ordinal))
                    cts.Cancel();
            }),
            cts.Token
        );

        result.Success.Should().BeFalse();
        File.Exists(harness.CleanupMarkerPath).Should().BeTrue();
        Directory.Exists(harness.InstallPath).Should().BeFalse();
        observed.Messages.Should().Contain(m => m.Contains("Cleaning up database entries"));
    }

    [Fact]
    public async Task CancelBeforeAnythingWasDeployed_LeavesExistingFolderUntouched()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.MakeFresh();
        harness.WriteInstalledFile("keep.txt", "user data");
        harness.SetPackage(new Dictionary<string, string> { ["app.dll"] = "app" });
        ProductManifest manifest = harness.ManifestWithCleanupProbe("1.0.0");
        using CancellationTokenSource cts = new CancellationTokenSource();
        CallbackProgress progress = new CallbackProgress(p =>
        {
            if (p.Message.StartsWith("Download complete", StringComparison.Ordinal))
                cts.Cancel();
        });

        InstallResult result = await harness.Engine.InstallAsync(
            manifest,
            harness.Options,
            progress,
            cts.Token
        );

        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("Cancelled");
        File.ReadAllText(harness.InstalledFile("keep.txt")).Should().Be("user data");
        File.Exists(harness.CleanupMarkerPath).Should().BeFalse();
        progress.Messages.Should().NotContain(m => m.Contains("Cleaning up..."));
    }

    [Fact]
    public async Task PathConflictAfterPreInstall_RunsPostUninstallButKeepsTheOtherProductsFolder()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.MakeFresh();
        harness.RegisterForeignProductAt(harness.InstallPath);
        harness.WriteInstalledFile("foreign.dll", "other product");
        harness.SetPackage(new Dictionary<string, string> { ["app.dll"] = "app" });
        ProductManifest manifest = harness.ManifestWithCleanupProbe("1.0.0");

        InstallResult result = await InstallAsync(harness, manifest);

        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("PathConflict");
        File.Exists(harness.CleanupMarkerPath).Should().BeTrue();
        File.ReadAllText(harness.InstalledFile("foreign.dll")).Should().Be("other product");
    }

    [Fact]
    public async Task PluginContext_ProvidesInstalledInstancesWithoutBlockingOnTheRepository()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.MakeFresh();
        harness.RegisterForeignProductAt(Path.Combine(harness.Root, "elsewhere"));
        harness.SetPackage(new Dictionary<string, string> { ["app.dll"] = "app" });
        ProductManifest manifest = harness.ManifestWithCleanupProbe("1.0.0");

        await InstallAsync(harness, manifest);

        File.ReadAllText(CleanupProbePlugin.InstancesMarkerPath(harness.ProductId))
            .Should()
            .Be("1");
    }

    [Fact]
    public async Task MissingConfigCallbackResult_CancelsTheInstall_ButPreparedConfigDoesNot()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.MakeFresh();
        harness.SetPackage(new Dictionary<string, string> { ["app.dll"] = "app" });
        ProductManifest manifest = harness.ManifestWithCleanupProbe("1.0.0");
        harness.Engine.OnPluginConfigNeeded = (_, _) => null;

        InstallResult withoutConfig = await harness.Engine.InstallAsync(
            manifest,
            harness.Options,
            new CallbackProgress(_ => { }),
            CancellationToken.None
        );
        InstallResult withPreparedConfig = await harness.Engine.InstallAsync(
            manifest,
            harness.Options with
            {
                PluginConfigValues = new Dictionary<string, string>(),
            },
            new CallbackProgress(_ => { }),
            CancellationToken.None
        );

        withoutConfig.FailedStep.Should().Be("PluginConfig");
        withPreparedConfig.FailedStep.Should().NotBe("PluginConfig");
    }

    [Fact]
    public async Task ExceptionAfterCopy_StillCleansUpLikeBefore()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.MakeFresh();
        harness.SetPackage(new Dictionary<string, string> { ["app.dll"] = "app" });
        harness
            .Repository.AddAsync(Arg.Any<InstalledProduct>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("registry unavailable"));
        ProductManifest manifest = harness.ManifestWithCleanupProbe("1.0.0");

        InstallResult result = await InstallAsync(harness, manifest);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("registry unavailable");
        File.Exists(harness.CleanupMarkerPath).Should().BeTrue();
        Directory.Exists(harness.InstallPath).Should().BeFalse();
    }
}
