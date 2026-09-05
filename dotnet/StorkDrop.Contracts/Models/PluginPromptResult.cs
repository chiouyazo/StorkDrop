namespace StorkDrop.Contracts.Models;

/// <summary>
/// Result of a plugin prompt.
/// </summary>
public sealed class PluginPromptResult
{
    /// <summary>
    /// Index of the option the user chose, or -1 if dismissed/cancelled.
    /// </summary>
    public int ChosenIndex { get; set; } = -1;

    /// <summary>
    /// True if the user dismissed the dialog without choosing an option.
    /// </summary>
    public bool Cancelled => ChosenIndex < 0;

    /// <summary>
    /// Values entered in the prompt's <see cref="PluginPrompt.Fields"/>, keyed by
    /// <see cref="PluginPromptField.Key"/>. Checkbox values are <c>"true"</c>/<c>"false"</c>,
    /// multi-select values are comma-separated. Empty when the prompt had no fields or was cancelled.
    /// </summary>
    public Dictionary<string, string> FieldValues { get; set; } = new Dictionary<string, string>();

    /// <summary>Returns the raw value entered for <paramref name="key"/>, or null if absent.</summary>
    public string? GetValue(string key) =>
        FieldValues.TryGetValue(key, out string? value) ? value : null;

    /// <summary>Returns true when a checkbox field <paramref name="key"/> was checked.</summary>
    public bool GetBool(string key) =>
        string.Equals(GetValue(key), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the selected values of a multi-select field <paramref name="key"/>.</summary>
    public IReadOnlyList<string> GetSelected(string key) =>
        GetValue(key) is { Length: > 0 } value
            ? value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            : [];
}
