using System.Collections.Generic;

namespace StorkDrop.Contracts;

/// <summary>
/// Full context passed to pre-install, post-install, pre-uninstall, and post-uninstall methods.
/// Includes the user's configuration choices from the dynamic UI.
/// </summary>
public sealed class PluginContext
{
    /// <summary>
    /// Gets or sets the unique product identifier.
    /// </summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version being installed or uninstalled.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target installation path on disk.
    /// </summary>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path to the StorkDrop configuration directory.
    /// </summary>
    public string StorkConfigDirectory { get; set; } = string.Empty;

    /// <summary>
    /// The values the user entered in the dynamic config UI, keyed by <see cref="PluginConfigField.Key"/>.
    /// For <see cref="PluginFieldType.MultiSelect"/>, values are comma-separated.
    /// </summary>
    public Dictionary<string, string> ConfigValues { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Extra data provided by <see cref="IStorkDropPlugin"/> implementations.
    /// </summary>
    public Dictionary<string, object> PluginData { get; set; } = new Dictionary<string, object>();
}
