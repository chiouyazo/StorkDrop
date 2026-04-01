using StorkDrop.Contracts;

namespace StorkDrop.Contracts.Interfaces;

/// <summary>
/// Optional interface that enables interactive button functionality in plugin configuration dialogs.
/// Implement this alongside <see cref="IStorkPlugin"/> when your plugin needs buttons that
/// perform actions (e.g., "Test Connection", "Browse...") and optionally update the config schema.
/// </summary>
public interface IInteractiveStorkPlugin
{
    /// <summary>
    /// Called when the user clicks a button in the plugin configuration dialog.
    /// Use this to perform validation, test connections, fetch dynamic data, or
    /// update the configuration schema based on user interaction.
    /// </summary>
    /// <param name="fieldKey">The key of the button field that was clicked.</param>
    /// <param name="currentValues">The current values of all configuration fields at the time of the click.</param>
    /// <returns>A result containing optional status text, error state, and an optionally updated schema.</returns>
    PluginButtonResult OnButtonClicked(string fieldKey, Dictionary<string, string> currentValues);
}
