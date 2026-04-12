using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Interfaces;

public interface IFileLockDetector
{
    IReadOnlyList<string> GetLockingProcesses(string filePath);
    bool IsFileLocked(string filePath);
    void ThrowIfAnyLocked(string directory);
    IReadOnlyList<LockedFileInfo> GetLockedFiles(string directory);
    bool TryKillProcess(int processId);
}
