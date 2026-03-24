using System.IO.Compression;
using StorkDrop.Core.Interfaces;

namespace StorkDrop.Installer;

public sealed class BackupService : IBackupService
{
    private readonly string _backupRoot;

    public BackupService()
    {
        _backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StorkDrop",
            "Backups"
        );
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
                    ZipFile.CreateFromDirectory(
                        sourcePath,
                        backupPath,
                        CompressionLevel.Optimal,
                        includeBaseDirectory: false
                    );
                }
                catch
                {
                    // Clean up partial backup file on failure
                    if (File.Exists(backupPath))
                    {
                        try
                        {
                            File.Delete(backupPath);
                        }
                        catch
                        {
                            // Best-effort cleanup
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

        if (Directory.Exists(targetPath))
            Directory.Delete(targetPath, recursive: true);

        Directory.CreateDirectory(targetPath);

        await Task.Run(
            () =>
            {
                try
                {
                    ZipFile.ExtractToDirectory(backupPath, targetPath, overwriteFiles: true);
                }
                catch
                {
                    // Clean up partially extracted directory on failure
                    if (Directory.Exists(targetPath))
                    {
                        try
                        {
                            Directory.Delete(targetPath, recursive: true);
                        }
                        catch
                        {
                            // Best-effort cleanup
                        }
                    }
                    throw;
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
