using FluentAssertions;
using StorkDrop.Contracts.Services;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class PluginConfigPersistenceTests : IDisposable
{
    private readonly string _pluginId = "persist-test-" + Guid.NewGuid().ToString("N")[..10];
    private readonly EngineFixture _fixture = new EngineFixture();

    public void Dispose()
    {
        foreach (
            string path in new[]
            {
                StorkPaths.PluginConfigFile(_pluginId),
                StorkPaths.InstancePluginConfigPath(_fixture.ProductId, "u1234567"),
            }
        )
        {
            foreach (string file in new[] { path, AtomicFile.BackupPath(path) })
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
    }

    [Fact]
    public async Task PluginSettingsStore_RoundTrips_AndKeepsABackupOfThePreviousValues()
    {
        PluginSettingsStore store = new PluginSettingsStore();

        await store.SaveAsync(_pluginId, new Dictionary<string, string> { ["a"] = "1" });
        await store.SaveAsync(_pluginId, new Dictionary<string, string> { ["a"] = "2" });

        (await store.LoadAsync(_pluginId)).Should().Contain("a", "2");
        File.ReadAllText(AtomicFile.BackupPath(StorkPaths.PluginConfigFile(_pluginId)))
            .Should()
            .Contain("\"1\"");
    }

    [Fact]
    public async Task PluginSettingsStore_FallsBackToTheBackup_WhenTheFileIsCorrupt()
    {
        PluginSettingsStore store = new PluginSettingsStore();
        await store.SaveAsync(_pluginId, new Dictionary<string, string> { ["a"] = "1" });
        await store.SaveAsync(_pluginId, new Dictionary<string, string> { ["a"] = "2" });
        await File.WriteAllTextAsync(StorkPaths.PluginConfigFile(_pluginId), "{ broken");

        (await store.LoadAsync(_pluginId)).Should().Contain("a", "1");
    }

    [Fact]
    public async Task PluginSettingsStore_MissingFile_YieldsEmptyValues()
    {
        (await new PluginSettingsStore().LoadAsync(_pluginId)).Should().BeEmpty();
    }

    [Fact]
    public async Task EngineSavedPluginConfig_FallsBackToTheBackup_WhenTheFileIsCorrupt()
    {
        string path = StorkPaths.InstancePluginConfigPath(_fixture.ProductId, "u1234567");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await AtomicFile.WriteAllTextAsync(path, "{\"db\":\"old\"}", keepBackup: true);
        await AtomicFile.WriteAllTextAsync(path, "{\"db\":\"new\"}", keepBackup: true);
        await File.WriteAllTextAsync(path, "");

        Dictionary<string, string> loaded = await _fixture.Engine.LoadSavedPluginConfigAsync(
            _fixture.ProductId,
            "u1234567"
        );

        loaded.Should().Contain("db", "old");
    }

    [Fact]
    public async Task EngineSavedPluginConfig_ReadsAValidFile()
    {
        string path = StorkPaths.InstancePluginConfigPath(_fixture.ProductId, "u1234567");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{\"db\":\"x\"}");

        (await _fixture.Engine.LoadSavedPluginConfigAsync(_fixture.ProductId, "u1234567"))
            .Should()
            .Contain("db", "x");
    }
}
