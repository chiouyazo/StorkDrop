namespace StorkDrop.Contracts.Models;

/// <summary>
/// Represents one selectable phase from one plugin in the action configuration dialog.
/// Groups are shown with a checkbox header that enables/disables the entire phase.
/// </summary>
public sealed class PluginActionGroup
{
    /// <summary>
    /// Unique identifier for this group (e.g., "preinstall-MyPlugin").
    /// </summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// Display title shown as the group header (e.g., "PreInstall: Database Setup").
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Which plugin lifecycle phase this group belongs to.
    /// </summary>
    public PluginActionPhase Phase { get; set; }

    /// <summary>
    /// Whether this group is enabled by default. Users can toggle this in the dialog.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Configuration fields to show inside this group. Empty for phases that
    /// have no configurable fields (e.g., PostInstall reuses PreInstall config).
    /// </summary>
    public IReadOnlyList<PluginConfigField> Fields { get; set; } = [];

    /// <summary>
    /// Optional descriptions of what this phase does, from IDescribableStorkPlugin.
    /// Shown as bullet points under the group header.
    /// </summary>
    public IReadOnlyList<PluginActionDescription> Descriptions { get; set; } = [];
}
