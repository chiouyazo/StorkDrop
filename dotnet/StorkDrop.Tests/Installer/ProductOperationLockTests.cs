using System.Diagnostics;
using FluentAssertions;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class ProductOperationLockTests
{
    private static ProductLockAcquisition Acquire(
        TestDirectory dir,
        string productId = "acme.app",
        string? instanceId = "default",
        string operation = "install",
        int inheritedOwner = 0
    ) =>
        ProductOperationLock.TryAcquire(dir.Path, productId, instanceId, operation, inheritedOwner);

    private static Task<ProductLockAcquisition> AcquireInNewFlowAsync(
        TestDirectory dir,
        string productId = "acme.app",
        string? instanceId = "default",
        int inheritedOwner = 0
    )
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(() => Acquire(dir, productId, instanceId, "other", inheritedOwner));
        }
    }

    [Fact]
    public async Task SecondFlowIsRejected_WhileFirstHoldsTheLock()
    {
        using TestDirectory dir = new TestDirectory();
        using ProductLockAcquisition first = Acquire(dir, operation: "update");

        using ProductLockAcquisition second = await AcquireInNewFlowAsync(dir);

        first.Acquired.Should().BeTrue();
        second.Acquired.Should().BeFalse();
        second.Holder.Should().NotBeNull();
        second.Holder!.ProcessId.Should().Be(Environment.ProcessId);
        second.Holder.Operation.Should().Be("update");
    }

    [Fact]
    public async Task LockIsAvailableAgain_AfterRelease()
    {
        using TestDirectory dir = new TestDirectory();
        ProductLockAcquisition first = Acquire(dir);
        first.Dispose();

        using ProductLockAcquisition second = await AcquireInNewFlowAsync(dir);

        second.Acquired.Should().BeTrue();
    }

    [Fact]
    public async Task DifferentInstancesOfTheSameProduct_DoNotBlockEachOther()
    {
        using TestDirectory dir = new TestDirectory();
        using ProductLockAcquisition first = Acquire(dir, instanceId: "prod");

        using ProductLockAcquisition second = await AcquireInNewFlowAsync(dir, instanceId: "test");

        second.Acquired.Should().BeTrue();
    }

    [Fact]
    public async Task DifferentProducts_DoNotBlockEachOther()
    {
        using TestDirectory dir = new TestDirectory();
        using ProductLockAcquisition first = Acquire(dir, productId: "a");

        using ProductLockAcquisition second = await AcquireInNewFlowAsync(dir, productId: "b");

        second.Acquired.Should().BeTrue();
    }

    [Fact]
    public async Task KeyIsCaseInsensitive_AndMissingInstanceMeansDefault()
    {
        using TestDirectory dir = new TestDirectory();
        using ProductLockAcquisition first = Acquire(dir, productId: "Acme.App", instanceId: null);

        using ProductLockAcquisition second = await AcquireInNewFlowAsync(
            dir,
            productId: "acme.app",
            instanceId: "DEFAULT"
        );

        second.Acquired.Should().BeFalse();
    }

    [Fact]
    public void NestedAcquisitionInTheSameFlow_IsReentrant_AndDoesNotReleaseTheOuterLock()
    {
        using TestDirectory dir = new TestDirectory();
        using ProductLockAcquisition outer = Acquire(dir);

        ProductLockAcquisition inner = Acquire(dir, operation: "update");
        inner.Acquired.Should().BeTrue();
        inner.Dispose();

        Acquire(dir).Acquired.Should().BeTrue("the outer acquisition is still active in this flow");
    }

    [Fact]
    public async Task ReleasingTheOuterLock_WhileInnerWasReentrant_FreesTheKey()
    {
        using TestDirectory dir = new TestDirectory();
        ProductLockAcquisition outer = Acquire(dir);
        ProductLockAcquisition inner = Acquire(dir);
        inner.Dispose();
        outer.Dispose();

        using ProductLockAcquisition other = await AcquireInNewFlowAsync(dir);

        other.Acquired.Should().BeTrue();
    }

    [Fact]
    public async Task LockCanBeReleasedFromAnotherThread()
    {
        using TestDirectory dir = new TestDirectory();
        ProductLockAcquisition first = Acquire(dir);

        await Task.Run(() => first.Dispose());

        using ProductLockAcquisition second = await AcquireInNewFlowAsync(dir);
        second.Acquired.Should().BeTrue();
    }

    [Fact]
    public async Task LockSurvivesAwaitsAndThreadHops()
    {
        using TestDirectory dir = new TestDirectory();
        using ProductLockAcquisition first = Acquire(dir);

        await Task.Delay(20);
        await Task.Run(() => Thread.Sleep(10));

        using ProductLockAcquisition second = await AcquireInNewFlowAsync(dir);
        second.Acquired.Should().BeFalse();
    }

    [Fact]
    public async Task HolderIsInherited_WhenItIsTheDeclaredOwnerProcess()
    {
        using TestDirectory dir = new TestDirectory();
        using ProductLockAcquisition parent = Acquire(dir);

        using ProductLockAcquisition child = await AcquireInNewFlowAsync(
            dir,
            inheritedOwner: Environment.ProcessId
        );

        child.Acquired.Should().BeTrue();
    }

    [Fact]
    public async Task HolderIsNotInherited_WhenItIsADifferentProcess()
    {
        using TestDirectory dir = new TestDirectory();
        using ProductLockAcquisition parent = Acquire(dir);

        using ProductLockAcquisition child = await AcquireInNewFlowAsync(
            dir,
            inheritedOwner: Environment.ProcessId + 1
        );

        child.Acquired.Should().BeFalse();
    }

    [Fact]
    public async Task StaleHolderInfoOfADeadProcess_IsNotReported()
    {
        using TestDirectory dir = new TestDirectory();
        using ProductLockAcquisition first = Acquire(dir);
        string info = Directory.GetFiles(dir.Path, "*.info").Single();
        await File.WriteAllTextAsync(
            info,
            """{"ProcessId":2147483000,"ProcessName":"ghost","Since":"2026-01-01T00:00:00Z","Operation":"update"}"""
        );

        using ProductLockAcquisition second = await AcquireInNewFlowAsync(dir);

        second.Acquired.Should().BeFalse();
        second.Holder.Should().BeNull();
        second.DescribeConflict("Acme").Should().Contain("Acme").And.Contain("in progress");
    }

    [Fact]
    public async Task ConflictMessage_NamesOperationProcessAndPid()
    {
        using TestDirectory dir = new TestDirectory();
        using ProductLockAcquisition first = Acquire(dir, operation: "update");

        using ProductLockAcquisition second = await AcquireInNewFlowAsync(dir);

        string message = second.DescribeConflict("Acme App");
        message.Should().Contain("Acme App").And.Contain("update");
        message.Should().Contain(Environment.ProcessId.ToString());
    }

    [Fact]
    public void ReleasingRemovesLockAndInfoFiles()
    {
        using TestDirectory dir = new TestDirectory();
        ProductLockAcquisition first = Acquire(dir);
        Directory.GetFiles(dir.Path).Should().HaveCount(2);

        first.Dispose();

        Directory.GetFiles(dir.Path).Should().BeEmpty();
    }

    [Fact]
    public void FileNamesAreSafe_ForUnusualProductAndInstanceIds()
    {
        using TestDirectory dir = new TestDirectory();

        using ProductLockAcquisition acquisition = Acquire(
            dir,
            productId: "weird/id:with*chars?" + new string('x', 200),
            instanceId: "inst ance\\1"
        );

        acquisition.Acquired.Should().BeTrue();
        Directory.GetFiles(dir.Path).Should().OnlyContain(f => Path.GetFileName(f).Length < 120);
    }

    private static async Task<int> TryOpenInExternalProcessAsync(string lockPath)
    {
        string script =
            "try { $s = [System.IO.File]::Open($env:STORKDROP_TEST_LOCK, 'OpenOrCreate', 'ReadWrite', 'None'); $s.Dispose(); exit 0 } "
            + "catch { if ($_.Exception.InnerException -is [System.IO.IOException]) { exit 3 } else { exit 4 } }";
        ProcessStartInfo startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);
        startInfo.Environment["STORKDROP_TEST_LOCK"] = lockPath;

        using Process child = Process.Start(startInfo)!;
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        return child.ExitCode;
    }

    [Fact]
    public async Task LockExcludesARealSecondProcess_UntilReleased()
    {
        using TestDirectory dir = new TestDirectory();
        ProductLockAcquisition held = Acquire(dir);
        string lockPath = Directory.GetFiles(dir.Path, "*.lock").Single();

        int whileHeld = await TryOpenInExternalProcessAsync(lockPath);
        held.Dispose();
        int afterRelease = await TryOpenInExternalProcessAsync(lockPath);

        whileHeld.Should().Be(3);
        afterRelease.Should().Be(0);
    }
}
