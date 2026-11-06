using System.IO.Compression;
using FluentAssertions;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _backupRoot;
    private readonly string _source;

    public BackupServiceTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "storkdrop-backup-tests",
            Guid.NewGuid().ToString("N")
        );
        _backupRoot = Path.Combine(_root, "backups");
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_source);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private void Write(string relative, string content)
    {
        string full = Path.Combine(_source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public async Task Backup_ThenRestore_RoundTripsOnlyTrackedFiles()
    {
        Write("a.dll", "old-a");
        Write("sub/b.dll", "old-b");
        BackupService service = new BackupService(_backupRoot);

        string backupPath = await service.CreateBackupAsync(
            "prod",
            _source,
            ["a.dll", "sub/b.dll"]
        );

        // Simulate a failed update: tracked files changed, plus a foreign file appeared.
        File.WriteAllText(Path.Combine(_source, "a.dll"), "new-a");
        File.WriteAllText(Path.Combine(_source, "foreign.txt"), "not-ours");

        await service.RestoreBackupAsync(backupPath, _source);

        File.ReadAllText(Path.Combine(_source, "a.dll")).Should().Be("old-a");
        File.ReadAllText(Path.Combine(_source, "sub", "b.dll")).Should().Be("old-b");
        // Restore must never touch files it did not back up.
        File.Exists(Path.Combine(_source, "foreign.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task Backup_OnlyIncludesGivenFiles_NotWholeFolder()
    {
        Write("tracked.dll", "x");
        Write("untracked.dll", "y");
        BackupService service = new BackupService(_backupRoot);

        string backupPath = await service.CreateBackupAsync("prod", _source, ["tracked.dll"]);

        // Restore into an empty folder and confirm only the tracked file comes back.
        string restoreTarget = Path.Combine(_root, "restore");
        await service.RestoreBackupAsync(backupPath, restoreTarget);

        File.Exists(Path.Combine(restoreTarget, "tracked.dll")).Should().BeTrue();
        File.Exists(Path.Combine(restoreTarget, "untracked.dll")).Should().BeFalse();
    }

    [Fact]
    public async Task Backup_WithoutFiles_ThrowsInsteadOfWritingAnEmptyZip()
    {
        BackupService service = new BackupService(_backupRoot);

        Func<Task> create = () => service.CreateBackupAsync("prod", _source, []);

        await create.Should().ThrowAsync<ArgumentException>();
        Directory.GetFiles(_backupRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task Backup_WhenNoListedFileExists_ThrowsAndLeavesNothingBehind()
    {
        BackupService service = new BackupService(_backupRoot);

        Func<Task> create = () => service.CreateBackupAsync("prod", _source, ["missing.dll"]);

        await create.Should().ThrowAsync<InvalidOperationException>();
        Directory.GetFiles(_backupRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task Backup_WhenCancelled_LeavesNoPartialFile()
    {
        Write("a.dll", "x");
        BackupService service = new BackupService(_backupRoot);
        using CancellationTokenSource cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> create = () => service.CreateBackupAsync("prod", _source, ["a.dll"], cts.Token);

        await create.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(_backupRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task Backup_IsWrittenAtomically_NoTemporaryFileRemains()
    {
        Write("a.dll", "x");
        BackupService service = new BackupService(_backupRoot);

        string backupPath = await service.CreateBackupAsync("prod", _source, ["a.dll"]);

        Directory
            .GetFiles(Path.GetDirectoryName(backupPath)!)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(backupPath);
        using ZipArchive archive = ZipFile.OpenRead(backupPath);
        archive.Entries.Should().ContainSingle().Which.FullName.Should().Be("a.dll");
    }

    [Fact]
    public async Task Backup_NamesIncludeInstanceAndMilliseconds_AndNeverCollide()
    {
        Write("a.dll", "x");
        BackupService service = new BackupService(_backupRoot, retainedBackups: 50);
        string otherSource = Path.Combine(_root, "other-instance");
        Directory.CreateDirectory(otherSource);
        File.WriteAllText(Path.Combine(otherSource, "a.dll"), "y");

        List<string> paths = [];
        for (int i = 0; i < 6; i++)
            paths.Add(await service.CreateBackupAsync("prod", _source, ["a.dll"]));
        string otherPath = await service.CreateBackupAsync("prod", otherSource, ["a.dll"]);

        paths.Should().OnlyHaveUniqueItems();
        paths.Concat([otherPath]).Should().OnlyHaveUniqueItems();
        Path.GetFileName(paths[0])
            .Should()
            .MatchRegex(@"^prod-[0-9a-f]{8}-\d{8}-\d{9}(-\d+)?\.zip$");
        Path.GetFileName(otherPath)
            .Split('-')[1]
            .Should()
            .NotBe(Path.GetFileName(paths[0]).Split('-')[1]);
    }

    [Fact]
    public async Task Backup_KeepsOnlyTheLastNBackups_PerProductAndInstance()
    {
        Write("a.dll", "x");
        BackupService service = new BackupService(_backupRoot, retainedBackups: 2);
        string otherSource = Path.Combine(_root, "other-instance");
        Directory.CreateDirectory(otherSource);
        File.WriteAllText(Path.Combine(otherSource, "a.dll"), "y");
        string productDir = Path.Combine(_backupRoot, "prod");
        Directory.CreateDirectory(productDir);
        string legacyBackup = Path.Combine(productDir, "prod-20240101-000000.zip");
        File.WriteAllText(legacyBackup, "legacy");

        List<string> instanceA = [];
        for (int i = 0; i < 4; i++)
        {
            instanceA.Add(await service.CreateBackupAsync("prod", _source, ["a.dll"]));
            await Task.Delay(20);
        }
        List<string> instanceB = [];
        for (int i = 0; i < 3; i++)
        {
            instanceB.Add(await service.CreateBackupAsync("prod", otherSource, ["a.dll"]));
            await Task.Delay(20);
        }
        string otherProduct = await service.CreateBackupAsync("other", _source, ["a.dll"]);

        instanceA.Select(File.Exists).Should().Equal(false, false, true, true);
        instanceB.Select(File.Exists).Should().Equal(false, true, true);
        File.Exists(legacyBackup).Should().BeTrue();
        File.Exists(otherProduct).Should().BeTrue();
    }
}
