using System.Text.Json;
using FluentAssertions;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using Xunit;

namespace StorkDrop.Tests.Core;

public sealed class FileManifestStoreTests
{
    [Fact]
    public void Parse_NewFormat_RoundTripsPathHashSize()
    {
        List<TrackedFile> files =
        [
            new TrackedFile("a.txt", "abc123", 10),
            new TrackedFile("sub\\b.dll", "def456", 20),
        ];
        string json = JsonSerializer.Serialize(files);

        List<TrackedFile>? parsed = FileManifestStore.Parse(json);

        parsed.Should().NotBeNull();
        parsed!.Should().HaveCount(2);
        parsed[0].Path.Should().Be("a.txt");
        parsed[0].Sha256.Should().Be("abc123");
        parsed[0].Size.Should().Be(10);
    }

    [Fact]
    public void Parse_LegacyStringArray_YieldsUnverifiableEntries()
    {
        List<TrackedFile>? parsed = FileManifestStore.Parse("[\"a.txt\",\"sub/b.dll\"]");

        parsed.Should().NotBeNull();
        parsed!.Should().HaveCount(2);
        parsed[0].Path.Should().Be("a.txt");
        parsed[0].Sha256.Should().BeNull();
    }

    [Fact]
    public async Task WriteThenReadPaths_ReturnsPaths_ForBothConsumers()
    {
        string temp = Path.Combine(
            Path.GetTempPath(),
            "sd-manifest-" + Guid.NewGuid().ToString("N") + ".json"
        );
        try
        {
            await FileManifestStore.WriteAsync(temp, [new TrackedFile("x.dll", "h", 1)]);

            List<string>? paths = await FileManifestStore.ReadPathsAsync(temp);

            paths.Should().ContainSingle().Which.Should().Be("x.dll");
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("[ { \"Path\": ")]
    [InlineData("garbage")]
    public void Parse_CorruptJson_ReturnsNull_InsteadOfThrowing(string json)
    {
        // A corrupt/half-written manifest must not crash uninstall or the integrity check.
        FileManifestStore.Parse(json).Should().BeNull();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("\"a string\"")]
    [InlineData("42")]
    public void Parse_ValidJsonThatIsNotAnArray_ReturnsNull(string json)
    {
        FileManifestStore.Parse(json).Should().BeNull();
    }

    [Fact]
    public async Task Write_CreatesMissingDirectory_AndLeavesNoTempFile()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "sd-manifest-" + Guid.NewGuid().ToString("N")
        );
        string path = Path.Combine(dir, "sub", "product.files.json");
        try
        {
            await FileManifestStore.WriteAsync(path, [new TrackedFile("bin/app.exe", "abc", 42)]);

            File.Exists(path).Should().BeTrue();
            File.Exists(path + ".tmp").Should().BeFalse();

            List<TrackedFile>? read = await FileManifestStore.ReadAsync(path);
            read.Should().ContainSingle();
            read![0].Path.Should().Be("bin/app.exe");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Read_ReturnsNull_WhenFileMissing()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "sd-missing-" + Guid.NewGuid().ToString("N") + ".json"
        );
        (await FileManifestStore.ReadAsync(path)).Should().BeNull();
    }
}
