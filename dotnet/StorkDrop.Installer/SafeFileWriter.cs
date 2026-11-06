using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

internal static class SafeFileWriter
{
    public static Task WriteAtomicAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken = default
    ) => AtomicFile.WriteAllTextAsync(filePath, content, keepBackup: false, cancellationToken);

    public static Task WriteAtomicWithBackupAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken = default
    ) => AtomicFile.WriteAllTextAsync(filePath, content, keepBackup: true, cancellationToken);
}
