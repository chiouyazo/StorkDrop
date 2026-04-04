using System.Net.Http;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Registry;

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

internal sealed class DemoEncryptionService : IEncryptionService
{
    public string Encrypt(string plainText) => plainText;

    public string Decrypt(string encryptedText) => encryptedText;
}

internal sealed class DemoFileLockDetector : IFileLockDetector
{
    public IReadOnlyList<string> GetLockingProcesses(string filePath) => [];

    public bool IsFileLocked(string filePath) => false;

    public void ThrowIfAnyLocked(string directory) { }
}

internal sealed class DemoSelfUpdateChecker : ISelfUpdateChecker
{
    public Task<UpdateInfo?> CheckForUpdateAsync(
        bool includeDevVersions,
        CancellationToken ct = default
    ) => Task.FromResult<UpdateInfo?>(null);
}

internal sealed class DemoPluginSettingsStore : IPluginSettingsStore
{
    private readonly Dictionary<string, Dictionary<string, string>> _store = new Dictionary<
        string,
        Dictionary<string, string>
    >
    {
        ["demo-sql-tools"] = new Dictionary<string, string>
        {
            ["databases"] =
                """[{"name":"Production","server":"db-prod-01","database":"NovaDB","password":"secret"},{"name":"Staging","server":"db-staging-01","database":"NovaDB_Staging","password":"secret"}]""",
        },
    };

    public Task<Dictionary<string, string>> LoadAsync(
        string pluginId,
        CancellationToken ct = default
    ) => Task.FromResult(_store.GetValueOrDefault(pluginId) ?? new Dictionary<string, string>());

    public Task SaveAsync(
        string pluginId,
        Dictionary<string, string> values,
        CancellationToken ct = default
    )
    {
        _store[pluginId] = values;
        return Task.CompletedTask;
    }
}

internal sealed class DemoFeedConnectionService : IFeedConnectionService
{
    public HttpClient CreateAuthenticatedClient(
        string baseUrl,
        string? username,
        string? password
    ) => new HttpClient();

    public Task<FeedConnectionResult> TestConnectionAsync(
        string url,
        string? username,
        string? password,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new FeedConnectionResult(Success: true, RepositoryCount: 2));
}

internal sealed class DemoNotificationService : INotificationService
{
    public void ShowInfo(string title, string message) { }

    public void ShowSuccess(string title, string message) { }

    public void ShowWarning(string title, string message) { }

    public void ShowError(string title, string message) { }

    public void ShowUpdateAvailable(string productTitle, string version) { }
}
