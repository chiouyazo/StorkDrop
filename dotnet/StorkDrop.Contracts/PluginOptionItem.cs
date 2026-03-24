namespace StorkDrop.Contracts;

/// <summary>
/// Represents a single option in a dropdown or multi-select configuration field.
/// </summary>
public sealed class PluginOptionItem
{
    /// <summary>
    /// Gets or sets the value stored when this option is selected.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display label shown to the user.
    /// </summary>
    public string Label { get; set; } = string.Empty;
}
