using StorkDrop.Contracts;

namespace StorkDrop.Contracts.Interfaces;

/// <summary>
/// The result returned by <see cref="IInteractiveStorkPlugin.OnButtonClicked"/>
/// after a button is clicked in the plugin configuration dialog.
/// </summary>
public sealed class PluginButtonResult
{
    /// <summary>
    /// Optional status text to display in the dialog after the button action completes.
    /// For example, "Connection successful" or "Invalid credentials".
    /// </summary>
    public string? StatusText { get; set; }

    /// <summary>
    /// Indicates whether the button action resulted in an error.
    /// When <see langword="true"/>, the <see cref="StatusText"/> is displayed as an error message.
    /// </summary>
    public bool IsError { get; set; }

    /// <summary>
    /// Optional updated configuration schema to replace the current fields in the dialog.
    /// Use this to dynamically add, remove, or modify fields based on the button action.
    /// When <see langword="null"/>, the existing schema remains unchanged.
    /// </summary>
    public IReadOnlyList<PluginConfigField>? UpdatedSchema { get; set; }
}
