using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Interfaces;

/// <summary>
/// Callback that the UI layer provides to show config fields to the user and return their selections.
/// Returns null if the user cancels.
/// </summary>
public delegate Dictionary<string, string>? FileHandlerConfigCallback(
    IReadOnlyList<PluginConfigField> fields,
    Dictionary<string, string> currentValues
);

/// <summary>
/// Callback that plugins can set to resolve template variables in install paths.
/// Receives the raw target path and the file handler context (if available).
/// Returns the resolved path, or null to keep the original.
/// </summary>
public delegate string? InstallPathResolverCallback(
    string targetPath,
    PluginContext? fileHandlerContext
);

/// <summary>
/// Callback that receives action groups (phases with config fields) and returns
/// the user's configuration values. Disabled groups are indicated by special keys
/// (__group_enabled_{groupId} = "false"). Returns null if the user cancels.
/// </summary>
public delegate Dictionary<string, string>? ActionGroupConfigCallback(
    IReadOnlyList<PluginActionGroup> groups,
    Dictionary<string, string> currentValues
);

/// <summary>
/// Defines the contract for the installation engine that handles product installation,
/// update, and uninstall operations.
/// </summary>
public interface IInstallationEngine
{
    /// <summary>
    /// Set by plugins to resolve template variables in install paths (e.g. {ACMEPath}).
    /// Called before files are copied, after file handler config dialog has run.
    /// </summary>
    InstallPathResolverCallback? OnResolveInstallPath { get; set; }

    /// <summary>
    /// Set by the UI layer to provide a callback for showing file handler config dialogs.
    /// When a file type handler needs user input, this callback is invoked on the UI thread.
    /// </summary>
    FileHandlerConfigCallback? OnFileHandlerConfigNeeded { get; set; }

    /// <summary>
    /// Set by the UI layer to show product plugin configuration dialogs.
    /// Called after extraction, before PreInstall, when a product has IStorkPlugin with config fields.
    /// Returns null if the user cancels.
    /// </summary>
    FileHandlerConfigCallback? OnPluginConfigNeeded { get; set; }

    /// <summary>
    /// Set by the UI layer to show the unified action group configuration dialog.
    /// Receives all action groups (file handlers + product plugin phases) with their fields.
    /// Returns null if the user cancels.
    /// </summary>
    ActionGroupConfigCallback? OnActionGroupConfigNeeded { get; set; }

    IInteractiveStorkPlugin? CurrentInteractivePlugin { get; }

    /// <summary>
    /// Gets the plugin configuration schema for the specified product manifest.
    /// </summary>
    /// <param name="manifest">The product manifest to get configuration for.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of plugin configuration fields.</returns>
    Task<IReadOnlyList<PluginConfigField>> GetPluginConfigurationAsync(
        ProductManifest manifest,
        string? feedId = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Builds action groups for a product, including file handler groups and product plugin
    /// PreInstall/PostInstall groups with their config fields and action descriptions.
    /// </summary>
    Task<IReadOnlyList<PluginActionGroup>> GetActionGroupsAsync(
        ProductManifest manifest,
        string? feedId = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Installs a product using the specified manifest and options.
    /// </summary>
    /// <param name="manifest">The product manifest describing what to install.</param>
    /// <param name="options">Options controlling how the installation is performed.</param>
    /// <param name="progress">Progress reporter for installation status.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An <see cref="InstallResult"/> indicating success or failure with details.</returns>
    Task<InstallResult> InstallAsync(
        ProductManifest manifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Updates an installed product to a new version.
    /// </summary>
    /// <param name="installed">The currently installed product.</param>
    /// <param name="newManifest">The manifest for the new version.</param>
    /// <param name="options">Options controlling how the update is performed.</param>
    /// <param name="progress">Progress reporter for update status.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateAsync(
        InstalledProduct installed,
        ProductManifest newManifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Uninstalls the specified product.
    /// </summary>
    /// <param name="product">The installed product to uninstall.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UninstallAsync(InstalledProduct product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-executes plugin actions (PreInstall + PostInstall) on an already-installed product.
    /// Loads the plugin from .stork/, shows the config dialog pre-filled with previous values,
    /// and runs the plugin lifecycle without re-copying files.
    /// </summary>
    /// <param name="product">The installed product to re-execute plugins for.</param>
    /// <param name="progress">Progress reporter for status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An <see cref="InstallResult"/> indicating success or failure.</returns>
    Task<InstallResult> ReExecutePluginsAsync(
        InstalledProduct product,
        ReExecuteOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    );
}
