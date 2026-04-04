using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Demo.Services;

internal sealed class DemoFileLockDetector : IFileLockDetector
{
    public IReadOnlyList<string> GetLockingProcesses(string filePath) => [];

    public bool IsFileLocked(string filePath) => false;

    public void ThrowIfAnyLocked(string directory) { }
}
