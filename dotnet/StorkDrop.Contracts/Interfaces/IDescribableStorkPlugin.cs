using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Interfaces;

/// <summary>
/// Optional interface that plugins can implement to describe what their Pre/PostInstall
/// steps do. Used to show users what will happen before re-executing plugin actions.
/// </summary>
public interface IDescribableStorkPlugin
{
    /// <summary>
    /// Returns descriptions of the actions this plugin performs during each phase.
    /// </summary>
    /// <param name="environment">The plugin environment with config and version info.</param>
    /// <returns>A list of action descriptions grouped by phase.</returns>
    IReadOnlyList<PluginActionDescription> GetActionDescriptions(PluginEnvironment environment);
}
