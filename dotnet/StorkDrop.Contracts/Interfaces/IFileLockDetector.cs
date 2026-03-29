namespace StorkDrop.Contracts.Interfaces;

public interface IFileLockDetector
{
    IReadOnlyList<string> GetLockingProcesses(string filePath);
    bool IsFileLocked(string filePath);

    /// <summary>
    /// Scans a directory for locked .exe/.dll files and throws FileLockedException if any are found.
    /// </summary>
    void ThrowIfAnyLocked(string directory);
}
