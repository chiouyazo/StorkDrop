using FluentAssertions;
using StorkDrop.Contracts.Services;
using Xunit;

namespace StorkDrop.Tests.Core;

public sealed class AtomicFileTests
{
    [Fact]
    public async Task WriteAllTextAsync_LeavesNoTempFilesBehind()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");

        await AtomicFile.WriteAllTextAsync(path, "{}");

        Directory.GetFiles(dir.Path).Should().ContainSingle().Which.Should().Be(path);
    }

    [Fact]
    public async Task WriteAllTextAsync_ParallelWritersToSameFile_NeverCollideOnTempName()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");
        await AtomicFile.WriteAllTextAsync(path, "{\"n\":-1}");

        Task[] writers = Enumerable
            .Range(0, 40)
            .Select(i => Task.Run(() => AtomicFile.WriteAllTextAsync(path, $"{{\"n\":{i}}}")))
            .ToArray();
        await Task.WhenAll(writers);

        AtomicFile.IsValidJson(await File.ReadAllTextAsync(path)).Should().BeTrue();
        Directory.GetFiles(dir.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void WriteAllText_Sync_WritesAndReplaces()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("machine-id");

        AtomicFile.WriteAllText(path, "first");
        AtomicFile.WriteAllText(path, "second");

        File.ReadAllText(path).Should().Be("second");
        Directory.GetFiles(dir.Path).Should().ContainSingle();
    }

    [Fact]
    public async Task WriteAllTextAsync_WithBackup_KeepsPreviousValidContentAsBak()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");
        await AtomicFile.WriteAllTextAsync(path, "{\"v\":1}", keepBackup: true);

        await AtomicFile.WriteAllTextAsync(path, "{\"v\":2}", keepBackup: true);

        File.ReadAllText(path).Should().Be("{\"v\":2}");
        File.ReadAllText(AtomicFile.BackupPath(path)).Should().Be("{\"v\":1}");
    }

    [Fact]
    public async Task WriteAllTextAsync_WithBackup_DoesNotOverwriteBackupWithCorruptPrimary()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");
        await AtomicFile.WriteAllTextAsync(path, "{\"v\":1}", keepBackup: true);
        await AtomicFile.WriteAllTextAsync(path, "{\"v\":2}", keepBackup: true);
        await File.WriteAllTextAsync(path, "{ this is not json");

        await AtomicFile.WriteAllTextAsync(path, "{\"v\":3}", keepBackup: true);

        File.ReadAllText(AtomicFile.BackupPath(path)).Should().Be("{\"v\":1}");
        File.ReadAllText(path).Should().Be("{\"v\":3}");
    }

    [Fact]
    public async Task ReadAllTextAsync_FallsBackToBackup_WhenPrimaryIsCorrupt()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");
        await AtomicFile.WriteAllTextAsync(path, "{\"v\":1}", keepBackup: true);
        await AtomicFile.WriteAllTextAsync(path, "{\"v\":2}", keepBackup: true);
        await File.WriteAllTextAsync(path, "");

        string? read = await AtomicFile.ReadAllTextAsync(path);

        read.Should().Be("{\"v\":1}");
    }

    [Fact]
    public async Task ReadAllTextAsync_MissingPrimary_DoesNotResurrectBackup()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");
        await File.WriteAllTextAsync(AtomicFile.BackupPath(path), "{\"v\":1}");

        (await AtomicFile.ReadAllTextAsync(path)).Should().BeNull();
    }

    [Fact]
    public async Task ReadAllTextAsync_NoValidCopy_ReturnsPrimaryForTheCallersParser()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");
        await File.WriteAllTextAsync(path, "garbage");
        await File.WriteAllTextAsync(AtomicFile.BackupPath(path), "also garbage");

        (await AtomicFile.ReadAllTextAsync(path)).Should().Be("garbage");
    }

    [Fact]
    public async Task ReadAllTextAsync_UsesCallerSuppliedValidity()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");
        await File.WriteAllTextAsync(path, "{}");
        await File.WriteAllTextAsync(AtomicFile.BackupPath(path), "[1]");

        string? read = await AtomicFile.ReadAllTextAsync(path, text => text.StartsWith('['));

        read.Should().Be("[1]");
    }

    [Fact]
    public async Task ReadAllTextAsync_WhileAnotherHandleHoldsFileOpen_Succeeds()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("state.json");
        await AtomicFile.WriteAllTextAsync(path, "{\"v\":1}");

        await using FileStream holder = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        string? read = await AtomicFile.ReadAllTextAsync(path);

        read.Should().Be("{\"v\":1}");
    }
}
