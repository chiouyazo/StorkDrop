using System.IO.Compression;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

public sealed class BackupService : IBackupService
{
    private readonly string _backupRoot;

    public BackupService()
    {
        _backupRoot = StorkPaths.BackupRoot;
        Directory.CreateDirectory(_backupRoot);
    }

    public BackupService(string backupRoot)
    {
        _backupRoot = backupRoot;
        Directory.CreateDirectory(_backupRoot);
    }

    public async Task<string> CreateBackupAsync(
        string productId,
        string sourcePath,
        IReadOnlyList<string> relativeFiles,
        CancellationToken cancellationToken = default
    )
    {
        string productBackupDir = Path.Combine(_backupRoot, productId);
        Directory.CreateDirectory(productBackupDir);

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string backupFileName = $"{productId}-{timestamp}.zip";
        string backupPath = Path.Combine(productBackupDir, backupFileName);

        await Task.Run(
            () =>
            {
                try
                {
                    using FileStream zipStream = new FileStream(backupPath, FileMode.Create);
                    using ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
                    foreach (string relativePath in relativeFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string fullPath = Path.Combine(sourcePath, relativePath);
                        if (!File.Exists(fullPath))
                            continue;

                        string entryName = relativePath.Replace('\\', '/');
                        archive.CreateEntryFromFile(fullPath, entryName, CompressionLevel.Optimal);
                    }
                }
                catch (Exception)
                {
                    if (File.Exists(backupPath))
                    {
                        try
                        {
                            File.Delete(backupPath);
                        }
                        catch (Exception)
                        {
                            // Best effort cleanup
                        }
                    }
                    throw;
                }
            },
            cancellationToken
        );

        return backupPath;
    }

    public async Task RestoreBackupAsync(
        string backupPath,
        string targetPath,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file not found.", backupPath);

        Directory.CreateDirectory(targetPath);
        string targetRoot = Path.GetFullPath(targetPath);

        await Task.Run(
            () =>
            {
                using FileStream zipStream = new FileStream(
                    backupPath,
                    FileMode.Open,
                    FileAccess.Read
                );
                using ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(entry.Name))
                        continue; // directory marker

                    string destination = Path.GetFullPath(Path.Combine(targetPath, entry.FullName));
                    // Guard against zip-slip: never write outside the target folder.
                    if (
                        !destination.StartsWith(
                            targetRoot + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && !string.Equals(
                            destination,
                            targetRoot,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);
                }
            },
            cancellationToken
        );
    }

    public Task<IReadOnlyList<string>> ListBackupsAsync(
        string productId,
        CancellationToken cancellationToken = default
    )
    {
        string productBackupDir = Path.Combine(_backupRoot, productId);

        if (!Directory.Exists(productBackupDir))
            return Task.FromResult<IReadOnlyList<string>>([]);

        List<string> backups = Directory
            .GetFiles(productBackupDir, "*.zip")
            .OrderByDescending(f => f)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(backups);
    }

    public Task DeleteBackupAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        if (File.Exists(backupPath))
            File.Delete(backupPath);

        return Task.CompletedTask;
    }
}
