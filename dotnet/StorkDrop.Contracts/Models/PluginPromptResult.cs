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
}
