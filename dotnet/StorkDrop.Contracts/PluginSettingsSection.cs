namespace StorkDrop.Contracts;

/// <summary>
/// Represents a settings section contributed by a plugin.
/// </summary>
public sealed class PluginSettingsSection
{
    /// <summary>
    /// Gets or sets the unique identifier for this settings section.
    /// </summary>
    public string SectionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title displayed in the settings UI.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the configuration fields for this settings section.
    /// </summary>
    public List<PluginConfigField> Fields { get; set; } = new List<PluginConfigField>();
}
