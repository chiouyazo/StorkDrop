namespace StorkDrop.Contracts.Models;

/// <summary>
/// Describes a single action that a plugin performs during a specific phase.
/// </summary>
public sealed class PluginActionDescription
{
    /// <summary>
    /// The phase this action belongs to (PreInstall or PostInstall).
    /// </summary>
    public PluginActionPhase Phase { get; set; }

    /// <summary>
    /// Short title of the action (e.g., "Create database tables").
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Longer description of what this action does.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
