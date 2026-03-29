using FluentAssertions;
using StorkDrop.Contracts.Services;
using Xunit;

namespace StorkDrop.Tests.Core;

public sealed class StorkPathsTests
{
    [Fact]
    public void ConfigDir_IsUnderAppDataRoaming()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        StorkPaths.ConfigDir.Should().StartWith(roaming);
    }

    [Fact]
    public void BackupRoot_IsUnderAppDataLocal()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        StorkPaths.BackupRoot.Should().StartWith(local);
    }

    [Fact]
    public void InstalledProductsFile_IsInsideStorkConfigDir()
    {
        StorkPaths.InstalledProductsFile.Should().StartWith(StorkPaths.StorkConfigDir);
    }

    [Fact]
    public void ActivityLogFile_IsInsideStorkConfigDir()
    {
        StorkPaths.ActivityLogFile.Should().StartWith(StorkPaths.StorkConfigDir);
    }

    [Fact]
    public void PluginConfigFile_ReturnsCorrectPatternWithPluginId()
    {
        string result = StorkPaths.PluginConfigFile("my-plugin");

        result.Should().Contain("my-plugin");
        result.Should().EndWith(".json");
        result.Should().StartWith(StorkPaths.ConfigDir);
    }

    [Fact]
    public void AllPaths_AreAbsolute()
    {
        Path.IsPathRooted(StorkPaths.ConfigDir).Should().BeTrue();
        Path.IsPathRooted(StorkPaths.StorkConfigDir).Should().BeTrue();
        Path.IsPathRooted(StorkPaths.LogDir).Should().BeTrue();
        Path.IsPathRooted(StorkPaths.LogFile).Should().BeTrue();
        Path.IsPathRooted(StorkPaths.InstalledProductsFile).Should().BeTrue();
        Path.IsPathRooted(StorkPaths.ActivityLogFile).Should().BeTrue();
        Path.IsPathRooted(StorkPaths.BackupRoot).Should().BeTrue();
        Path.IsPathRooted(StorkPaths.TempDir).Should().BeTrue();
        Path.IsPathRooted(StorkPaths.PluginTempDir).Should().BeTrue();
        Path.IsPathRooted(StorkPaths.DefaultInstallRoot).Should().BeTrue();
    }

    [Fact]
    public void PluginConfigFile_WithSpecialCharactersInId_ProducesValidPath()
    {
        string result = StorkPaths.PluginConfigFile("my plugin (v2)");

        Path.IsPathRooted(result).Should().BeTrue();
        result.Should().EndWith(".json");

        // Should not throw when getting directory name (i.e., it is a structurally valid path)
        string? dir = Path.GetDirectoryName(result);
        dir.Should().NotBeNullOrEmpty();
    }
}
