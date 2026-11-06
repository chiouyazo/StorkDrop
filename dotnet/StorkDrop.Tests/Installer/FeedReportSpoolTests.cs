using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class FeedReportSpoolTests
{
    private static FeedReportService Create(string spoolDirectory) =>
        new FeedReportService(
            Substitute.For<IConfigurationService>(),
            Substitute.For<IProductRepository>(),
            Substitute.For<IEncryptionService>(),
            NullLogger<FeedReportService>.Instance,
            spoolDirectory
        );

    [Fact]
    public async Task FlushAsync_DropsCorruptSpoolFile_WhenNoOtherProcessIsFlushing()
    {
        using TestDirectory dir = new TestDirectory();
        string corrupt = dir.File("20260101000000000-a.json");
        await File.WriteAllTextAsync(corrupt, "not json");

        await Create(dir.Path).FlushAsync();

        File.Exists(corrupt).Should().BeFalse();
    }

    [Fact]
    public async Task FlushAsync_LeavesSpoolUntouched_WhenAnotherProcessHoldsTheFlushLock()
    {
        using TestDirectory dir = new TestDirectory();
        string pending = dir.File("20260101000000000-a.json");
        await File.WriteAllTextAsync(pending, "not json");
        using IDisposable? otherProcess = CrossProcessFileLock.TryAcquire(dir.File("flush"));
        otherProcess.Should().NotBeNull();

        await Create(dir.Path).FlushAsync();

        File.Exists(pending).Should().BeTrue();
    }

    [Fact]
    public async Task FlushAsync_RemovesOnlyOldOrphanedTempFiles()
    {
        using TestDirectory dir = new TestDirectory();
        string oldTemp = dir.File("a.json.0123.tmp");
        string freshTemp = dir.File("b.json.4567.tmp");
        await File.WriteAllTextAsync(oldTemp, "x");
        await File.WriteAllTextAsync(freshTemp, "y");
        File.SetLastWriteTimeUtc(oldTemp, DateTime.UtcNow.AddDays(-3));

        await Create(dir.Path).FlushAsync();

        File.Exists(oldTemp).Should().BeFalse();
        File.Exists(freshTemp).Should().BeTrue();
    }

    [Fact]
    public async Task FlushAsync_IgnoresTempFilesWhenReadingTheSpool()
    {
        using TestDirectory dir = new TestDirectory();
        string halfWritten = dir.File("20260101000000000-a.json.89ab.tmp");
        await File.WriteAllTextAsync(halfWritten, "{\"Url\":");

        await Create(dir.Path).FlushAsync();

        File.Exists(halfWritten).Should().BeTrue();
    }
}
