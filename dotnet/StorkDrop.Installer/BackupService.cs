using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

public sealed class BackupService : IBackupService
{
    public const int DefaultRetainedBackups = 3;

    private readonly string _backupRoot;
    private readonly int _retainedBackups;

    public BackupService()
    {
        _backupRoot = StorkPaths.BackupRoot;
        _retainedBackups = DefaultRetainedBackups;
        Directory.CreateDirectory(_backupRoot);
    }

    public BackupService(string backupRoot, int retainedBackups = DefaultRetainedBackups)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retainedBackups, 1);
        _backupRoot = backupRoot;
        _retainedBackups = retainedBackups;
        Directory.CreateDirectory(_backupRoot);
    }

    public async Task<string> CreateBackupAsync(
        string productId,
        string sourcePath,
        IReadOnlyList<string> relativeFiles,
        CancellationToken cancellationToken = default
    )
    {
        if (relativeFiles.Count == 0)
            throw new ArgumentException("A backup needs at least one file.", nameof(relativeFiles));

        string productBackupDir = Path.Combine(_backupRoot, productId);
        Directory.CreateDirectory(productBackupDir);

        string instanceTag = GetInstanceTag(sourcePath);
        string baseName = $"{productId}-{instanceTag}-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}";
        string tempPath = Path.Combine(productBackupDir, $".{Guid.NewGuid():N}.tmp");

        string backupPath;
        try
        {
            await Task.Run(
                () => WriteVerifiedArchive(tempPath, sourcePath, relativeFiles, cancellationToken),
                cancellationToken
            );
            backupPath = MoveToUniqueName(tempPath, productBackupDir, baseName);
        }
        catch (Exception)
        {
            DeleteQuietly(tempPath);
            throw;
        }

        PruneOldBackups(productBackupDir, productId, instanceTag, backupPath);
        return backupPath;
    }

    private static void WriteVerifiedArchive(
        string archivePath,
        string sourcePath,
        IReadOnlyList<string> relativeFiles,
        CancellationToken cancellationToken
    )
    {
        List<string> written = [];
        using (
            FileStream zipStream = new FileStream(
                archivePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough
            )
        )
        using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            foreach (string relativePath in relativeFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fullPath = Path.Combine(sourcePath, relativePath);
                if (!File.Exists(fullPath))
                    continue;

                string entryName = relativePath.Replace('\\', '/');
                archive.CreateEntryFromFile(fullPath, entryName, CompressionLevel.Optimal);
                written.Add(entryName);
            }
        }

        if (written.Count == 0)
            throw new InvalidOperationException(
                "None of the files to back up exist, so no backup was created."
            );

        VerifyArchive(archivePath, written, cancellationToken);
    }

    private static void VerifyArchive(
        string archivePath,
        List<string> expected,
        CancellationToken cancellationToken
    )
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count != expected.Count)
            throw new InvalidDataException(
                $"Backup verification failed: expected {expected.Count} entries but found {archive.Entries.Count}."
            );

        for (int index = 0; index < expected.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = archive.Entries[index];
            if (entry.FullName != expected[index])
                throw new InvalidDataException(
                    $"Backup verification failed for '{expected[index]}'."
                );

            using Stream stream = entry.Open();
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                total += read;
            if (total != entry.Length)
                throw new InvalidDataException(
                    $"Backup verification failed for '{entry.FullName}': content is truncated."
                );
        }
    }

    private static string MoveToUniqueName(string tempPath, string directory, string baseName)
    {
        for (int attempt = 0; ; attempt++)
        {
            string fileName = attempt == 0 ? $"{baseName}.zip" : $"{baseName}-{attempt}.zip";
            string candidate = Path.Combine(directory, fileName);
            try
            {
                File.Move(tempPath, candidate);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                continue;
            }
        }
    }

    private void PruneOldBackups(
        string productBackupDir,
        string productId,
        string instanceTag,
        string keepPath
    )
    {
        Regex ownBackup = new Regex(
            $"^{Regex.Escape(productId)}-{instanceTag}-\\d{{8}}-\\d{{9}}(-\\d+)?\\.zip$",
            RegexOptions.CultureInvariant
        );

        List<FileInfo> own = new DirectoryInfo(productBackupDir)
            .GetFiles("*.zip")
            .Where(f => ownBackup.IsMatch(f.Name))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ThenByDescending(f => f.Name, StringComparer.Ordinal)
            .ToList();

        foreach (FileInfo stale in own.Skip(_retainedBackups))
        {
            if (string.Equals(stale.FullName, keepPath, StringComparison.OrdinalIgnoreCase))
                continue;

            DeleteQuietly(stale.FullName);
        }
    }

    private static string GetInstanceTag(string sourcePath)
    {
        string normalized = Path.GetFullPath(sourcePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hash.AsSpan(0, 4));
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
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
