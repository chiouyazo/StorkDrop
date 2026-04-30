namespace StorkDrop.Contracts.Interfaces;

/// <summary>
/// Implement this in your product DLL. StorkDrop calls <see cref="GetConfigurationSchema"/>
/// before install to build a dynamic UI, then passes the user's choices to
/// <see cref="PreInstallAsync"/> and <see cref="PostInstallAsync"/>.
/// </summary>
public interface IStorkPlugin
{
    /// <summary>
    /// Return the configuration fields the user must fill in before install.
    /// StorkDrop generates the UI dynamically from this schema.
    /// Return an empty list if no configuration is needed.
    /// </summary>
    /// <param name="environment">Read-only environment information for building dynamic options.</param>
    /// <returns>A list of configuration fields to display in the UI.</returns>
    IReadOnlyList<PluginConfigField> GetConfigurationSchema(PluginEnvironment environment);

    /// <summary>
    /// Runs before files are copied. Receives the user's configuration choices.
    /// Use this to validate, prepare databases, check prerequisites, etc.
    /// </summary>
    /// <param name="context">The full plugin context including user configuration values.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result indicating whether the pre-install phase succeeded.</returns>
    Task<PluginPreInstallResult> PreInstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Runs after files are copied and verified. Receives the same context.
    /// Use this to create DB entries, register components, run migrations, etc.
    /// </summary>
    /// <param name="context">The full plugin context including user configuration values.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PostInstallAsync(PluginContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs before files are removed during uninstall. Use this to check prerequisites,
    /// stop services, or prepare for file removal.
    /// </summary>
    /// <param name="context">The full plugin context for the product being uninstalled.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result indicating whether the pre-uninstall phase succeeded.</returns>
    Task<PluginPreInstallResult> PreUninstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Runs after files have been removed during uninstall. Use this to clean up
    /// database entries, registry keys, or other external resources.
    /// </summary>
    /// <param name="context">The full plugin context for the product that was uninstalled.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PostUninstallAsync(PluginContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called before the plugin's assembly load context is unloaded.
    /// Release all native resources, close connection pools, dispose handles, etc.
    /// This prevents native access violations during assembly unloading.
    /// Default implementation does nothing.
    /// </summary>
    void Cleanup() { }
}
