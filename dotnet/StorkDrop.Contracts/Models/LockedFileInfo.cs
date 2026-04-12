namespace StorkDrop.Contracts.Models;

public sealed class LockedFileInfo
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public List<LockingProcessInfo> Processes { get; init; } = [];
}
