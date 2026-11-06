using FluentAssertions;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class FileLockDetectorTests
{
    private readonly FileLockDetector _detector = new();

    [Fact]
    public void IsFileLocked_ForFileHeldWithoutSharing_ReturnsTrue()
    {
        string path = Path.Combine(Path.GetTempPath(), $"storkdrop-lock-{Guid.NewGuid():N}.dll");
        File.WriteAllText(path, "x");
        try
        {
            using FileStream hold = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );

            new FileLockDetector().IsFileLocked(path).Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsFileLocked_ForReadOnlyFileNobodyUses_ReturnsFalse()
    {
        string path = Path.Combine(Path.GetTempPath(), $"storkdrop-lock-{Guid.NewGuid():N}.dll");
        File.WriteAllText(path, "x");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            new FileLockDetector().IsFileLocked(path).Should().BeFalse();
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    [Fact]
    public void ThrowIfAnyLocked_DoesNothingForNonExistentDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "StorkDropTests", Guid.NewGuid().ToString());

        Action act = () => _detector.ThrowIfAnyLocked(dir);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfAnyLocked_DoesNothingForEmptyDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "StorkDropTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            Action act = () => _detector.ThrowIfAnyLocked(dir);

            act.Should().NotThrow();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ThrowIfAnyLocked_DoesNothingForDirectoryWithOnlyTxtFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "StorkDropTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "hello");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "world");

            Action act = () => _detector.ThrowIfAnyLocked(dir);

            act.Should().NotThrow();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ThrowIfAnyLocked_DoesNothingForUnlockedDllFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "StorkDropTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            File.WriteAllText(Path.Combine(dir, "library.dll"), "fake dll content");

            Action act = () => _detector.ThrowIfAnyLocked(dir);

            act.Should().NotThrow();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ThrowIfAnyLocked_ThrowsFileLockedException_WhenDllIsLocked()
    {
        string dir = Path.Combine(Path.GetTempPath(), "StorkDropTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        string dllPath = Path.Combine(dir, "locked.dll");
        File.WriteAllText(dllPath, "fake dll content");

        FileStream? lockStream = null;
        try
        {
            // Hold an exclusive lock on the file
            lockStream = new FileStream(
                dllPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );

            Action act = () => _detector.ThrowIfAnyLocked(dir);

            act.Should().Throw<FileLockedException>().Which.FileName.Should().Be("locked.dll");
        }
        finally
        {
            lockStream?.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ThrowIfAnyLocked_ThrowsForLockedExeFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "StorkDropTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        string exePath = Path.Combine(dir, "app.exe");
        File.WriteAllText(exePath, "fake exe content");

        FileStream? lockStream = null;
        try
        {
            lockStream = new FileStream(
                exePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );

            Action act = () => _detector.ThrowIfAnyLocked(dir);

            act.Should().Throw<FileLockedException>().Which.FileName.Should().Be("app.exe");
        }
        finally
        {
            lockStream?.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ThrowIfAnyLocked_IgnoresLockedTxtFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "StorkDropTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        string txtPath = Path.Combine(dir, "data.txt");
        File.WriteAllText(txtPath, "some text content");

        FileStream? lockStream = null;
        try
        {
            lockStream = new FileStream(
                txtPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );

            Action act = () => _detector.ThrowIfAnyLocked(dir);

            act.Should().NotThrow("locked .txt files should be ignored");
        }
        finally
        {
            lockStream?.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }
}
