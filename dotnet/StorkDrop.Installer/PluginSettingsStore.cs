using System.Text.Json;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

public sealed class PluginSettingsStore : IPluginSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<Dictionary<string, string>> LoadAsync(
        string pluginId,
        CancellationToken cancellationToken = default
    )
    {
        string path = StorkPaths.PluginConfigFile(pluginId);
        if (!File.Exists(path))
            return new Dictionary<string, string>();

        string? json = await AtomicFile.ReadAllTextAsync(
            path,
            cancellationToken: cancellationToken
        );
        return json is null
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
    }

    public async Task SaveAsync(
        string pluginId,
        Dictionary<string, string> values,
        CancellationToken cancellationToken = default
    )
    {
        string path = StorkPaths.PluginConfigFile(pluginId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(values, JsonOptions);
        await SafeFileWriter.WriteAtomicWithBackupAsync(path, json, cancellationToken);
    }
}
