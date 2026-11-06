using FluentAssertions;
using StorkDrop.Contracts.Services;
using Xunit;

namespace StorkDrop.Tests.Core;

public sealed class ElevationWaitTests
{
    private sealed class FakeChild : IElevatedChild
    {
        private readonly Func<int, bool> _exitsOnPoll;
        private int _polls;

        public FakeChild(Func<int, bool> exitsOnPoll, int exitCode = 0)
        {
            _exitsOnPoll = exitsOnPoll;
            ExitCode = exitCode;
        }

        public int ExitCode { get; }
        public bool Killed { get; private set; }
        public int Polls => _polls;

        public bool WaitForExit(TimeSpan timeout)
        {
            _polls++;
            Thread.Sleep(2);
            return !Killed && _exitsOnPoll(_polls);
        }

        public void Kill() => Killed = true;
    }

    private static ElevationResult Wait(
        FakeChild child,
        Func<string, bool>? onLongRunning = null,
        Action<string>? onProgress = null,
        string? logPath = null,
        TimeSpan? threshold = null,
        CancellationToken cancellationToken = default
    ) =>
        ElevationHelper.WaitForChild(
            child,
            logPath,
            onProgress,
            onLongRunning,
            threshold ?? TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(1),
            cancellationToken
        );

    [Fact]
    public void WithoutLongRunningCallback_ChildIsNeverKilledForElapsedTime()
    {
        FakeChild child = new FakeChild(polls => polls >= 60);

        ElevationResult result = Wait(child, onLongRunning: null);

        result.Should().Be(ElevationResult.Succeeded);
        child.Killed.Should().BeFalse();
        child.Polls.Should().BeGreaterThanOrEqualTo(60);
    }

    [Fact]
    public void CallbackReturningTrue_KeepsWaitingAndIsConsultedRepeatedly()
    {
        int consulted = 0;
        FakeChild child = new FakeChild(polls => polls >= 80);

        ElevationResult result = Wait(
            child,
            onLongRunning: _ =>
            {
                consulted++;
                return true;
            }
        );

        result.Should().Be(ElevationResult.Succeeded);
        child.Killed.Should().BeFalse();
        consulted.Should().BeGreaterThan(1);
    }

    [Fact]
    public void CallbackReturningFalse_KillsChildAndFails()
    {
        FakeChild child = new FakeChild(_ => false);

        ElevationResult result = Wait(child, onLongRunning: _ => false);

        result.Should().Be(ElevationResult.Failed);
        child.Killed.Should().BeTrue();
    }

    [Fact]
    public void NonZeroExitCode_IsAFailure()
    {
        FakeChild child = new FakeChild(polls => polls >= 2, exitCode: 1);

        Wait(child).Should().Be(ElevationResult.Failed);
    }

    [Fact]
    public void Cancellation_KillsChildAndThrows()
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        FakeChild child = new FakeChild(_ => false);
        cts.CancelAfter(30);

        Action act = () => Wait(child, cancellationToken: cts.Token);

        act.Should().Throw<OperationCanceledException>();
        child.Killed.Should().BeTrue();
    }

    [Fact]
    public void ProgressLines_AreForwardedFromTheChildsOwnLog_AndFlushedAtExit()
    {
        using TestDirectory dir = new TestDirectory();
        string log = dir.File("progress.log");
        File.WriteAllText(log, "2026-11-06 10:00:00.000 +01:00 [INF] first\n");
        List<string> lines = [];
        FakeChild child = new FakeChild(polls =>
        {
            if (polls == 3)
                File.AppendAllText(log, "2026-11-06 10:00:01.000 +01:00 [INF] second\n");
            return polls >= 5;
        });

        Wait(child, onProgress: lines.Add, logPath: log, threshold: TimeSpan.FromMinutes(5));

        lines.Should().HaveCount(2);
        lines[0].Should().EndWith("first");
        lines[1].Should().EndWith("second");
    }

    [Fact]
    public void LongRunningCallback_ReceivesTheLastObservedStep()
    {
        using TestDirectory dir = new TestDirectory();
        string log = dir.File("progress.log");
        File.WriteAllText(log, "[INF] copying files\n");
        string? seen = null;
        FakeChild child = new FakeChild(polls => polls >= 30);

        Wait(
            child,
            onLongRunning: step =>
            {
                seen ??= step;
                return true;
            },
            logPath: log
        );

        seen.Should().Be("[INF] copying files");
    }

    [Fact]
    public void MissingProgressLog_DoesNotBreakWaiting()
    {
        FakeChild child = new FakeChild(polls => polls >= 3);

        Wait(
                child,
                logPath: Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()),
                threshold: TimeSpan.FromMinutes(5)
            )
            .Should()
            .Be(ElevationResult.Succeeded);
    }
}
