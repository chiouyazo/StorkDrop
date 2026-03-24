using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Interfaces;

/// <summary>
/// Defines the contract for the installation engine that handles product installation,
/// update, and uninstall operations.
/// </summary>
public interface IInstallationEngine
{
    /// <summary>
    /// Gets the plugin configuration schema for the specified product manifest.
    /// </summary>
    /// <param name="manifest">The product manifest to get configuration for.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of plugin configuration fields.</returns>
    Task<IReadOnlyList<PluginConfigField>> GetPluginConfigurationAsync(
        ProductManifest manifest,
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
}
