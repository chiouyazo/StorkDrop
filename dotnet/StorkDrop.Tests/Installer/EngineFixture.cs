using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Installer;

namespace StorkDrop.Tests.Installer;

internal sealed class EngineFixture
{
    public const string FeedId = "feed";

    public IFeedRegistry Feeds { get; } = Substitute.For<IFeedRegistry>();
    public IRegistryClient Client { get; } = Substitute.For<IRegistryClient>();
    public IProductRepository Repository { get; } = Substitute.For<IProductRepository>();
    public IBackupService Backup { get; } = Substitute.For<IBackupService>();
    public IFileLockDetector LockDetector { get; } = Substitute.For<IFileLockDetector>();
    public IActivityLog Activity { get; } = Substitute.For<IActivityLog>();
    public InstallationEngine Engine { get; }
    public string ProductId { get; } = "engine-test-" + Guid.NewGuid().ToString("N")[..10];

    public EngineFixture(
        bool hasFeeds = true,
        Func<string, long?>? availableBytes = null,
        PackageDownloadOptions? downloadOptions = null
    )
    {
        Feeds.GetFeeds().Returns(hasFeeds ? [new FeedInfo(FeedId, "Feed")] : []);
        Feeds.GetClient(FeedId).Returns(Client);
        Repository
            .GetByIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<InstalledProduct?>(null));
        Repository
            .GetInstancesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<InstalledProduct>>([]));
        Repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<InstalledProduct>>([]));
        LockDetector
            .GetLockedFiles(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        IFeedReportService feedReport = Substitute.For<IFeedReportService>();
        EnvironmentVariableService envVars = new EnvironmentVariableService(
            NullLogger<EnvironmentVariableService>.Instance
        );
        UninstallService uninstall = new UninstallService(
            Repository,
            Activity,
            LockDetector,
            envVars,
            new DeferredFileOps(),
            feedReport,
            NullLogger<UninstallService>.Instance
        );

        Engine = new InstallationEngine(
            Feeds,
            Repository,
            Backup,
            LockDetector,
            Activity,
            Substitute.For<IConfigurationService>(),
            Substitute.For<IEncryptionService>(),
            [],
            envVars,
            uninstall,
            feedReport,
            NullLogger<InstallationEngine>.Instance,
            new PackageDownloader(
                NullLogger.Instance,
                downloadOptions
                    ?? new PackageDownloadOptions
                    {
                        Delay = (_, _) => Task.CompletedTask,
                        ReadTimeout = TimeSpan.FromSeconds(10),
                        AvailableBytes = _ => null,
                    }
            ),
            availableBytes ?? (_ => null)
        );
    }

    public ProductManifest Manifest(string version = "1.0.0", string? sha256 = null) =>
        new ProductManifest(
            ProductId: ProductId,
            Title: "Engine Test",
            Version: version,
            ReleaseDate: new DateOnly(2026, 1, 1),
            InstallType: InstallType.Plugin,
            ContentSha256: sha256
        );

    public InstalledProduct Installed(string installedPath, string version = "0.9.0") =>
        new InstalledProduct(
            ProductId: ProductId,
            InstanceId: "default",
            Title: "Engine Test",
            Version: version,
            InstalledPath: installedPath,
            InstalledDate: DateTime.UtcNow,
            FeedId: FeedId,
            InstanceUniqueId: "abcd1234"
        );

    public InstallOptions Options(string targetPath, bool backup = false) =>
        new InstallOptions(TargetPath: targetPath, FeedId: FeedId, CreateBackup: backup);

    public void ServeBytes(byte[] package) =>
        Client
            .DownloadProductAsync(ProductId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(package)));

    public static byte[] Zip(params (string Name, string Content)[] files)
    {
        using MemoryStream buffer = new MemoryStream();
        using (ZipArchive archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            foreach ((string name, string content) in files)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                using StreamWriter writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }
        return buffer.ToArray();
    }

    public static string Sha256(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
