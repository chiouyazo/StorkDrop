using System.IO;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Demo.Services;

internal sealed class DemoFileLockDetector : IFileLockDetector
{
    private readonly DemoLockState _locks;

    public DemoFileLockDetector(DemoLockState locks)
    {
        _locks = locks;
    }

    public IReadOnlyList<string> GetLockingProcesses(string filePath) =>
        _locks
            .GetLocked([filePath])
            .SelectMany(l => l.Processes.Select(p => p.ProcessName))
            .ToList();

    public bool IsFileLocked(string filePath) => _locks.GetLocked([filePath]).Count > 0;

    public void ThrowIfAnyLocked(string directory) { }

    public IReadOnlyList<LockedFileInfo> GetLockedFiles(string directory) =>
        Directory.Exists(directory)
            ? _locks.GetLocked(Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            : [];

    public IReadOnlyList<LockedFileInfo> GetLockedFiles(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default
    ) => _locks.GetLocked(filePaths);

    public bool TryKillProcess(int processId) => _locks.Release(processId);
}
