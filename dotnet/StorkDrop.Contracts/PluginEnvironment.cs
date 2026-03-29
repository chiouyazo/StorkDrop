using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Contracts;

/// <summary>
/// Read-only environment info passed to <see cref="IStorkPlugin.GetConfigurationSchema"/>
/// so plugins can build dynamic options.
/// </summary>
public sealed class PluginEnvironment
{
    /// <summary>
    /// Gets or sets the path to the StorkDrop configuration directory.
    /// </summary>
    public string StorkConfigDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the previously installed version, or null if this is a fresh install.
    /// </summary>
    public string? PreviousVersion { get; set; }

    /// <summary>
    /// Gets or sets the configuration values from the previous install.
    /// </summary>
    public Dictionary<string, string> PreviousConfigValues { get; set; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Extra data provided by <see cref="IStorkDropPlugin"/> implementations.
    /// Keyed by plugin ID. Plugins can put arbitrary context here for their product plugins.
    /// </summary>
    public Dictionary<string, object> PluginData { get; set; } = new Dictionary<string, object>();
}
