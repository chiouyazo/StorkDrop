namespace StorkDrop.Contracts.Models;

/// <summary>
/// Describes a single toggleable action step that a plugin performs during a specific phase.
/// Each action has its own config fields and can be enabled/disabled independently by the user.
/// </summary>
public sealed class PluginActionDescription
{
    /// <summary>
    /// The phase this action belongs to (PreInstall or PostInstall).
    /// </summary>
    public PluginActionPhase Phase { get; set; }

    /// <summary>
    /// Short title of the action shown as the group header in the dialog.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Longer description of what this action does.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Configuration fields that belong to this action step.
    /// Shown inside the action's toggleable group in the dialog.
    /// </summary>
    public List<PluginConfigField> Fields { get; set; } = [];

    /// <summary>
    /// Whether this action is enabled by default.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
