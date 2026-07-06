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
}
