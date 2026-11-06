using System.Net;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class InstallationEngineRobustnessTests : IDisposable
{
    private static readonly IProgress<InstallProgress> NoProgress = new Progress<InstallProgress>(
        _ => { }
    );

    private readonly List<string> _createdDirectories = [];

    public void Dispose()
    {
        foreach (string directory in _createdDirectories)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private string NewTargetDir()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "StorkDropTests",
            "target-" + Guid.NewGuid().ToString("N")
        );
        _createdDirectories.Add(path);
        return path;
    }

    private static Task<ProductLockAcquisition> HoldLockInOtherFlowAsync(
        string productId,
        string operation
    )
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(() => ProductOperationLock.TryAcquire(productId, "default", operation));
        }
    }

    [Fact]
    public async Task Install_ChecksumMismatch_FailsWithRealCause_NotExtractionFailed()
    {
        EngineFixture fixture = new EngineFixture();
        byte[] package = EngineFixture.Zip(("app.txt", "hello"));
        fixture.ServeBytes(package);

        InstallResult result = await fixture.Engine.InstallAsync(
            fixture.Manifest(sha256: new string('0', 64)),
            fixture.Options(NewTargetDir()),
            NoProgress
        );

        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("PackageVerification");
        result
            .ErrorMessage.Should()
            .Contain("Checksum mismatch")
            .And.NotContain("Extraction failed");
    }

    [Fact]
    public async Task Install_DownloadFailure_NamesTheServerStatus()
    {
        EngineFixture fixture = new EngineFixture();
        fixture
            .Client.DownloadProductAsync(
                fixture.ProductId,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<Stream>>(_ =>
                throw new HttpRequestException("nope", null, HttpStatusCode.NotFound)
            );

        InstallResult result = await fixture.Engine.InstallAsync(
            fixture.Manifest(),
            fixture.Options(NewTargetDir()),
            NoProgress
        );

        result.FailedStep.Should().Be("Downloading");
        result.ErrorMessage.Should().Contain("404").And.Contain("Engine Test");
    }

    [Fact]
    public async Task Install_TransientDownloadFailure_IsRetried_AndExtractionErrorShowsTheCause()
    {
        EngineFixture fixture = new EngineFixture();
        int calls = 0;
        fixture
            .Client.DownloadProductAsync(
                fixture.ProductId,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                calls++;
                if (calls == 1)
                    throw new IOException("connection reset");
                return Task.FromResult<Stream>(new MemoryStream("this is not a zip"u8.ToArray()));
            });

        InstallResult result = await fixture.Engine.InstallAsync(
            fixture.Manifest(),
            fixture.Options(NewTargetDir()),
            NoProgress
        );

        calls.Should().Be(2);
        result.FailedStep.Should().Be("Extracting");
        result
            .ErrorMessage.Should()
            .StartWith("Extraction failed: ")
            .And.NotBe("Extraction failed.");
    }

    [Fact]
    public async Task Install_WithoutConfiguredFeeds_FailsWithAClearMessage()
    {
        EngineFixture fixture = new EngineFixture(hasFeeds: false);
        InstallOptions options = new InstallOptions(
            TargetPath: NewTargetDir(),
            CreateBackup: false
        );

        InstallResult result = await fixture.Engine.InstallAsync(
            fixture.Manifest(),
            options,
            NoProgress
        );

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No feeds configured");
    }

    [Fact]
    public async Task Install_NotEnoughSpaceToExtract_FailsBeforeTouchingTheTarget()
    {
        EngineFixture fixture = new EngineFixture(availableBytes: _ => 1);
        fixture.ServeBytes(EngineFixture.Zip(("app.txt", new string('x', 5000))));
        string target = NewTargetDir();

        InstallResult result = await fixture.Engine.InstallAsync(
            fixture.Manifest(),
            fixture.Options(target),
            NoProgress
        );

        result.FailedStep.Should().Be("DiskSpace");
        result
            .ErrorMessage.Should()
            .Contain("Not enough free disk space")
            .And.Contain("extracting");
        Directory.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task Install_NotEnoughSpaceAtTheTarget_FailsBeforeAnyChange()
    {
        string target = NewTargetDir();
        EngineFixture fixture = new EngineFixture(availableBytes: path =>
            path.StartsWith(
                StorkPaths.TempDir + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            )
                ? long.MaxValue
                : 1
        );
        fixture.ServeBytes(EngineFixture.Zip(("app.txt", new string('x', 5000))));

        InstallResult result = await fixture.Engine.InstallAsync(
            fixture.Manifest(),
            fixture.Options(target),
            NoProgress
        );

        result.FailedStep.Should().Be("DiskSpace");
        result.ErrorMessage.Should().Contain("installing Engine Test v1.0.0");
        Directory.Exists(target).Should().BeFalse();
        await fixture.Repository.DidNotReceive().AddAsync(Arg.Any<InstalledProduct>(), default);
    }

    [Fact]
    public async Task Install_SkipsTargetSpaceCheck_ForUnresolvedTemplatePaths()
    {
        EngineFixture fixture = new EngineFixture(availableBytes: path =>
            path.StartsWith(
                StorkPaths.TempDir + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            )
                ? long.MaxValue
                : 1
        );
        fixture.ServeBytes(EngineFixture.Zip(("app.txt", "x")));

        InstallResult result = await fixture.Engine.InstallAsync(
            fixture.Manifest(),
            fixture.Options(@"{ProductPath:other}\app"),
            NoProgress
        );

        result.FailedStep.Should().NotBe("DiskSpace");
    }

    [Fact]
    public async Task Install_WhenAnotherOperationHoldsTheLock_ReportsHolderAndDoesNothing()
    {
        EngineFixture fixture = new EngineFixture();
        using ProductLockAcquisition other = await HoldLockInOtherFlowAsync(
            fixture.ProductId,
            "update"
        );

        InstallResult result = await fixture.Engine.InstallAsync(
            fixture.Manifest(),
            fixture.Options(NewTargetDir()),
            NoProgress
        );

        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("Coordination");
        result
            .ErrorMessage.Should()
            .Contain("Engine Test")
            .And.Contain("update")
            .And.Contain(Environment.ProcessId.ToString());
        await fixture
            .Client.DidNotReceive()
            .DownloadProductAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Install_ReleasesTheLock_AfterAFailedRun()
    {
        EngineFixture fixture = new EngineFixture();
        fixture
            .Client.DownloadProductAsync(
                fixture.ProductId,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<Stream>>(_ =>
                throw new HttpRequestException("x", null, HttpStatusCode.NotFound)
            );
        await fixture.Engine.InstallAsync(
            fixture.Manifest(),
            fixture.Options(NewTargetDir()),
            NoProgress
        );

        using ProductLockAcquisition afterwards = await HoldLockInOtherFlowAsync(
            fixture.ProductId,
            "check"
        );

        afterwards.Acquired.Should().BeTrue();
    }

    [Fact]
    public async Task Update_WhenAnotherOperationHoldsTheLock_Throws()
    {
        EngineFixture fixture = new EngineFixture();
        using ProductLockAcquisition other = await HoldLockInOtherFlowAsync(
            fixture.ProductId,
            "install"
        );

        Func<Task> act = () =>
            fixture.Engine.UpdateAsync(
                fixture.Installed(NewTargetDir()),
                fixture.Manifest("2.0.0"),
                fixture.Options(NewTargetDir()),
                NoProgress
            );

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("already in progress")
            .And.Contain("install");
    }

    [Fact]
    public async Task ReExecute_WhenAnotherOperationHoldsTheLock_Fails()
    {
        EngineFixture fixture = new EngineFixture();
        using ProductLockAcquisition other = await HoldLockInOtherFlowAsync(
            fixture.ProductId,
            "update"
        );

        InstallResult result = await fixture.Engine.ReExecutePluginsAsync(
            fixture.Installed(NewTargetDir()),
            new ReExecuteOptions(),
            NoProgress
        );

        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("Coordination");
    }

    [Fact]
    public async Task Uninstall_WhenAnotherOperationHoldsTheLock_Throws()
    {
        EngineFixture fixture = new EngineFixture();
        using ProductLockAcquisition other = await HoldLockInOtherFlowAsync(
            fixture.ProductId,
            "update"
        );

        Func<Task> act = () => fixture.Engine.UninstallAsync(fixture.Installed(NewTargetDir()));

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*already in progress*");
        await fixture
            .Repository.DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<string>(), default);
    }

    [Fact]
    public async Task Lock_IsPerInstance_SoAnotherInstanceCanStillBeInstalled()
    {
        EngineFixture fixture = new EngineFixture();
        using ProductLockAcquisition other = await HoldLockInOtherFlowAsync(
            fixture.ProductId,
            "update"
        );
        fixture
            .Client.DownloadProductAsync(
                fixture.ProductId,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<Stream>>(_ =>
                throw new HttpRequestException("x", null, HttpStatusCode.NotFound)
            );
        InstallOptions otherInstance = new InstallOptions(
            TargetPath: NewTargetDir(),
            InstanceId: "second",
            FeedId: EngineFixture.FeedId,
            CreateBackup: false
        );

        InstallResult result = await fixture.Engine.InstallAsync(
            fixture.Manifest(),
            otherInstance,
            NoProgress
        );

        result.FailedStep.Should().Be("Downloading");
    }

    private static string WriteStoredManifest(
        string productId,
        string uniqueId,
        ProductManifest manifest
    )
    {
        string dir = StorkPaths.ProductMetadataDir(productId, uniqueId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
        return dir;
    }

    [Fact]
    public async Task ReExecute_ManifestRequiringANewerHost_IsRejectedBeforeAnyPluginRuns()
    {
        EngineFixture fixture = new EngineFixture();
        ProductManifest manifest = fixture.Manifest() with
        {
            MinHostVersion = "999.0.0",
            Plugins = [new StorkPluginInfo("plugins/missing.dll", "Acme.Plugin")],
        };
        string metadata = WriteStoredManifest(fixture.ProductId, "abcd1234", manifest);
        try
        {
            InstallResult result = await fixture.Engine.ReExecutePluginsAsync(
                fixture.Installed(NewTargetDir()),
                new ReExecuteOptions(),
                NoProgress
            );

            result.Success.Should().BeFalse();
            result.FailedStep.Should().Be("HostVersion");
            result.ErrorMessage.Should().Contain("999.0.0");
        }
        finally
        {
            Directory.Delete(metadata, recursive: true);
        }
    }

    [Fact]
    public async Task ReExecute_ManifestWithSatisfiedHostVersion_IsNotRejected()
    {
        EngineFixture fixture = new EngineFixture();
        ProductManifest manifest = fixture.Manifest() with
        {
            MinHostVersion = "0.0.1",
            Plugins = [new StorkPluginInfo("plugins/missing.dll", "Acme.Plugin")],
        };
        string metadata = WriteStoredManifest(fixture.ProductId, "abcd1234", manifest);
        try
        {
            InstallResult result = await fixture.Engine.ReExecutePluginsAsync(
                fixture.Installed(NewTargetDir()),
                new ReExecuteOptions(),
                NoProgress
            );

            result.FailedStep.Should().NotBe("HostVersion");
        }
        finally
        {
            Directory.Delete(metadata, recursive: true);
        }
    }

    [Fact]
    public async Task ParallelOperations_ReportProgressOnlyToTheirOwnSink()
    {
        EngineFixture fixture = new EngineFixture();
        string idA = "iso-a-" + Guid.NewGuid().ToString("N")[..8];
        string idB = "iso-b-" + Guid.NewGuid().ToString("N")[..8];
        ProductManifest template = fixture.Manifest() with
        {
            Plugins = [new StorkPluginInfo("plugins/missing.dll", "Acme.Plugin")],
        };
        string metaA = WriteStoredManifest(idA, "abcd1234", template with { ProductId = idA });
        string metaB = WriteStoredManifest(idB, "abcd1234", template with { ProductId = idB });

        int arrived = 0;
        TaskCompletionSource bothReachedTheGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        fixture
            .Repository.GetByIdAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(async _ =>
            {
                if (Interlocked.Increment(ref arrived) == 2)
                    bothReachedTheGate.SetResult();
                await bothReachedTheGate.Task.WaitAsync(TimeSpan.FromSeconds(20));
                return (InstalledProduct?)null;
            });

        List<string> sinkA = [];
        List<string> sinkB = [];
        object gate = new object();
        InstalledProduct productA = fixture.Installed(NewTargetDir()) with
        {
            ProductId = idA,
            Title = "Alpha",
        };
        InstalledProduct productB = fixture.Installed(NewTargetDir()) with
        {
            ProductId = idB,
            Title = "Beta",
        };

        try
        {
            Task<InstallResult> a = Task.Run(() =>
                fixture.Engine.ReExecutePluginsAsync(
                    productA,
                    new ReExecuteOptions(),
                    new SyncProgress(m =>
                    {
                        lock (gate)
                            sinkA.Add(m);
                    })
                )
            );
            Task<InstallResult> b = Task.Run(() =>
                fixture.Engine.ReExecutePluginsAsync(
                    productB,
                    new ReExecuteOptions(),
                    new SyncProgress(m =>
                    {
                        lock (gate)
                            sinkB.Add(m);
                    })
                )
            );
            await Task.WhenAll(a, b);
        }
        finally
        {
            Directory.Delete(metaA, recursive: true);
            Directory.Delete(metaB, recursive: true);
        }

        sinkA
            .Should()
            .Contain("Loading plugin data for Alpha...")
            .And.NotContain(m => m.Contains("Beta"));
        sinkB
            .Should()
            .Contain("Loading plugin data for Beta...")
            .And.NotContain(m => m.Contains("Alpha"));
        sinkA.Should().HaveCount(sinkB.Count).And.Contain("Running PreInstall...");
        sinkB.Should().Contain("Running PreInstall...");
    }

    private sealed class SyncProgress(Action<string> sink) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => sink(value.Message);
    }

    [Fact]
    public async Task OperationState_DoesNotLeakIntoALaterOperation()
    {
        EngineFixture fixture = new EngineFixture();
        string idA = "iso-c-" + Guid.NewGuid().ToString("N")[..8];
        List<string> first = [];
        List<string> second = [];

        await fixture.Engine.ReExecutePluginsAsync(
            fixture.Installed(NewTargetDir()) with
            {
                ProductId = idA,
            },
            new ReExecuteOptions(),
            new SyncProgress(first.Add)
        );
        await fixture.Engine.ReExecutePluginsAsync(
            fixture.Installed(NewTargetDir()) with
            {
                ProductId = idA,
                Title = "Second",
            },
            new ReExecuteOptions(),
            new SyncProgress(second.Add)
        );

        first.Should().ContainSingle().Which.Should().Be("Loading plugin data for Engine Test...");
        second.Should().ContainSingle().Which.Should().Be("Loading plugin data for Second...");
    }
}
