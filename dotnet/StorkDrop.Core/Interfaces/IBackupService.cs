namespace StorkDrop.Core.Interfaces;

public interface IBackupService
{
    Task<string> CreateBackupAsync(
        string productId,
        string sourcePath,
        CancellationToken cancellationToken = default
    );
    Task RestoreBackupAsync(
        string backupPath,
        string targetPath,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<string>> ListBackupsAsync(
        string productId,
        CancellationToken cancellationToken = default
    );
    Task DeleteBackupAsync(string backupPath, CancellationToken cancellationToken = default);
}
