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
    public void Branding_Prefix_IsolatesAllIdentityPaths()
    {
        try
        {
            Branding.Initialize(new BrandingInfo(Prefix: "acme"));

            StorkPaths.ConfigDir.Should().Contain("acme-StorkDrop");
            StorkPaths.StorkConfigDir.Should().Contain("acme-StorkDrop");
            StorkPaths.LogDir.Should().Contain("acme-StorkDrop");
            StorkPaths.BackupRoot.Should().Contain("acme-StorkDrop");
            StorkPaths.TempDir.Should().Contain("acme-StorkDrop");
            StorkPaths.DefaultInstallRoot.Should().EndWith("acme-StorkDrop");
        }
        finally
        {
            Branding.Initialize(BrandingInfo.Default);
        }
    }

    [Fact]
    public void ProductMetadata_LivesUnderConfig_NotInInstallDirectory()
    {
        string installPath = @"C:\Program Files (x86)\STAPS\Application";
        string metadataDir = StorkPaths.ProductMetadataDir("some-product", "ab12cd34");

        // The whole point of the fix: metadata must NOT live inside the product's install directory
        // (which may be shared), but under StorkDrop's own per-instance config.
        metadataDir.Should().StartWith(StorkPaths.StorkConfigDir);
        metadataDir.Should().NotStartWith(installPath);
        StorkPaths.ProductManifestFile("some-product", "ab12cd34").Should().StartWith(metadataDir);
        StorkPaths.ProductPluginsDir("some-product", "ab12cd34").Should().StartWith(metadataDir);
        StorkPaths
            .ProductHandledFilesDir("some-product", "ab12cd34")
            .Should()
            .StartWith(metadataDir);
    }

    [Fact]
    public void ProductMetadata_IsIsolatedPerProductAndInstance()
    {
        string a = StorkPaths.ProductMetadataDir("product-a", "inst0001");
        string b = StorkPaths.ProductMetadataDir("product-b", "inst0001");
        string a2 = StorkPaths.ProductMetadataDir("product-a", "inst0002");

        a.Should().NotBe(b);
        a.Should().NotBe(a2);
    }

    [Fact]
    public void Branding_Default_UsesPlainStorkDropFolder()
    {
        Branding.Initialize(BrandingInfo.Default);

        StorkPaths.ConfigDir.Should().Contain("StorkDrop");
        StorkPaths.ConfigDir.Should().NotContain("-StorkDrop");
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
