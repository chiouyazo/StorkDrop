using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Installer;

namespace StorkDrop.Tests.Installer;

internal sealed class EngineTestHarness : IDisposable
{
    public const string FeedId = "test-feed";

    private Dictionary<string, string> _package = [];
    private readonly Dictionary<string, byte[]> _binaryPackage = [];

    private readonly IsolatedStorkEnvironment _isolation = new IsolatedStorkEnvironment();

    public EngineTestHarness()
    {
        ProductId = "enginetest-" + Guid.NewGuid().ToString("N")[..8];
        UniqueId = "ab12cd34";
        Root = Path.Combine(
            Path.GetTempPath(),
            "storkdrop-engine-tests",
            Guid.NewGuid().ToString("N")
        );
        InstallPath = Path.Combine(Root, "install");
        BackupRoot = Path.Combine(Root, "backups");
        Directory.CreateDirectory(InstallPath);

        Installed = new InstalledProduct(
            ProductId,
            InstanceIdHelper.DefaultInstanceId,
            "Test product",
            "1.0.0",
            InstallPath,
            DateTime.UtcNow,
            FeedId,
            null,
            null,
            null,
            null,
            UniqueId
        );

        Registry = Substitute.For<IRegistryClient>();
        Registry
            .DownloadProductAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Task.FromResult<Stream>(BuildPackage()));

        IFeedRegistry feedRegistry = Substitute.For<IFeedRegistry>();
        feedRegistry.GetClient(Arg.Any<string>()).Returns(Registry);
        feedRegistry.GetFeeds().Returns([new FeedInfo(FeedId, "Test feed")]);

        Repository = Substitute.For<IProductRepository>();
        Repository
            .GetByIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<InstalledProduct?>(Installed));
        Repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<InstalledProduct>>([Installed]));
        Repository
            .GetInstancesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<InstalledProduct>>([Installed]));

        IFileLockDetector lockDetector = Substitute.For<IFileLockDetector>();
        lockDetector
            .GetLockedFiles(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LockedFileInfo>());

        IFeedReportService feedReportService = Substitute.For<IFeedReportService>();
        feedReportService
            .NotifyFeedChangedAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        EnvironmentVariableService envVarService = new EnvironmentVariableService(
            NullLogger<EnvironmentVariableService>.Instance,
            new FakeEnvironmentVariableStore()
        );
        IActivityLog activityLog = Substitute.For<IActivityLog>();
        LockDetector = lockDetector;
        UninstallService uninstallService = new UninstallService(
            Repository,
            activityLog,
            lockDetector,
            envVarService,
            new DeferredFileOps(),
            feedReportService,
            NullLogger<UninstallService>.Instance
        );

        uninstallService.ProgramsFolder = Path.Combine(Root, "startmenu");
        Uninstaller = uninstallService;
        Backups = new BackupService(BackupRoot);
        Engine = new InstallationEngine(
            feedRegistry,
            Repository,
            Backups,
            lockDetector,
            activityLog,
            Substitute.For<IConfigurationService>(),
            Substitute.For<IEncryptionService>(),
            [],
            envVarService,
            uninstallService,
            feedReportService,
            NullLogger<InstallationEngine>.Instance
        );
    }

    public string ProductId { get; }

    public string UniqueId { get; }

    public string Root { get; }

    public string InstallPath { get; }

    public string BackupRoot { get; }

    public InstalledProduct Installed { get; }

    public IRegistryClient Registry { get; }

    public IProductRepository Repository { get; }

    public BackupService Backups { get; }

    public IFileLockDetector LockDetector { get; }

    public UninstallService Uninstaller { get; }

    public InstallationEngine Engine { get; }

    public InstallOptions Options =>
        new InstallOptions(InstallPath, InstanceIdHelper.DefaultInstanceId, true, null, FeedId);

    public string InstalledFile(string relativePath) =>
        Path.Combine(InstallPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public string MetadataDir => StorkPaths.ProductMetadataDir(ProductId, UniqueId);

    public string FileManifestFile => StorkPaths.FileManifestPath(ProductId, UniqueId);

    public void WriteInstalledFile(string relativePath, string content)
    {
        string full = InstalledFile(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public async Task WriteFileManifestAsync(params string[] relativePaths)
    {
        Directory.CreateDirectory(StorkPaths.StorkConfigDir);
        List<TrackedFile> tracked = relativePaths.Select(p => new TrackedFile(p)).ToList();
        await FileManifestStore.WriteAsync(FileManifestFile, tracked);
    }

    public void WriteMetadataFile(string relativePath, string content)
    {
        string full = Path.Combine(MetadataDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void SetPackage(Dictionary<string, string> files)
    {
        _package = files;
    }

    public void AddPackageFile(string path, byte[] content)
    {
        _binaryPackage[path] = content;
    }

    public string CleanupMarkerPath => CleanupProbePlugin.MarkerPath(ProductId);

    public ProductManifest ManifestWithCleanupProbe(string version)
    {
        string assemblyPath = typeof(CleanupProbePlugin).Assembly.Location;
        AddPackageFile(
            "plugins/" + Path.GetFileName(assemblyPath),
            File.ReadAllBytes(assemblyPath)
        );
        return Manifest(version) with
        {
            Plugins =
            [
                new StorkPluginInfo(
                    "plugins/" + Path.GetFileName(assemblyPath),
                    typeof(CleanupProbePlugin).FullName!
                ),
            ],
        };
    }

    public void MakeFresh()
    {
        Repository
            .GetByIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<InstalledProduct?>(null));
        Repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<InstalledProduct>>([]));
        Repository
            .GetInstancesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<InstalledProduct>>([]));
    }

    public void RegisterForeignProductAt(string installedPath)
    {
        InstalledProduct foreign = new InstalledProduct(
            "foreign-product",
            InstanceIdHelper.DefaultInstanceId,
            "Foreign",
            "1.0.0",
            installedPath,
            DateTime.UtcNow
        );
        Repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<InstalledProduct>>([foreign]));
    }

    public ProductManifest Manifest(
        string version,
        string[]? excludeFiles = null,
        string[]? preserveOnSwitch = null
    ) =>
        new ProductManifest(
            ProductId,
            "Test product",
            version,
            new DateOnly(2026, 1, 1),
            InstallType.Executable,
            ExcludeFiles: excludeFiles,
            PreserveOnSwitch: preserveOnSwitch
        );

    public void Dispose()
    {
        TryDelete(() => Directory.Delete(Root, recursive: true));
        TryDelete(() => Directory.Delete(MetadataDir, recursive: true));
        TryDelete(() => File.Delete(FileManifestFile));
        TryDelete(() => File.Delete(CleanupMarkerPath));
        TryDelete(() => File.Delete(CleanupProbePlugin.InstancesMarkerPath(ProductId)));
        _isolation.Dispose();
        TryDelete(() => File.Delete(StorkPaths.InstancePluginConfigPath(ProductId, UniqueId)));
    }

    private Stream BuildPackage()
    {
        MemoryStream stream = new MemoryStream();
        using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (KeyValuePair<string, string> file in _package)
            {
                ZipArchiveEntry entry = archive.CreateEntry(file.Key);
                using StreamWriter writer = new StreamWriter(entry.Open());
                writer.Write(file.Value);
            }

            foreach (KeyValuePair<string, byte[]> file in _binaryPackage)
            {
                ZipArchiveEntry entry = archive.CreateEntry(file.Key);
                using Stream entryStream = entry.Open();
                entryStream.Write(file.Value);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
    }
}
