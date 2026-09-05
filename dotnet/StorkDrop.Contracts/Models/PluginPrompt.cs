namespace StorkDrop.Contracts.Models;

/// <summary>
/// A prompt shown to the user during plugin execution.
/// Plugins build a prompt and pass it to <see cref="PluginContext.Prompt"/>.
/// </summary>
public sealed class PluginPrompt
{
    /// <summary>
    /// Dialog title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Main message body. Can be multi-line.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Optional detail text shown in a scrollable/expandable area.
    /// Use this for diffs, code snippets, long content, etc.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// The options the user can choose from. Each string is a button label.
    /// The callback returns the index of the chosen option.
    /// </summary>
    public List<string> Options { get; set; } = [];

    /// <summary>
    /// Index of the default/recommended option (shown as primary button).
    /// -1 for no default.
    /// </summary>
    public int DefaultOptionIndex { get; set; } = -1;

    /// <summary>
    /// Optional input fields shown between the message and the option buttons, so the plugin can
    /// collect checkbox/selection/text input alongside the button choice. Entered values are returned
    /// in <see cref="PluginPromptResult.FieldValues"/>. Empty for a plain button prompt.
    /// </summary>
    public List<PluginPromptField> Fields { get; set; } = [];
}
