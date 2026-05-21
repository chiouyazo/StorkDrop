using StorkDrop.Contracts.Interfaces;

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
    /// Gets or sets the instance identifier for multi-instance products.
    /// Plugins can use this to differentiate service names, database schemas, etc.
    /// </summary>
    public string InstanceId { get; set; } = "default";

    /// <summary>
    /// Gets or sets the machine-generated 8-char unique identifier for this instance.
    /// Used by the Steps SDK to generate instance-unique database keys (e.g. SD_{uniqueId}_{tag}).
    /// </summary>
    public string InstanceUniqueId { get; set; } = string.Empty;

    /// <summary>
    /// The values the user entered in the dynamic config UI, keyed by <see cref="PluginConfigField.Key"/>.
    /// For <see cref="PluginFieldType.MultiSelect"/>, values are comma-separated.
    /// </summary>
    public Dictionary<string, string> ConfigValues { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Extra data provided by <see cref="IStorkDropPlugin"/> implementations.
    /// </summary>
    public Dictionary<string, object> PluginData { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// Optional callback that plugins can invoke to log messages during execution.
    /// Messages are forwarded to the installation tracker's log entries.
    /// </summary>
    public Action<string>? Log { get; set; }

    /// <summary>
    /// Optional callback that plugins can invoke to show a prompt dialog during execution.
    /// Returns the user's choice. Returns a cancelled result if the callback is null.
    /// </summary>
    public Func<Models.PluginPrompt, Models.PluginPromptResult>? Prompt { get; set; }
}
