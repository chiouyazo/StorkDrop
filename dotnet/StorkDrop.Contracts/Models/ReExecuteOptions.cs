namespace StorkDrop.Contracts.Models;

/// <summary>
/// Options controlling which phases to run during plugin re-execution.
/// </summary>
public sealed class ReExecuteOptions
{
    public bool RunPreInstall { get; set; } = true;
    public bool RunPostInstall { get; set; } = true;
    public bool RunFileHandlers { get; set; } = false;
    public Dictionary<string, string>? PluginConfigValues { get; set; }
}
