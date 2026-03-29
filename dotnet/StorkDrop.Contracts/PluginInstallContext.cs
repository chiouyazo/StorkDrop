using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Contracts;

/// <summary>
/// Context passed to <see cref="IStorkDropPlugin.OnProductInstalledAsync"/>.
/// </summary>
public sealed class PluginInstallContext
{
    /// <summary>
    /// Gets or sets the unique product identifier.
    /// </summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version that was installed.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path where the product was installed.
    /// </summary>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path where the extracted files are located.
    /// </summary>
    public string ExtractedPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path to the configuration directory.
    /// </summary>
    public string ConfigDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the plugin-specific settings.
    /// </summary>
    public Dictionary<string, string> PluginSettings { get; set; } =
        new Dictionary<string, string>();
}
