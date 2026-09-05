namespace StorkDrop.Contracts.Models;

/// <summary>
/// An input field shown inside a <see cref="PluginPrompt"/> so a plugin can collect a decision
/// (checkbox, selection, text) at execution time - after a runtime check - instead of only up front
/// in the config dialog. Entered values come back in <see cref="PluginPromptResult.FieldValues"/>,
/// keyed by <see cref="Key"/>.
/// </summary>
public sealed class PluginPromptField
{
    /// <summary>Key the entered value is returned under in <see cref="PluginPromptResult.FieldValues"/>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Label shown next to (or as) the control.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Control type. Supported in a prompt: <see cref="PluginFieldType.Checkbox"/>,
    /// <see cref="PluginFieldType.MultiSelect"/>, <see cref="PluginFieldType.Dropdown"/>,
    /// <see cref="PluginFieldType.Text"/>, <see cref="PluginFieldType.Password"/>,
    /// <see cref="PluginFieldType.Number"/>. Others fall back to a text box.
    /// </summary>
    public PluginFieldType FieldType { get; set; } = PluginFieldType.Text;

    /// <summary>
    /// Initial value. For <see cref="PluginFieldType.Checkbox"/> use <c>"true"</c>/<c>"false"</c>;
    /// for <see cref="PluginFieldType.MultiSelect"/> a comma-separated list of pre-selected option values.
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>Options for <see cref="PluginFieldType.Dropdown"/> and <see cref="PluginFieldType.MultiSelect"/>.</summary>
    public List<PluginOptionItem> Options { get; set; } = [];
}
