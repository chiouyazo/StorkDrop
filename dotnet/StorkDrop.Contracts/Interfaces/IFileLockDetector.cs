using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Interfaces;

public interface IFileLockDetector
{
    IReadOnlyList<string> GetLockingProcesses(string filePath);
    bool IsFileLocked(string filePath);
    void ThrowIfAnyLocked(string directory);
    IReadOnlyList<LockedFileInfo> GetLockedFiles(string directory);

    /// <summary>
    /// Checks only the given files for locks, using a single Restart Manager session. Cheap and
    /// bounded even when many files are locked; honors <paramref name="cancellationToken"/>.
    /// </summary>
    IReadOnlyList<LockedFileInfo> GetLockedFiles(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default
    );

    bool TryKillProcess(int processId);
}
