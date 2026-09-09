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

        List<string> unresolved = [];

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

                    try
                    {
                        entry.ExtractToFile(destination, overwrite: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        if (!TryRestoreDeferred(entry, destination))
                            unresolved.Add(entry.FullName);
                    }
                }
            },
            cancellationToken
        );

        if (unresolved.Count > 0)
        {
            string sample = string.Join(", ", unresolved.Take(5));
            throw new IOException(
                $"{unresolved.Count} file(s) could not be restored because they are locked by a "
                    + $"running process (e.g. {sample}). Please stop the product's services/programs "
                    + "and try again."
            );
        }
    }

    private static bool TryRestoreDeferred(ZipArchiveEntry entry, string destination)
    {
        string pending = $"{destination}.pending-restore-{Guid.NewGuid():N}";
        try
        {
            entry.ExtractToFile(pending, overwrite: true);
            if (new DeferredFileOps().ScheduleMoveOnReboot(pending, destination))
                return true;

            if (File.Exists(pending))
                File.Delete(pending);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (File.Exists(pending))
                    File.Delete(pending);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Leftover pending file is harmless; it never replaces the target.
            }
            return false;
        }
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
