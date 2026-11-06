using FluentAssertions;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class StartMenuShortcutCleanerTests : IDisposable
{
    private const string InstallPath = @"C:\Acme\Dashboard";

    private readonly string _programs = Path.Combine(
        Path.GetTempPath(),
        "storkdrop-startmenu-tests",
        Guid.NewGuid().ToString("N")
    );

    public StartMenuShortcutCleanerTests()
    {
        Directory.CreateDirectory(_programs);
    }

    public void Dispose()
    {
        Directory.Delete(_programs, recursive: true);
    }

    private string Shortcut(string folder, string name)
    {
        string directory = Path.Combine(_programs, folder);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".lnk");
        File.WriteAllText(path, "lnk");
        return path;
    }

    private static (string? Target, string? WorkingDirectory) Read(
        Dictionary<string, string> targets,
        string lnk
    ) => targets.TryGetValue(lnk, out string? target) ? (target, null) : (null, null);

    [Fact]
    public void RemoveProductShortcuts_DeletesOnlyShortcutsPointingIntoTheInstallFolder()
    {
        string own = Shortcut("Acme", "Acme Dashboard");
        string sameNameOtherProduct = Shortcut("Other", "Acme Dashboard Docs");
        string unrelated = Shortcut("Other", "Notepad");
        Dictionary<string, string> targets = new Dictionary<string, string>
        {
            [own] = Path.Combine(InstallPath, "dashboard.exe"),
            [sameNameOtherProduct] = @"C:\Other\docs.exe",
            [unrelated] = @"C:\Windows\notepad.exe",
        };

        int removed = StartMenuShortcutCleaner.RemoveProductShortcuts(
            _programs,
            "Acme Dashboard",
            InstallPath,
            lnk => Read(targets, lnk)
        );

        removed.Should().Be(1);
        File.Exists(own).Should().BeFalse();
        File.Exists(sameNameOtherProduct).Should().BeTrue();
        File.Exists(unrelated).Should().BeTrue();
    }

    [Fact]
    public void RemoveProductShortcuts_KeepsShortcutsWhoseTargetCannotBeRead()
    {
        string unreadable = Shortcut("Acme", "Acme Dashboard");

        int removed = StartMenuShortcutCleaner.RemoveProductShortcuts(
            _programs,
            "Acme Dashboard",
            InstallPath,
            _ => (null, null)
        );

        removed.Should().Be(0);
        File.Exists(unreadable).Should().BeTrue();
    }

    [Fact]
    public void RemoveProductShortcuts_RemovesOnlyFoldersItEmptiedItself()
    {
        string own = Shortcut("Acme", "Acme Dashboard");
        Directory.CreateDirectory(Path.Combine(_programs, "UnrelatedEmptyFolder"));
        Dictionary<string, string> targets = new Dictionary<string, string>
        {
            [own] = Path.Combine(InstallPath, "dashboard.exe"),
        };

        StartMenuShortcutCleaner.RemoveProductShortcuts(
            _programs,
            "Acme Dashboard",
            InstallPath,
            lnk => Read(targets, lnk)
        );

        Directory.Exists(Path.Combine(_programs, "Acme")).Should().BeFalse();
        Directory.Exists(Path.Combine(_programs, "UnrelatedEmptyFolder")).Should().BeTrue();
    }

    [Theory]
    [InlineData(@"C:\Acme\Dashboard\bin\app.exe", true)]
    [InlineData(@"C:\Acme\Dashboard", true)]
    [InlineData(@"C:\Acme\Dashboard2\app.exe", false)]
    [InlineData(@"C:\Acme\app.exe", false)]
    [InlineData(null, false)]
    public void IsInside_RespectsFolderBoundaries(string? path, bool expected)
    {
        StartMenuShortcutCleaner.IsInside(path, InstallPath).Should().Be(expected);
    }
}
