namespace StorkDrop.Contracts.Interfaces;

public interface IPluginSettingsStore
{
    Task<Dictionary<string, string>> LoadAsync(
        string pluginId,
        CancellationToken cancellationToken = default
    );
    Task SaveAsync(
        string pluginId,
        Dictionary<string, string> values,
        CancellationToken cancellationToken = default
    );
}
