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
    /// </summary>
    PluginButtonResult OnButtonClicked(string fieldKey, Dictionary<string, string> currentValues);
}
