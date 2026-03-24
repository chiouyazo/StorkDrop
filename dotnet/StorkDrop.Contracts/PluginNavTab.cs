namespace StorkDrop.Contracts;

/// <summary>
/// Represents a navigation tab contributed by a plugin in the sidebar.
/// </summary>
public sealed class PluginNavTab
{
    /// <summary>
    /// Gets or sets the unique identifier for this tab.
    /// </summary>
    public string TabId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name shown in the sidebar.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Segoe MDL2 icon character for this tab.
    /// </summary>
    public string Icon { get; set; } = string.Empty;
}
