using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class PackageDownloaderTests
{
    private static readonly byte[] Content = Enumerable
        .Range(0, 300_000)
        .Select(i => (byte)(i % 251))
        .ToArray();

    private static string HashOf(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static PackageDownloader Create(
        List<TimeSpan>? delays = null,
        TimeSpan? readTimeout = null,
        Func<string, long?>? availableBytes = null,
        int maxAttempts = 4
    ) =>
        new PackageDownloader(
            NullLogger.Instance,
            new PackageDownloadOptions
            {
                MaxAttempts = maxAttempts,
                InitialBackoff = TimeSpan.FromSeconds(1),
                ReadTimeout = readTimeout ?? TimeSpan.FromSeconds(30),
                Delay = (delay, _) =>
                {
                    delays?.Add(delay);
                    return Task.CompletedTask;
                },
                AvailableBytes = availableBytes ?? (_ => null),
            }
        );

    private static Task<PackageDownloadResult> Download(
        PackageDownloader downloader,
        Func<CancellationToken, Task<Stream>> open,
        string destination,
        string? sha = null,
        long? size = null,
        Action<long, long?>? progress = null,
        CancellationToken ct = default
    ) => downloader.DownloadAsync(open, destination, "acme 1.0", sha, size, progress, ct);

    private sealed class FailingStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _failAfter;
        private readonly Exception? _failure;
        private int _position;

        public FailingStream(byte[] data, int failAfter, Exception? failure)
        {
            _data = data;
            _failAfter = failAfter;
            _failure = failure;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (_position >= _failAfter)
                return _failure is null ? new ValueTask<int>(0) : throw _failure;

            int count = Math.Min(Math.Min(buffer.Length, 10_000), _failAfter - _position);
            _data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return new ValueTask<int>(count);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class HangingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Download_WritesFile_ReportsProgress_AndVerifiesChecksum()
    {
        using TestDirectory dir = new TestDirectory();
        string destination = dir.File("pkg.zip");
        List<long> reported = [];

        PackageDownloadResult result = await Download(
            Create(),
            _ => Task.FromResult<Stream>(new MemoryStream(Content)),
            destination,
            sha: HashOf(Content).ToUpperInvariant(),
            progress: (read, _) => reported.Add(read)
        );

        (await File.ReadAllBytesAsync(destination)).Should().Equal(Content);
        result.Bytes.Should().Be(Content.Length);
        result.Verified.Should().BeTrue();
        result.Sha256.Should().Be(HashOf(Content));
        reported.Should().NotBeEmpty().And.BeInAscendingOrder();
        reported[^1].Should().Be(Content.Length);
    }

    [Fact]
    public async Task Download_WithoutChecksum_SucceedsButIsReportedUnverified()
    {
        using TestDirectory dir = new TestDirectory();

        PackageDownloadResult result = await Download(
            Create(),
            _ => Task.FromResult<Stream>(new MemoryStream(Content)),
            dir.File("pkg.zip")
        );

        result.Verified.Should().BeFalse();
        File.Exists(dir.File("pkg.zip")).Should().BeTrue();
    }

    [Fact]
    public async Task Download_ChecksumMismatch_ThrowsAndLeavesNoFile_WithoutRetry()
    {
        using TestDirectory dir = new TestDirectory();
        int opens = 0;

        Func<Task> act = () =>
            Download(
                Create(),
                _ =>
                {
                    opens++;
                    return Task.FromResult<Stream>(new MemoryStream(Content));
                },
                dir.File("pkg.zip"),
                sha: new string('a', 64)
            );

        PackageIntegrityException ex = (
            await act.Should().ThrowAsync<PackageIntegrityException>()
        ).Which;
        ex.Message.Should().Contain("Checksum mismatch").And.Contain("acme 1.0");
        File.Exists(dir.File("pkg.zip")).Should().BeFalse();
        opens.Should().Be(1);
    }

    [Fact]
    public async Task Download_TransientFailure_RetriesWithExponentialBackoff_ThenSucceeds()
    {
        using TestDirectory dir = new TestDirectory();
        List<TimeSpan> delays = [];
        int opens = 0;

        PackageDownloadResult result = await Download(
            Create(delays),
            _ =>
            {
                opens++;
                return Task.FromResult<Stream>(
                    opens < 3
                        ? new FailingStream(Content, 25_000, new IOException("connection reset"))
                        : new MemoryStream(Content)
                );
            },
            dir.File("pkg.zip"),
            sha: HashOf(Content)
        );

        opens.Should().Be(3);
        delays.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        (await File.ReadAllBytesAsync(dir.File("pkg.zip"))).Should().Equal(Content);
        result.Verified.Should().BeTrue();
    }

    [Fact]
    public async Task Download_RestartsCleanly_NoLeftoverBytesFromTheFailedAttempt()
    {
        using TestDirectory dir = new TestDirectory();
        int opens = 0;

        await Download(
            Create(),
            _ =>
            {
                opens++;
                return Task.FromResult<Stream>(
                    opens == 1
                        ? new FailingStream(Content, 200_000, new IOException("reset"))
                        : new MemoryStream(Content)
                );
            },
            dir.File("pkg.zip")
        );

        new FileInfo(dir.File("pkg.zip")).Length.Should().Be(Content.Length);
    }

    [Fact]
    public async Task Download_ExhaustedRetries_ThrowsWithRealCauseAndAttemptCount_AndCleansUp()
    {
        using TestDirectory dir = new TestDirectory();
        int opens = 0;

        Func<Task> act = () =>
            Download(
                Create(maxAttempts: 3),
                _ =>
                {
                    opens++;
                    throw new HttpRequestException("boom", null, HttpStatusCode.ServiceUnavailable);
                },
                dir.File("pkg.zip")
            );

        PackageDownloadException ex = (
            await act.Should().ThrowAsync<PackageDownloadException>()
        ).Which;
        ex.Message.Should().Contain("acme 1.0").And.Contain("after 3 attempts").And.Contain("503");
        opens.Should().Be(3);
        File.Exists(dir.File("pkg.zip")).Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Download_ClientErrors_AreNotRetried_AndNameTheStatus(HttpStatusCode status)
    {
        using TestDirectory dir = new TestDirectory();
        int opens = 0;

        Func<Task> act = () =>
            Download(
                Create(),
                _ =>
                {
                    opens++;
                    throw new HttpRequestException("x", null, status);
                },
                dir.File("pkg.zip")
            );

        PackageDownloadException ex = (
            await act.Should().ThrowAsync<PackageDownloadException>()
        ).Which;
        opens.Should().Be(1);
        ex.Message.Should().Contain(((int)status).ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Download_RetriesTransientStatuses(HttpStatusCode status)
    {
        using TestDirectory dir = new TestDirectory();
        int opens = 0;

        await Download(
            Create(),
            _ =>
            {
                opens++;
                return opens == 1
                    ? throw new HttpRequestException("x", null, status)
                    : Task.FromResult<Stream>(new MemoryStream(Content));
            },
            dir.File("pkg.zip")
        );

        opens.Should().Be(2);
    }

    [Fact]
    public async Task Download_NetworkFailureWithoutStatus_IsRetried()
    {
        using TestDirectory dir = new TestDirectory();
        int opens = 0;

        await Download(
            Create(),
            _ =>
            {
                opens++;
                return opens == 1
                    ? throw new HttpRequestException(
                        "dns",
                        new SocketException((int)SocketError.HostNotFound)
                    )
                    : Task.FromResult<Stream>(new MemoryStream(Content));
            },
            dir.File("pkg.zip")
        );

        opens.Should().Be(2);
    }

    [Fact]
    public async Task Download_FileNotFound_IsNotRetried()
    {
        using TestDirectory dir = new TestDirectory();
        int opens = 0;

        Func<Task> act = () =>
            Download(
                Create(),
                _ =>
                {
                    opens++;
                    throw new FileNotFoundException("Package for acme not found.");
                },
                dir.File("pkg.zip")
            );

        PackageDownloadException ex = (
            await act.Should().ThrowAsync<PackageDownloadException>()
        ).Which;
        ex.Message.Should().Contain("not found");
        opens.Should().Be(1);
    }

    [Fact]
    public async Task Download_StalledBody_TimesOutAndIsRetried()
    {
        using TestDirectory dir = new TestDirectory();
        int opens = 0;

        await Download(
            Create(readTimeout: TimeSpan.FromMilliseconds(150)),
            _ =>
            {
                opens++;
                return Task.FromResult<Stream>(
                    opens == 1 ? new HangingStream() : new MemoryStream(Content)
                );
            },
            dir.File("pkg.zip")
        );

        opens.Should().Be(2);
        new FileInfo(dir.File("pkg.zip")).Length.Should().Be(Content.Length);
    }

    [Fact]
    public async Task Download_StalledBodyOnEveryAttempt_FailsWithTimeoutMessage()
    {
        using TestDirectory dir = new TestDirectory();

        Func<Task> act = () =>
            Download(
                Create(readTimeout: TimeSpan.FromMilliseconds(100), maxAttempts: 2),
                _ => Task.FromResult<Stream>(new HangingStream()),
                dir.File("pkg.zip")
            );

        (await act.Should().ThrowAsync<PackageDownloadException>())
            .Which.Message.Should()
            .Contain("No data received");
        File.Exists(dir.File("pkg.zip")).Should().BeFalse();
    }

    [Fact]
    public async Task Download_UserCancellation_IsNotRetriedAndPropagatesAsCancellation()
    {
        using TestDirectory dir = new TestDirectory();
        using CancellationTokenSource cts = new CancellationTokenSource(100);
        int opens = 0;

        Func<Task> act = () =>
            Download(
                Create(readTimeout: TimeSpan.FromSeconds(30)),
                _ =>
                {
                    opens++;
                    return Task.FromResult<Stream>(new HangingStream());
                },
                dir.File("pkg.zip"),
                ct: cts.Token
            );

        await act.Should().ThrowAsync<OperationCanceledException>();
        opens.Should().Be(1);
        File.Exists(dir.File("pkg.zip")).Should().BeFalse();
    }

    [Fact]
    public async Task Download_NotEnoughFreeSpace_FailsBeforeOpeningTheStream()
    {
        using TestDirectory dir = new TestDirectory();
        int opens = 0;

        Func<Task> act = () =>
            Download(
                Create(availableBytes: _ => 10_000),
                _ =>
                {
                    opens++;
                    return Task.FromResult<Stream>(new MemoryStream(Content));
                },
                dir.File("pkg.zip"),
                size: 50_000_000
            );

        InsufficientDiskSpaceException ex = (
            await act.Should().ThrowAsync<InsufficientDiskSpaceException>()
        ).Which;
        ex.Message.Should().Contain("Not enough free disk space").And.Contain("acme 1.0");
        ex.RequiredBytes.Should().BeGreaterThan(50_000_000);
        opens.Should().Be(0);
    }

    [Fact]
    public async Task Download_UnknownFreeSpace_DoesNotBlock()
    {
        using TestDirectory dir = new TestDirectory();

        PackageDownloadResult result = await Download(
            Create(availableBytes: _ => null),
            _ => Task.FromResult<Stream>(new MemoryStream(Content)),
            dir.File("pkg.zip"),
            size: long.MaxValue / 4
        );

        result.Bytes.Should().Be(Content.Length);
    }

    [Fact]
    public async Task Download_ProgressTotalFallsBackToExpectedSize_WhenStreamHasNoLength()
    {
        using TestDirectory dir = new TestDirectory();
        long? seenTotal = null;

        await Download(
            Create(),
            _ => Task.FromResult<Stream>(new FailingStream(Content, Content.Length, null)),
            dir.File("pkg.zip"),
            size: Content.Length,
            progress: (_, total) => seenTotal = total
        );

        seenTotal.Should().Be(Content.Length);
    }

    [Fact]
    public async Task Download_ProgressTotalUsesStreamLength_WhenAvailable()
    {
        using TestDirectory dir = new TestDirectory();
        long? seenTotal = null;

        await Download(
            Create(),
            _ => Task.FromResult<Stream>(new MemoryStream(Content)),
            dir.File("pkg.zip"),
            progress: (_, total) => seenTotal = total
        );

        seenTotal.Should().Be(Content.Length);
    }

    [Fact]
    public void IsTransient_Classification()
    {
        CancellationToken none = CancellationToken.None;
        PackageDownloader.IsTransient(new IOException("x"), none).Should().BeTrue();
        PackageDownloader.IsTransient(new TimeoutException(), none).Should().BeTrue();
        PackageDownloader.IsTransient(new TaskCanceledException(), none).Should().BeTrue();
        PackageDownloader.IsTransient(new FileNotFoundException(), none).Should().BeFalse();
        PackageDownloader.IsTransient(new UnauthorizedAccessException(), none).Should().BeFalse();
        PackageDownloader.IsTransient(new PackageIntegrityException("x"), none).Should().BeFalse();
        PackageDownloader
            .IsTransient(new InsufficientDiskSpaceException("C:\\", "x", 2, 1), none)
            .Should()
            .BeFalse();
        PackageDownloader
            .IsTransient(new IOException("full", unchecked((int)0x80070070)), none)
            .Should()
            .BeFalse();

        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();
        PackageDownloader
            .IsTransient(new OperationCanceledException(cts.Token), cts.Token)
            .Should()
            .BeFalse();
    }
}
