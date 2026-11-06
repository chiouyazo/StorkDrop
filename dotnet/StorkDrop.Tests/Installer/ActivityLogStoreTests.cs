using FluentAssertions;
using StorkDrop.Contracts.Models;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class ActivityLogStoreTests
{
    private static ActivityLogEntry Entry(string id, int minute = 0) =>
        new ActivityLogEntry(
            Id: id,
            Timestamp: new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc),
            Action: "Install",
            ProductId: "p",
            Details: id,
            Success: true
        );

    [Fact]
    public async Task LogAsync_FromTwoStoreInstances_KeepsBothEntries()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("activity-log.json");
        using ActivityLogStore gui = new ActivityLogStore(path);
        using ActivityLogStore cli = new ActivityLogStore(path);
        await gui.GetEntriesAsync();

        await cli.LogAsync(Entry("from-cli", 1));
        await gui.LogAsync(Entry("from-gui", 2));

        using ActivityLogStore reader = new ActivityLogStore(path);
        (await reader.GetEntriesAsync()).Select(e => e.Id).Should().Equal("from-gui", "from-cli");
    }

    [Fact]
    public async Task GetEntriesAsync_SeesEntriesWrittenByAnotherInstance()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("activity-log.json");
        using ActivityLogStore gui = new ActivityLogStore(path);
        using ActivityLogStore cli = new ActivityLogStore(path);
        (await gui.GetEntriesAsync()).Should().BeEmpty();

        await cli.LogAsync(Entry("late"));

        (await gui.GetEntriesAsync()).Select(e => e.Id).Should().Equal("late");
    }

    [Fact]
    public async Task ParallelLogsFromManyInstances_LoseNothing()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("activity-log.json");

        await Task.WhenAll(
            Enumerable
                .Range(0, 10)
                .Select(async i =>
                {
                    using ActivityLogStore store = new ActivityLogStore(path);
                    await store.LogAsync(Entry($"e{i}", i));
                })
        );

        using ActivityLogStore reader = new ActivityLogStore(path);
        (await reader.GetEntriesAsync()).Should().HaveCount(10);
    }

    [Fact]
    public async Task GetEntriesAsync_FallsBackToBackup_WhenFileIsCorrupt()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("activity-log.json");
        using (ActivityLogStore first = new ActivityLogStore(path))
        {
            await first.LogAsync(Entry("one", 1));
            await first.LogAsync(Entry("two", 2));
        }
        await File.WriteAllTextAsync(path, "not json");

        using ActivityLogStore store = new ActivityLogStore(path);

        (await store.GetEntriesAsync()).Select(e => e.Id).Should().Equal("one");
    }

    [Fact]
    public async Task ClearAsync_EmptiesTheSharedFile()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("activity-log.json");
        using ActivityLogStore store = new ActivityLogStore(path);
        await store.LogAsync(Entry("one"));

        await store.ClearAsync();

        using ActivityLogStore reader = new ActivityLogStore(path);
        (await reader.GetEntriesAsync()).Should().BeEmpty();
    }
}
