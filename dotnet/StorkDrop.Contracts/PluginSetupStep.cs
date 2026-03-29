namespace StorkDrop.Contracts;

/// <summary>
/// Represents a setup wizard step contributed by a plugin.
/// </summary>
public sealed class PluginSetupStep
{
    /// <summary>
    /// Gets or sets the unique identifier for this step.
    /// </summary>
    public string StepId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title displayed in the wizard.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description displayed in the wizard.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Configuration fields for this step.
    /// </summary>
    public List<PluginConfigField> Fields { get; set; } = new List<PluginConfigField>();
}
