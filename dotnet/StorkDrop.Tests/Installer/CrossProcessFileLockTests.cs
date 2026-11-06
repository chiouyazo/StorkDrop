using FluentAssertions;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class CrossProcessFileLockTests
{
    [Fact]
    public async Task AcquireAsync_SecondAcquirerWaitsUntilFirstReleases()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");

        IDisposable first = await CrossProcessFileLock.AcquireAsync(path);
        Task<IDisposable> second = CrossProcessFileLock.AcquireAsync(path);

        await Task.Delay(150);
        second.IsCompleted.Should().BeFalse();

        first.Dispose();
        using IDisposable acquired = await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AcquireAsync_ThrowsIOException_WhenTimeoutElapses()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");
        using IDisposable held = await CrossProcessFileLock.AcquireAsync(path);

        Func<Task> act = () =>
            CrossProcessFileLock.AcquireAsync(path, TimeSpan.FromMilliseconds(100));

        await act.Should().ThrowAsync<IOException>().WithMessage("*Timed out*");
    }

    [Fact]
    public async Task AcquireAsync_HonoursCancellationWhileWaiting()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");
        using IDisposable held = await CrossProcessFileLock.AcquireAsync(path);
        using CancellationTokenSource cts = new CancellationTokenSource(100);

        Func<Task> act = () =>
            CrossProcessFileLock.AcquireAsync(path, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void TryAcquire_ReturnsNullWhileHeld_AndSucceedsAfterRelease()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");

        IDisposable? first = CrossProcessFileLock.TryAcquire(path);
        first.Should().NotBeNull();
        CrossProcessFileLock.TryAcquire(path).Should().BeNull();

        first!.Dispose();
        using IDisposable? again = CrossProcessFileLock.TryAcquire(path);
        again.Should().NotBeNull();
    }

    [Fact]
    public async Task Dispose_RemovesTheLockFile()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");

        IDisposable held = await CrossProcessFileLock.AcquireAsync(path);
        File.Exists(CrossProcessFileLock.LockPathFor(path)).Should().BeTrue();

        held.Dispose();

        File.Exists(CrossProcessFileLock.LockPathFor(path)).Should().BeFalse();
    }

    [Fact]
    public async Task AcquireAsync_IsNotBoundToTheAcquiringThread()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");

        IDisposable held = await Task.Run(() => CrossProcessFileLock.AcquireAsync(path));
        await Task.Delay(10);
        await Task.Run(() => held.Dispose());

        using IDisposable? again = CrossProcessFileLock.TryAcquire(path);
        again.Should().NotBeNull();
    }
}
