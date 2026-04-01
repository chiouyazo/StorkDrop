namespace StorkDrop.Contracts.Interfaces;

public interface IInteractiveStorkPlugin
{
    PluginButtonResult OnButtonClicked(string fieldKey, Dictionary<string, string> currentValues);
}

public sealed class PluginButtonResult
{
    public string? StatusText { get; set; }
    public bool IsError { get; set; }
    public IReadOnlyList<PluginConfigField>? UpdatedSchema { get; set; }
}
