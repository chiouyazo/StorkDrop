using FluentAssertions;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class TempDirectoryCleanerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "storkdrop-cleaner-tests",
        Guid.NewGuid().ToString("N")
    );

    public TempDirectoryCleanerTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private string CreateFolder(string name, DateTime lastWriteUtc)
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "package.zip");
        File.WriteAllText(file, "x");
        File.SetLastWriteTimeUtc(file, lastWriteUtc);
        Directory.SetLastWriteTimeUtc(folder, lastWriteUtc);
        return folder;
    }

    [Fact]
    public void DeleteStaleSubdirectories_RemovesOnlyFoldersOlderThanMinimumAge()
    {
        string stale = CreateFolder("stale", DateTime.UtcNow.AddHours(-30));
        string fresh = CreateFolder("fresh", DateTime.UtcNow.AddMinutes(-5));

        int deleted = TempDirectoryCleaner.DeleteStaleSubdirectories(_root, TimeSpan.FromHours(24));

        deleted.Should().Be(1);
        Directory.Exists(stale).Should().BeFalse();
        Directory.Exists(fresh).Should().BeTrue();
    }

    [Fact]
    public void DeleteStaleSubdirectories_KeepsOldFolderWithRecentlyModifiedContent()
    {
        string folder = CreateFolder("running", DateTime.UtcNow.AddHours(-30));
        File.SetLastWriteTimeUtc(Path.Combine(folder, "package.zip"), DateTime.UtcNow);

        int deleted = TempDirectoryCleaner.DeleteStaleSubdirectories(_root, TimeSpan.FromHours(24));

        deleted.Should().Be(0);
        Directory.Exists(folder).Should().BeTrue();
    }

    [Fact]
    public void DeleteStaleSubdirectories_ForMissingRoot_ReturnsZero()
    {
        TempDirectoryCleaner
            .DeleteStaleSubdirectories(
                Path.Combine(_root, "does-not-exist"),
                TimeSpan.FromHours(24)
            )
            .Should()
            .Be(0);
    }
}
