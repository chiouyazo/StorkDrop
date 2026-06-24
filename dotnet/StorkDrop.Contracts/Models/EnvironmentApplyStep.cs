namespace StorkDrop.Contracts.Models;

public sealed class EnvironmentApplyStep
{
    public string Id { get; set; } = "";
    public string? Version { get; set; }
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public long DurationMs { get; set; }
}
