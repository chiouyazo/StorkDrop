using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Demo.Services;

internal sealed class DemoBackupService : IBackupService
{
    public Task<string> CreateBackupAsync(
        string productId,
        string sourcePath,
        CancellationToken ct = default
    ) => Task.FromResult(string.Empty);

    public Task RestoreBackupAsync(
        string backupPath,
        string targetPath,
        CancellationToken ct = default
    ) => Task.CompletedTask;

    public Task<IReadOnlyList<string>> ListBackupsAsync(
        string productId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<string>>([]);

    public Task DeleteBackupAsync(string backupPath, CancellationToken ct = default) =>
        Task.CompletedTask;
}
