namespace StorkDrop.Contracts.Interfaces;

public interface IBackupService
{
    /// <summary>
    /// Backs up only <paramref name="relativeFiles"/> (relative to <paramref name="sourcePath"/>)
    /// into a zip, so a product installed into a large shared folder is not copied wholesale.
    /// </summary>
    Task<string> CreateBackupAsync(
        string productId,
        string sourcePath,
        IReadOnlyList<string> relativeFiles,
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
