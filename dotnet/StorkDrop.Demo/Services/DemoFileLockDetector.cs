using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Demo.Services;

internal sealed class DemoFileLockDetector : IFileLockDetector
{
    public IReadOnlyList<string> GetLockingProcesses(string filePath) => [];

    public bool IsFileLocked(string filePath) => false;

    public void ThrowIfAnyLocked(string directory) { }

    public IReadOnlyList<LockedFileInfo> GetLockedFiles(string directory) => [];

    public bool TryKillProcess(int processId) => true;
}
