namespace StorkDrop.Installer;

/// <summary>
/// Thrown when a file cannot be modified because it is locked by another process.
/// </summary>
public sealed class FileLockedException : Exception
{
    public string FileName { get; }
    public string ProcessNames { get; }

    public FileLockedException(string fileName, string processNames)
        : base($"File locked: {fileName}")
    {
        FileName = fileName;
        ProcessNames = processNames;
    }
}
