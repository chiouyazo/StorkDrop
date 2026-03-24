namespace StorkDrop.Contracts.Interfaces;

public interface IFileLockDetector
{
    IReadOnlyList<string> GetLockingProcesses(string filePath);
    bool IsFileLocked(string filePath);
}
