namespace StorkDrop.Contracts.Models;

public sealed class EnvironmentApplyReport
{
    public bool Success { get; set; }
    public List<EnvironmentApplyStep> Steps { get; set; } = [];
}
