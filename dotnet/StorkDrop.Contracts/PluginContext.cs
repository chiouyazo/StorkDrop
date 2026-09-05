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
    /// The version that was installed before this operation, or null on a fresh install. Mirrors
    /// <see cref="PluginEnvironment.PreviousVersion"/> so update logic that must run in
    /// <see cref="Interfaces.IStorkPlugin.PreInstallAsync"/>/<c>PostInstallAsync</c> (e.g. after a
    /// runtime check) can tell an update from a fresh install without stashing it during the config
    /// phase.
    /// </summary>
    public string? PreviousVersion { get; set; }

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
    /// Used by SDKs to generate instance-unique keys (e.g. SD_{uniqueId}_{tag}).
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
    /// Free-form metadata declared by the product in its manifest (<c>ProductManifest.Metadata</c>).
    /// StorkDrop never interprets these values; they are passed through verbatim so a plugin can act
    /// on product-specific declarations such as a minimum required host version.
    /// </summary>
    public Dictionary<string, string> ProductMetadata { get; set; } =
        new Dictionary<string, string>();

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
