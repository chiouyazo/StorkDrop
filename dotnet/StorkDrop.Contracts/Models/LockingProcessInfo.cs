namespace StorkDrop.Contracts.Models;

public sealed class LockingProcessInfo
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public DateTime? StartTime { get; init; }
    public uint SessionId { get; init; }
}
