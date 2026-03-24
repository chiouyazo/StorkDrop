using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StorkDrop.Contracts;

/// <summary>
/// Implement this interface to create a StorkDrop plugin that extends
/// the application with custom setup steps, settings, and install hooks.
/// </summary>
public interface IStorkDropPlugin
{
    /// <summary>
    /// Gets the unique identifier for this plugin.
    /// </summary>
    string PluginId { get; }

    /// <summary>
    /// Gets the human-readable display name for this plugin.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Extra setup wizard steps this plugin contributes.
    /// </summary>
    /// <returns>A list of setup steps to add to the wizard.</returns>
    IReadOnlyList<PluginSetupStep> GetSetupSteps();

    /// <summary>
    /// Extra settings sections this plugin contributes.
    /// </summary>
    /// <returns>A list of settings sections to add to the settings UI.</returns>
    IReadOnlyList<PluginSettingsSection> GetSettingsSections();

    /// <summary>
    /// Called when a product from a feed assigned to this plugin is installed.
    /// </summary>
    /// <param name="context">Context information about the installed product.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnProductInstalledAsync(PluginInstallContext context, CancellationToken ct = default);

    /// <summary>
    /// Called when a product is uninstalled.
    /// </summary>
    /// <param name="productId">The unique identifier of the uninstalled product.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnProductUninstalledAsync(string productId, CancellationToken ct = default);

    /// <summary>
    /// Feed URLs this plugin is responsible for.
    /// </summary>
    string[] AssociatedFeeds { get; }

    /// <summary>
    /// Returns navigation tabs this plugin contributes to the sidebar.
    /// </summary>
    /// <returns>A list of navigation tabs to add to the sidebar.</returns>
    IReadOnlyList<PluginNavTab> GetNavigationTabs();

    /// <summary>
    /// Called when the user clicks on a plugin-contributed navigation tab.
    /// </summary>
    /// <param name="tabId">The unique identifier of the clicked tab.</param>
    void OnNavigationTabSelected(string tabId);
}
