using System.IO.Compression;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

/// <summary>
/// Core engine responsible for product installation, update, and uninstall orchestration.
/// </summary>
public sealed class InstallationEngine : IInstallationEngine
{
    private readonly IFeedRegistry _feedRegistry;
    private readonly IProductRepository _productRepository;
    private readonly IBackupService _backupService;
    private readonly IFileLockDetector _fileLockDetector;
    private readonly IActivityLog _activityLog;
    private readonly IConfigurationService _configurationService;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<InstallationEngine> _logger;
    private readonly EnvironmentVariableService _envVarService;
    private readonly UninstallService _uninstallService;
    private readonly IFeedReportService _feedReportService;
    private readonly List<IFileTypeHandler> _fileTypeHandlers;
    private readonly PackageDownloader _downloader;
    private readonly Func<string, long?> _availableBytes;
    private const int MaxLockedFilesPrompts = 5;

    private readonly AsyncLocal<OperationState?> _operation = new AsyncLocal<OperationState?>();

    private sealed class OperationState
    {
        public IProgress<InstallProgress>? Progress { get; set; }
        public bool DeferRemainingLockedFiles { get; set; }
        public List<ProductPluginLoadContext> PluginContexts { get; } = [];
        public string? LastElevatedError { get; set; }
    }

    private sealed class OperationScope(Action? onDispose) : IDisposable
    {
        public void Dispose() => onDispose?.Invoke();
    }

    private OperationState Operation => _operation.Value ?? new OperationState();

    private OperationScope BeginOperation(IProgress<InstallProgress>? progress)
    {
        OperationState? existing = _operation.Value;
        if (existing is not null)
        {
            existing.Progress = progress ?? existing.Progress;
            return new OperationScope(null);
        }

        _operation.Value = new OperationState { Progress = progress };
        return new OperationScope(() => _operation.Value = null);
    }

    private static readonly TimeSpan RollbackPluginTimeout = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    /// <inheritdoc />
    public InstallPathResolverCallback? OnResolveInstallPath { get; set; }

    /// <inheritdoc />
    public FileHandlerConfigCallback? OnFileHandlerConfigNeeded { get; set; }

    /// <inheritdoc />
    public FileHandlerConfigCallback? OnPluginConfigNeeded { get; set; }

    public ActionGroupConfigCallback? OnActionGroupConfigNeeded { get; set; }
    public LockedFilesCallback? OnLockedFilesDetected { get; set; }
    public Func<PluginPrompt, PluginPromptResult>? OnPrompt { get; set; }
    public Func<InstancePickRequest, InstalledProduct?>? OnPickInstalledInstance { get; set; }
    public Func<string, object[], string>? OnLocalize { get; set; }

    private async Task<
        Func<string, IReadOnlyList<InstalledProduct>>
    > CreateInstalledInstancesLookupAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<InstalledProduct> installed = await _productRepository.GetAllAsync(
            cancellationToken
        );
        return productId =>
            installed
                .Where(p => string.Equals(p.ProductId, productId, StringComparison.Ordinal))
                .ToList();
    }

    public IInteractiveStorkPlugin? CurrentInteractivePlugin { get; private set; }

    private string Localize(string key, params object[] args) =>
        OnLocalize?.Invoke(key, args) ?? key;

    /// <summary>
    /// Guards against running a product whose plugins were built against a newer StorkDrop contract
    /// surface than this host provides. Returns a user-facing error when the manifest declares a
    /// <see cref="ProductManifest.MinHostVersion"/> the running host is below, otherwise null.
    /// Checked before any destructive step so a version mismatch aborts cleanly instead of failing
    /// deep inside a plugin with a MissingMethodException.
    /// </summary>
    private string? CheckMinHostVersion(ProductManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.MinHostVersion))
            return null;

        if (
            !StorkDrop.Contracts.PluginVersion.TryParse(
                manifest.MinHostVersion,
                out StorkDrop.Contracts.PluginVersion required
            )
            || !StorkDrop.Contracts.PluginVersion.TryParse(
                StorkDrop.Contracts.Services.HostVersion.Current,
                out StorkDrop.Contracts.PluginVersion current
            )
        )
            return null;

        if (current >= required)
            return null;

        _logger.LogError(
            "Host version {Host} is below the required {Required} for {ProductId} v{Version}",
            StorkDrop.Contracts.Services.HostVersion.Current,
            manifest.MinHostVersion,
            manifest.ProductId,
            manifest.Version
        );

        string localized = Localize(
            "HostVersion_TooOld",
            manifest.Title,
            manifest.Version,
            manifest.MinHostVersion,
            StorkDrop.Contracts.Services.HostVersion.Current
        );
        return localized == "HostVersion_TooOld"
            ? $"„{manifest.Title}“ {manifest.Version} benötigt StorkDrop ≥ {manifest.MinHostVersion} (installiert: {StorkDrop.Contracts.Services.HostVersion.Current}). Bitte StorkDrop zuerst aktualisieren."
            : localized;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationEngine"/> class.
    /// </summary>
    public InstallationEngine(
        IFeedRegistry feedRegistry,
        IProductRepository productRepository,
        IBackupService backupService,
        IFileLockDetector fileLockDetector,
        IActivityLog activityLog,
        IConfigurationService configurationService,
        IEncryptionService encryptionService,
        IEnumerable<IStorkDropPlugin> plugins,
        EnvironmentVariableService envVarService,
        UninstallService uninstallService,
        IFeedReportService feedReportService,
        ILogger<InstallationEngine> logger
    )
        : this(
            feedRegistry,
            productRepository,
            backupService,
            fileLockDetector,
            activityLog,
            configurationService,
            encryptionService,
            plugins,
            envVarService,
            uninstallService,
            feedReportService,
            logger,
            new PackageDownloader(logger)
        ) { }

    internal InstallationEngine(
        IFeedRegistry feedRegistry,
        IProductRepository productRepository,
        IBackupService backupService,
        IFileLockDetector fileLockDetector,
        IActivityLog activityLog,
        IConfigurationService configurationService,
        IEncryptionService encryptionService,
        IEnumerable<IStorkDropPlugin> plugins,
        EnvironmentVariableService envVarService,
        UninstallService uninstallService,
        IFeedReportService feedReportService,
        ILogger<InstallationEngine> logger,
        PackageDownloader downloader,
        Func<string, long?>? availableBytes = null
    )
    {
        _downloader = downloader;
        _availableBytes = availableBytes ?? DiskSpaceGuard.GetAvailableBytes;
        _feedRegistry = feedRegistry;
        _productRepository = productRepository;
        _backupService = backupService;
        _fileLockDetector = fileLockDetector;
        _activityLog = activityLog;
        _configurationService = configurationService;
        _encryptionService = encryptionService;
        _fileTypeHandlers = plugins.OfType<IFileTypeHandler>().ToList();
        _envVarService = envVarService;
        _uninstallService = uninstallService;
        _feedReportService = feedReportService;
        _logger = logger;

        _logger.LogInformation(
            "InstallationEngine initialized with {PluginCount} plugins, {HandlerCount} file type handlers",
            plugins.Count(),
            _fileTypeHandlers.Count
        );
    }

    private IRegistryClient GetClientForFeed(string? feedId) =>
        _feedRegistry.GetClient(string.IsNullOrEmpty(feedId) ? FirstFeedId() : feedId);

    private string FirstFeedId()
    {
        IReadOnlyList<FeedInfo> feeds = _feedRegistry.GetFeeds();
        if (feeds.Count == 0)
            throw new InvalidOperationException("No feeds configured.");
        return feeds[0].Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PluginConfigField>> GetPluginConfigurationAsync(
        ProductManifest manifest,
        string? feedId = null,
        CancellationToken cancellationToken = default
    )
    {
        if (manifest.Plugins is not { Length: > 0 })
        {
            _logger.LogDebug("No plugins defined for {ProductId}", manifest.ProductId);
            return Array.Empty<PluginConfigField>();
        }

        _logger.LogInformation(
            "Loading plugin configuration for {ProductId} ({PluginCount} plugins)",
            manifest.ProductId,
            manifest.Plugins.Length
        );

        PluginEnvironment environment = await BuildPluginEnvironmentAsync(
            manifest,
            cancellationToken
        );
        List<PluginConfigField> allFields = new List<PluginConfigField>();

        foreach (StorkPluginInfo pluginInfo in manifest.Plugins)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                IStorkPlugin? plugin = await DownloadAndLoadPluginAsync(
                    manifest,
                    pluginInfo,
                    cancellationToken,
                    feedId
                );
                if (plugin is null)
                {
                    await LogPluginResult(
                        manifest.ProductId,
                        $"Plugin {pluginInfo.TypeName} could not be loaded (null).",
                        false,
                        cancellationToken
                    );
                    continue;
                }

                IReadOnlyList<PluginConfigField> fields = plugin.GetConfigurationSchema(
                    environment
                );
                _logger.LogInformation(
                    "Plugin {TypeName} returned {FieldCount} config fields",
                    pluginInfo.TypeName,
                    fields.Count
                );
                allFields.AddRange(fields);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to load plugin config schema from {TypeName} for {ProductId}",
                    pluginInfo.TypeName,
                    manifest.ProductId
                );
                await LogPluginResult(
                    manifest.ProductId,
                    $"Failed to load config schema from {pluginInfo.TypeName}: {ex.Message}",
                    false,
                    cancellationToken
                );
            }
        }

        return allFields;
    }

    public async Task<IReadOnlyList<PluginActionGroup>> GetActionGroupsAsync(
        ProductManifest manifest,
        string? feedId = null,
        CancellationToken cancellationToken = default
    )
    {
        List<PluginActionGroup> groups = [];

        if (manifest.Plugins is not { Length: > 0 })
            return groups;

        PluginEnvironment environment = await BuildPluginEnvironmentAsync(
            manifest,
            cancellationToken
        );

        foreach (StorkPluginInfo pluginInfo in manifest.Plugins)
        {
            try
            {
                IStorkPlugin? plugin = await DownloadAndLoadPluginAsync(
                    manifest,
                    pluginInfo,
                    cancellationToken,
                    feedId
                );
                if (plugin is null)
                    continue;

                if (plugin is IInteractiveStorkPlugin interactive)
                    CurrentInteractivePlugin = interactive;

                IReadOnlyList<PluginConfigField> sharedFields;
                try
                {
                    sharedFields = plugin.GetConfigurationSchema(environment);
                }
                catch (Exception schemaEx)
                {
                    _logger.LogError(
                        schemaEx,
                        "GetConfigurationSchema failed for {TypeName}",
                        pluginInfo.TypeName
                    );
                    sharedFields = [];
                }

                _logger.LogInformation(
                    "GetConfigurationSchema for {TypeName} returned {Count} fields",
                    pluginInfo.TypeName,
                    sharedFields.Count
                );

                if (sharedFields.Count > 0)
                {
                    string cfgName = pluginInfo.TypeName.Contains('.')
                        ? pluginInfo.TypeName[(pluginInfo.TypeName.LastIndexOf('.') + 1)..]
                        : pluginInfo.TypeName;

                    groups.Add(
                        new PluginActionGroup
                        {
                            GroupId = $"config-{pluginInfo.TypeName}",
                            Title = $"Configuration: {cfgName}",
                            Phase = PluginActionPhase.PreInstall,
                            Fields = sharedFields,
                            Descriptions = [],
                        }
                    );
                }

                if (plugin is IDescribableStorkPlugin describable)
                {
                    IReadOnlyList<PluginActionDescription> descriptions =
                        describable.GetActionDescriptions(environment);

                    foreach (PluginActionDescription desc in descriptions)
                    {
                        groups.Add(
                            new PluginActionGroup
                            {
                                GroupId =
                                    $"{desc.Phase.ToString().ToLowerInvariant()}-{pluginInfo.TypeName}-{desc.Title}",
                                Title = desc.Title,
                                Phase = desc.Phase,
                                IsEnabled = desc.IsEnabled,
                                Fields = desc.Fields,
                                Descriptions = [desc],
                            }
                        );
                    }
                }
                else
                {
                    string shortName = pluginInfo.TypeName.Contains('.')
                        ? pluginInfo.TypeName[(pluginInfo.TypeName.LastIndexOf('.') + 1)..]
                        : pluginInfo.TypeName;

                    groups.Add(
                        new PluginActionGroup
                        {
                            GroupId = $"preinstall-{pluginInfo.TypeName}",
                            Title = $"PreInstall: {shortName}",
                            Phase = PluginActionPhase.PreInstall,
                            Fields = [],
                            Descriptions = [],
                        }
                    );

                    groups.Add(
                        new PluginActionGroup
                        {
                            GroupId = $"postinstall-{pluginInfo.TypeName}",
                            Title = $"PostInstall: {shortName}",
                            Phase = PluginActionPhase.PostInstall,
                            Fields = [],
                            Descriptions = [],
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to load action groups from {TypeName} for {ProductId}",
                    pluginInfo.TypeName,
                    manifest.ProductId
                );
            }
        }

        return groups;
    }

    private async Task<List<PluginActionGroup>> BuildActionGroupsFromExtractedAsync(
        ProductManifest manifest,
        string extractPath,
        CancellationToken cancellationToken
    )
    {
        List<PluginActionGroup> groups = [];

        if (manifest.Plugins is not { Length: > 0 })
            return groups;

        PluginEnvironment environment = await BuildPluginEnvironmentAsync(
            manifest,
            cancellationToken
        );

        foreach (StorkPluginInfo pluginInfo in manifest.Plugins)
        {
            try
            {
                IStorkPlugin? plugin = LoadPlugin(
                    extractPath,
                    pluginInfo,
                    Operation.PluginContexts
                );
                if (plugin is null)
                    continue;

                if (plugin is IInteractiveStorkPlugin interactive)
                    CurrentInteractivePlugin = interactive;

                IReadOnlyList<PluginConfigField> sharedFields;
                try
                {
                    sharedFields = plugin.GetConfigurationSchema(environment);
                }
                catch (Exception schemaEx)
                {
                    _logger.LogError(
                        schemaEx,
                        "GetConfigurationSchema failed for {TypeName}",
                        pluginInfo.TypeName
                    );
                    sharedFields = [];
                }

                _logger.LogInformation(
                    "GetConfigurationSchema for {TypeName} returned {Count} fields",
                    pluginInfo.TypeName,
                    sharedFields.Count
                );

                if (sharedFields.Count > 0)
                {
                    string cfgName = pluginInfo.TypeName.Contains('.')
                        ? pluginInfo.TypeName[(pluginInfo.TypeName.LastIndexOf('.') + 1)..]
                        : pluginInfo.TypeName;

                    groups.Add(
                        new PluginActionGroup
                        {
                            GroupId = $"config-{pluginInfo.TypeName}",
                            Title = $"Configuration: {cfgName}",
                            Phase = PluginActionPhase.PreInstall,
                            Fields = sharedFields,
                            Descriptions = [],
                        }
                    );
                }

                if (plugin is IDescribableStorkPlugin describable)
                {
                    IReadOnlyList<PluginActionDescription> descriptions =
                        describable.GetActionDescriptions(environment);

                    foreach (PluginActionDescription desc in descriptions)
                    {
                        groups.Add(
                            new PluginActionGroup
                            {
                                GroupId =
                                    $"{desc.Phase.ToString().ToLowerInvariant()}-{pluginInfo.TypeName}-{desc.Title}",
                                Title = desc.Title,
                                Phase = desc.Phase,
                                IsEnabled = desc.IsEnabled,
                                Fields = desc.Fields,
                                Descriptions = [desc],
                            }
                        );
                    }
                }
                else
                {
                    string shortName = pluginInfo.TypeName.Contains('.')
                        ? pluginInfo.TypeName[(pluginInfo.TypeName.LastIndexOf('.') + 1)..]
                        : pluginInfo.TypeName;

                    groups.Add(
                        new PluginActionGroup
                        {
                            GroupId = $"preinstall-{pluginInfo.TypeName}",
                            Title = $"PreInstall: {shortName}",
                            Phase = PluginActionPhase.PreInstall,
                            Fields = [],
                            Descriptions = [],
                        }
                    );

                    groups.Add(
                        new PluginActionGroup
                        {
                            GroupId = $"postinstall-{pluginInfo.TypeName}",
                            Title = $"PostInstall: {shortName}",
                            Phase = PluginActionPhase.PostInstall,
                            Fields = [],
                            Descriptions = [],
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to build action groups from {TypeName}",
                    pluginInfo.TypeName
                );
            }
        }

        return groups;
    }

    private void AddFileHandlerGroups(List<PluginActionGroup> groups, string filesDirectory)
    {
        if (_fileTypeHandlers.Count == 0 || !Directory.Exists(filesDirectory))
            return;

        string[] allFiles = Directory.GetFiles(filesDirectory);
        if (allFiles.Length == 0)
            return;

        foreach (IFileTypeHandler handler in _fileTypeHandlers)
        {
            try
            {
                List<string> matchingFiles = allFiles
                    .Where(f =>
                        handler.HandledExtensions.Any(ext =>
                            f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    .ToList();

                if (matchingFiles.Count == 0)
                    continue;

                PluginContext tempContext = new PluginContext
                {
                    StorkConfigDirectory = GetStorkConfigDir(),
                };

                IReadOnlyList<PluginConfigField> fields = handler.GetFileHandlerConfig(
                    matchingFiles,
                    tempContext
                );

                string handlerName = handler is IStorkDropPlugin sdp
                    ? sdp.DisplayName
                    : handler.GetType().Name;

                groups.Add(
                    new PluginActionGroup
                    {
                        GroupId = $"filehandler-{handlerName}",
                        Title = $"File Handler: {handlerName}",
                        Phase = PluginActionPhase.PreInstall,
                        Fields = fields,
                        Descriptions = [],
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to build file handler group for {Handler}",
                    handler.GetType().Name
                );
            }
        }
    }

    /// <inheritdoc />
    public async Task<InstallResult> InstallAsync(
        ProductManifest manifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    )
    {
        string? hostVersionError = CheckMinHostVersion(manifest);
        if (hostVersionError is not null)
            return new InstallResult
            {
                Success = false,
                ErrorMessage = hostVersionError,
                FailedStep = "HostVersion",
            };

        using ProductLockAcquisition operationLock = ProductOperationLock.TryAcquire(
            manifest.ProductId,
            options.InstanceId,
            "install"
        );
        if (!operationLock.Acquired)
            return new InstallResult
            {
                Success = false,
                ErrorMessage = operationLock.DescribeConflict(manifest.Title),
                FailedStep = "Coordination",
            };

        InstallResult result = await InstallInternalAsync(
            manifest,
            options,
            progress,
            cancellationToken
        );

        if (result.Success)
            await _feedReportService.NotifyFeedChangedAsync(options.FeedId, cancellationToken);

        return result;
    }

    private async Task<InstallResult> InstallInternalAsync(
        ProductManifest manifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    )
    {
        using OperationScope operationScope = BeginOperation(progress);
        string? resolvedPath = null;
        InstalledProduct? existingProduct = null;
        string instanceUniqueId = string.Empty;
        string? extractPath = null;
        bool preInstallPassed = false;
        bool filesCopyStarted = false;

        _logger.LogInformation(
            "Starting installation of {ProductId} v{Version} to {TargetPath}",
            manifest.ProductId,
            manifest.Version,
            options.TargetPath
        );

        existingProduct = await _productRepository.GetByIdAsync(
            manifest.ProductId,
            options.InstanceId,
            cancellationToken
        );

        // Priority: existing product record > config file (from elevation) > generate new
        string uniqueIdSource;
        if (existingProduct?.InstanceUniqueId is { Length: > 0 } uid)
        {
            instanceUniqueId = uid;
            uniqueIdSource = "reused";
        }
        else if (
            options.PluginConfigValues is not null
            && options.PluginConfigValues.TryGetValue("__instanceUniqueId", out string? passedId)
            && !string.IsNullOrEmpty(passedId)
        )
        {
            instanceUniqueId = passedId;
            uniqueIdSource = "from elevation config";
        }
        else if (
            !string.Equals(
                options.InstanceId,
                InstanceIdHelper.DefaultInstanceId,
                StringComparison.Ordinal
            )
        )
        {
            instanceUniqueId = InstanceIdHelper.Sanitize(options.InstanceId);
            uniqueIdSource = "from instance id";
        }
        else
        {
            instanceUniqueId = InstanceIdHelper.GenerateUniqueId();
            uniqueIdSource = "generated";
        }

        _logger.LogInformation(
            "InstanceUniqueId for {ProductId}/{InstanceId}: {UniqueId} ({Source})",
            manifest.ProductId,
            options.InstanceId,
            instanceUniqueId,
            uniqueIdSource
        );

        string tempDir = Path.Combine(StorkPaths.TempDir, Guid.NewGuid().ToString());

        try
        {
            Directory.CreateDirectory(tempDir);

            cancellationToken.ThrowIfCancellationRequested();
            string? productContentPath;
            InstallResult? downloadError;
            (extractPath, productContentPath, downloadError) = await DownloadAndExtractAsync(
                manifest,
                options,
                tempDir,
                progress,
                cancellationToken
            );
            if (downloadError is not null || extractPath is null)
                return downloadError
                    ?? new InstallResult
                    {
                        Success = false,
                        ErrorMessage = "Extraction failed.",
                        FailedStep = "Extracting",
                    };

            string copySourcePath = productContentPath ?? extractPath;

            InstallResult? diskSpaceError = await CheckTargetDiskSpaceAsync(
                manifest,
                options,
                existingProduct,
                copySourcePath,
                cancellationToken
            );
            if (diskSpaceError is not null)
                return diskSpaceError;

            cancellationToken.ThrowIfCancellationRequested();
            (
                HashSet<string> handledFiles,
                PluginContext? fileHandlerContext,
                InstallResult? fileHandlerError
            ) = await HandleFileTypesAsync(
                manifest,
                options,
                extractPath,
                instanceUniqueId,
                progress,
                cancellationToken
            );
            if (fileHandlerError is not null)
                return fileHandlerError;

            HashSet<string> disabledGroups = new HashSet<string>();
            if (OnActionGroupConfigNeeded is not null && options.PluginConfigValues is null)
            {
                List<PluginActionGroup> groups = await BuildActionGroupsFromExtractedAsync(
                    manifest,
                    extractPath,
                    cancellationToken
                );

                if (groups.Count > 0)
                {
                    Dictionary<string, string> previousValues = await LoadPluginConfigValues(
                        manifest.ProductId,
                        instanceUniqueId,
                        cancellationToken
                    );
                    Dictionary<string, string>? userValues = OnActionGroupConfigNeeded(
                        groups,
                        previousValues
                    );
                    if (userValues is null)
                    {
                        return new InstallResult
                        {
                            Success = false,
                            ErrorMessage = "Installation cancelled by user.",
                            FailedStep = "PluginConfig",
                        };
                    }

                    foreach (string key in userValues.Keys)
                    {
                        if (key.StartsWith("__group_enabled_") && userValues[key] == "false")
                            disabledGroups.Add(key["__group_enabled_".Length..]);
                    }

                    options = options with { PluginConfigValues = userValues };
                }
            }

            if (
                manifest.Plugins is { Length: > 0 }
                && options.PluginConfigValues is null
                && OnPluginConfigNeeded is not null
            )
            {
                List<PluginConfigField> allFields = [];
                CurrentInteractivePlugin = null;
                PluginEnvironment env = await BuildPluginEnvironmentAsync(
                    manifest,
                    cancellationToken,
                    options.InstanceId
                );

                foreach (StorkPluginInfo pluginInfo in manifest.Plugins)
                {
                    IStorkPlugin? plugin = LoadPlugin(
                        extractPath,
                        pluginInfo,
                        Operation.PluginContexts
                    );
                    if (plugin is null)
                    {
                        _logger.LogError("Could not load plugin {TypeName}", pluginInfo.TypeName);
                        return new InstallResult
                        {
                            Success = false,
                            ErrorMessage = $"Plugin {pluginInfo.TypeName} could not be loaded.",
                            FailedStep = "PluginConfig",
                        };
                    }

                    if (
                        plugin is IInteractiveStorkPlugin interactive
                        && CurrentInteractivePlugin is null
                    )
                        CurrentInteractivePlugin = interactive;

                    IReadOnlyList<PluginConfigField> fields = plugin.GetConfigurationSchema(env);
                    allFields.AddRange(fields);
                }

                if (allFields.Count > 0)
                {
                    Dictionary<string, string>? userValues = OnPluginConfigNeeded(
                        allFields,
                        new Dictionary<string, string>()
                    );
                    if (userValues is null)
                    {
                        return new InstallResult
                        {
                            Success = false,
                            ErrorMessage = "Installation cancelled by user.",
                            FailedStep = "PluginConfig",
                        };
                    }
                    options = options with { PluginConfigValues = userValues };
                }
            }

            InstallResult? elevationResult = await TryElevateInstallAsync(
                manifest,
                options,
                options.TargetPath,
                instanceUniqueId,
                progress,
                cancellationToken
            );
            if (elevationResult is not null)
                return elevationResult;

            cancellationToken.ThrowIfCancellationRequested();
            PluginContext pluginContext = await BuildPluginContextAsync(
                manifest,
                options,
                instanceUniqueId,
                cancellationToken
            );

            bool skipPreInstall =
                manifest.Plugins is { Length: > 0 }
                && manifest.Plugins.All(p => disabledGroups.Contains($"preinstall-{p.TypeName}"));
            bool skipPostInstall =
                manifest.Plugins is { Length: > 0 }
                && manifest.Plugins.All(p => disabledGroups.Contains($"postinstall-{p.TypeName}"));

            PluginPhaseResult preInstallResult = skipPreInstall
                ? new PluginPhaseResult { Success = true }
                : await RunPluginPhaseAsync(
                    manifest,
                    options,
                    pluginContext,
                    PluginPhase.PreInstall,
                    cancellationToken,
                    extractPath
                );
            if (!preInstallResult.Success)
            {
                _logger.LogError(
                    "Installation failed at step PreInstall: {Error}",
                    preInstallResult.ErrorMessage
                );
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        0,
                        $"Installation failed at step PreInstall: {preInstallResult.ErrorMessage}"
                    )
                );
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = preInstallResult.ErrorMessage,
                    FailedStep = "PreInstall",
                };
            }

            preInstallPassed = true;

            // Update only: now that PreInstall succeeded (and stopped the product's own services),
            // remove the previous install's files. Doing this after PreInstall means a failed PreInstall
            // leaves the existing install untouched, and the services are no longer holding their files.
            bool removePreviousFiles =
                options.RemovePreviousFiles
                || (
                    options.PluginConfigValues is not null
                    && options.PluginConfigValues.TryGetValue(
                        "__removePreviousFiles",
                        out string? removePreviousFlag
                    )
                    && removePreviousFlag == "true"
                );
            if (removePreviousFiles && existingProduct is not null)
            {
                await RemoveOldTrackedFilesAsync(
                    existingProduct,
                    manifest,
                    progress,
                    cancellationToken
                );
            }

            InstallResult? pathError;
            (resolvedPath, pathError) = await ResolveAndValidateTargetPathAsync(
                manifest,
                options,
                fileHandlerContext,
                pluginContext,
                extractPath,
                instanceUniqueId,
                progress,
                cancellationToken
            );
            if (pathError is not null)
            {
                if (existingProduct is null && !pathError.Success)
                    await CleanUpFailedFreshInstallAsync(
                        manifest,
                        options,
                        instanceUniqueId,
                        resolvedPath ?? options.TargetPath,
                        extractPath,
                        runPostUninstall: true,
                        removeInstallDir: false
                    );
                return pathError;
            }

            pluginContext.InstallPath = resolvedPath;

            cancellationToken.ThrowIfCancellationRequested();

            // Exclude plugins/ directory from the main product copy (they go to .stork/)
            string pluginsSourceDir = Path.Combine(copySourcePath, "plugins");
            if (Directory.Exists(pluginsSourceDir))
            {
                foreach (
                    string pf in Directory.GetFiles(
                        pluginsSourceDir,
                        "*",
                        SearchOption.AllDirectories
                    )
                )
                    handledFiles.Add(pf);
            }

            List<string> excludedFiles = [];
            if (manifest.ExcludeFiles is { Length: > 0 })
            {
                foreach (string pattern in manifest.ExcludeFiles)
                {
                    foreach (
                        string file in Directory.GetFiles(
                            copySourcePath,
                            pattern,
                            SearchOption.AllDirectories
                        )
                    )
                    {
                        if (handledFiles.Add(file))
                            excludedFiles.Add(Path.GetFileName(file));
                    }
                }

                if (excludedFiles.Count > 0)
                {
                    _logger.LogInformation(
                        "Excluded {Count} file(s) from copy: {Files}",
                        excludedFiles.Count,
                        string.Join("; ", excludedFiles)
                    );
                }
            }

            filesCopyStarted = true;
            InstallResult? copyError = await CopyFilesToTargetAsync(
                copySourcePath,
                resolvedPath,
                handledFiles,
                progress,
                cancellationToken
            );
            if (copyError is not null)
            {
                if (existingProduct is null)
                    await CleanUpFailedFreshInstallAsync(
                        manifest,
                        options,
                        instanceUniqueId,
                        resolvedPath,
                        extractPath,
                        runPostUninstall: true,
                        removeInstallDir: true
                    );
                return copyError;
            }

            // Unload plugin contexts from PreInstall phase before copying to .stork/
            // (the DLLs may already exist from a previous install and be locked)
            foreach (ProductPluginLoadContext ctx in Operation.PluginContexts)
            {
                try
                {
                    ctx.Unload();
                }
                catch { }
            }
            Operation.PluginContexts.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Copy plugin directory to .stork/plugins/ and save manifest for uninstall
            string pluginsExtractDir = Path.Combine(extractPath, "plugins");
            if (Directory.Exists(pluginsExtractDir))
            {
                string storkPluginsDir = StorkPaths.ProductPluginsDir(
                    manifest.ProductId,
                    instanceUniqueId
                );
                _logger.LogInformation("Copying product plugins to {Dir}", storkPluginsDir);
                ReportProgress(InstallStage.Installing, 0, "Copying product plugins...");
                FileOperations pluginCopyOps = new();
                foreach (
                    string file in Directory.GetFiles(
                        pluginsExtractDir,
                        "*",
                        SearchOption.AllDirectories
                    )
                )
                {
                    string relativePath = Path.GetRelativePath(pluginsExtractDir, file);
                    string targetFile = Path.Combine(storkPluginsDir, relativePath);
                    string? targetDir = Path.GetDirectoryName(targetFile);
                    if (targetDir is not null)
                        Directory.CreateDirectory(targetDir);

                    try
                    {
                        await pluginCopyOps.CopyFileAsync(file, targetFile, cancellationToken);
                    }
                    catch (IOException)
                    {
                        if (File.Exists(targetFile))
                        {
                            string delName =
                                $"DEL_{Guid.NewGuid():N}_{Path.GetFileName(targetFile)}";
                            string delPath = Path.Combine(
                                Path.GetDirectoryName(targetFile)!,
                                delName
                            );
                            File.Move(targetFile, delPath);
                            new DeferredFileOps().ScheduleDeleteOnReboot(delPath);
                            await pluginCopyOps.CopyFileAsync(file, targetFile, cancellationToken);
                            _logger.LogWarning(
                                "Plugin file {File} was locked, deferred old version for reboot delete",
                                Path.GetFileName(targetFile)
                            );
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            }

            if (handledFiles.Count > 0)
            {
                string storkFilesDir = StorkPaths.ProductHandledFilesDir(
                    manifest.ProductId,
                    instanceUniqueId
                );
                Directory.CreateDirectory(storkFilesDir);
                foreach (string handledFile in handledFiles)
                {
                    if (File.Exists(handledFile))
                    {
                        string destPath = Path.Combine(
                            storkFilesDir,
                            Path.GetFileName(handledFile)
                        );
                        try
                        {
                            File.Copy(handledFile, destPath, overwrite: true);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Failed to store handled file {File} to .stork/files/",
                                handledFile
                            );
                        }
                    }
                }
            }

            if (manifest.Plugins is { Length: > 0 })
            {
                string storkDir = StorkPaths.ProductMetadataDir(
                    manifest.ProductId,
                    instanceUniqueId
                );
                Directory.CreateDirectory(storkDir);
                string manifestJson = System.Text.Json.JsonSerializer.Serialize(
                    manifest,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                );
                await SafeFileWriter.WriteAtomicAsync(
                    Path.Combine(storkDir, "manifest.json"),
                    manifestJson,
                    cancellationToken
                );
                _logger.LogDebug("Saved product manifest to {Dir}", storkDir);
            }

            cancellationToken.ThrowIfCancellationRequested();
            InstallResult? verifyError = await VerifyInstallationAsync(
                manifest,
                resolvedPath,
                copySourcePath,
                handledFiles,
                progress
            );
            if (verifyError is not null)
            {
                if (existingProduct is null)
                    await CleanUpFailedFreshInstallAsync(
                        manifest,
                        options,
                        instanceUniqueId,
                        resolvedPath,
                        extractPath,
                        runPostUninstall: true,
                        removeInstallDir: true
                    );
                return verifyError;
            }

            await FinalizeInstallationAsync(
                manifest,
                options,
                resolvedPath,
                pluginContext,
                extractPath,
                copySourcePath,
                handledFiles,
                instanceUniqueId,
                progress,
                cancellationToken,
                skipPostInstall
            );

            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    100,
                    $"Installation of {manifest.Title} v{manifest.Version} completed successfully"
                )
            );
            _logger.LogInformation(
                "Installation of {ProductId} v{Version} completed successfully",
                manifest.ProductId,
                manifest.Version
            );
            return new InstallResult { Success = true };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Installation of {ProductId} cancelled by user", manifest.ProductId);
            if (existingProduct is null && preInstallPassed)
            {
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        0,
                        "Installation cancelled. Cleaning up..."
                    )
                );
                await CleanUpFailedFreshInstallAsync(
                    manifest,
                    options,
                    instanceUniqueId,
                    resolvedPath ?? options.TargetPath,
                    extractPath,
                    runPostUninstall: true,
                    removeInstallDir: filesCopyStarted
                );
            }
            else
            {
                progress.Report(
                    new InstallProgress(InstallStage.Installing, 0, "Installation cancelled.")
                );
            }

            return new InstallResult
            {
                Success = false,
                ErrorMessage = "Installation cancelled by user.",
                FailedStep = "Cancelled",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Installation failed for {ProductId}: {Error}",
                manifest.ProductId,
                ex.Message
            );

            bool freshInstall = existingProduct is null;
            string installPath = resolvedPath ?? options.TargetPath;

            // Fresh install failed late (e.g. a service that started too slowly): let the user keep the
            // partial install or remove it, instead of silently leaving files behind while deleting the
            // services and not listing the product. Updates go through UpdateAsync's rollback path.
            if (freshInstall && !ShouldRemoveAfterInstallFailure(manifest, ex))
            {
                _logger.LogWarning(
                    "Install of {ProductId} failed but was kept on user request.",
                    manifest.ProductId
                );
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        0,
                        $"Installation failed but the product was kept: {ex.Message}"
                    )
                );
                await RegisterElevatedInstallAsync(
                    manifest,
                    installPath,
                    options.FeedId,
                    "",
                    cancellationToken,
                    options.InstanceId,
                    instanceUniqueId
                );
                return new InstallResult { Success = true };
            }

            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    0,
                    freshInstall
                        ? $"Installation failed: {ex.Message}. Cleaning up..."
                        : $"Installation failed: {ex.Message}"
                )
            );

            if (freshInstall)
                await CleanUpFailedFreshInstallAsync(
                    manifest,
                    options,
                    instanceUniqueId,
                    installPath,
                    extractPath,
                    runPostUninstall: true,
                    removeInstallDir: filesCopyStarted
                );

            return new InstallResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FailedStep = "Unknown",
                Exception = ex,
            };
        }
        finally
        {
            CleanupTempDirectory(tempDir, progress);
        }
    }

    private async Task CleanUpFailedFreshInstallAsync(
        ProductManifest manifest,
        InstallOptions options,
        string instanceUniqueId,
        string installPath,
        string? extractPath,
        bool runPostUninstall,
        bool removeInstallDir
    )
    {
        if (runPostUninstall && manifest.Plugins is { Length: > 0 })
        {
            using CancellationTokenSource cleanupTimeout = new(RollbackPluginTimeout);
            try
            {
                ReportProgress(
                    InstallStage.Installing,
                    0,
                    "Cleaning up database entries after failed install..."
                );
                PluginContext cleanupContext = await BuildPluginContextAsync(
                    manifest,
                    options,
                    instanceUniqueId,
                    cleanupTimeout.Token
                );
                cleanupContext.InstallPath = installPath;
                await RunPluginPhaseAsync(
                    manifest,
                    options,
                    cleanupContext,
                    PluginPhase.PostUninstall,
                    cleanupTimeout.Token,
                    extractPath
                );
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "PostUninstall cleanup failed");
            }
        }

        if (removeInstallDir)
            TryRemoveInstallDir(installPath);
    }

    private async Task<InstallResult?> TryElevateInstallAsync(
        ProductManifest manifest,
        InstallOptions options,
        string targetPath,
        string instanceUniqueId,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        if (targetPath.Contains('{'))
        {
            _logger.LogInformation(
                "Target path contains template variables, deferring elevation check"
            );
            return null;
        }

        if (!ElevationHelper.PathRequiresAdmin(targetPath) || ElevationHelper.IsRunningAsAdmin())
            return null;

        _logger.LogInformation(
            "Elevation required, spawning elevated process for {ProductId}",
            manifest.ProductId
        );

        string? configFilePath = null;
        try
        {
            // Always write config file when elevating - at minimum we need to pass the UniqueId
            Dictionary<string, string> elevationConfig = options.PluginConfigValues
                is { Count: > 0 }
                ? new Dictionary<string, string>(options.PluginConfigValues)
                : new Dictionary<string, string>();
            elevationConfig["__instanceUniqueId"] = instanceUniqueId;
            if (options.RemovePreviousFiles)
                elevationConfig["__removePreviousFiles"] = "true";

            configFilePath = await ElevationConfigFile.WriteAsync(
                elevationConfig,
                cancellationToken
            );
            _logger.LogDebug(
                "Saved elevation config to {Path} (uniqueId={UniqueId})",
                configFilePath,
                instanceUniqueId
            );

            Operation.LastElevatedError = null;
            ReportProgress(InstallStage.Installing, 0, Localize("Elevation_Starting"));
            ElevationResult elevation = ElevationHelper.RunElevatedInstall(
                manifest.ProductId,
                manifest.Version,
                targetPath,
                options.FeedId ?? FirstFeedId(),
                options.InstanceId,
                configFilePath,
                onProgressLine: ReportElevatedProgressLine,
                onLongRunning: HandleElevatedLongRunning,
                cancellationToken: cancellationToken
            );

            if (elevation != ElevationResult.Succeeded)
            {
                string message = ElevationFailureMessage(
                    elevation,
                    "Installation cancelled: administrator rights were denied."
                );
                if (elevation == ElevationResult.DeniedByUser)
                    _logger.LogWarning(
                        "Elevation denied by user for {ProductId}",
                        manifest.ProductId
                    );
                else
                    _logger.LogError(
                        "Elevated install failed for {ProductId}: {Message}",
                        manifest.ProductId,
                        message
                    );
                progress.Report(new InstallProgress(InstallStage.Installing, 0, message));
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = message,
                    FailedStep =
                        elevation == ElevationResult.DeniedByUser ? "Elevation" : "ElevatedInstall",
                };
            }

            await RegisterElevatedInstallAsync(
                manifest,
                targetPath,
                options.FeedId,
                "(elevated)",
                cancellationToken,
                options.InstanceId,
                instanceUniqueId
            );

            if (options.PluginConfigValues is { Count: > 0 })
            {
                await SavePluginConfigValues(
                    manifest.ProductId,
                    instanceUniqueId,
                    options.PluginConfigValues,
                    cancellationToken
                );
            }

            return new InstallResult { Success = true };
        }
        finally
        {
            if (configFilePath is not null)
            {
                try
                {
                    File.Delete(configFilePath);
                }
                catch { }
            }
        }
    }

    private async Task RegisterElevatedInstallAsync(
        ProductManifest manifest,
        string installPath,
        string? feedId,
        string suffix,
        CancellationToken cancellationToken,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        string? instanceUniqueId = null
    )
    {
        InstalledProduct product = new InstalledProduct(
            ProductId: manifest.ProductId,
            InstanceId: instanceId,
            Title: manifest.Title,
            Version: manifest.Version,
            InstalledPath: installPath,
            InstalledDate: DateTime.UtcNow,
            FeedId: feedId,
            BackupPath: null,
            InstallType: manifest.InstallType,
            BadgeText: manifest.BadgeText,
            BadgeColor: manifest.BadgeColor,
            InstanceUniqueId: instanceUniqueId
        );
        await _productRepository.AddAsync(product, cancellationToken);

        await _activityLog.LogAsync(
            new ActivityLogEntry(
                Id: Guid.NewGuid().ToString(),
                Timestamp: DateTime.UtcNow,
                Action: "Install",
                ProductId: manifest.ProductId,
                Details: $"{manifest.Title} v{manifest.Version} installed to {installPath} {suffix}",
                Success: true
            ),
            cancellationToken
        );
    }

    private async Task<(
        string? extractPath,
        string? productContentPath,
        InstallResult? error
    )> DownloadAndExtractAsync(
        ProductManifest manifest,
        InstallOptions options,
        string tempDir,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        string description = $"{manifest.Title} v{manifest.Version}";
        _logger.LogInformation(
            "Downloading product {ProductId} v{Version}",
            manifest.ProductId,
            manifest.Version
        );
        progress.Report(
            new InstallProgress(InstallStage.Downloading, 0, $"Downloading {description}...")
        );

        IRegistryClient registryClient = GetClientForFeed(options.FeedId);
        string zipPath = Path.Combine(tempDir, $"{manifest.ProductId}-{manifest.Version}.zip");
        int lastPercent = -1;
        try
        {
            await _downloader.DownloadAsync(
                token =>
                    registryClient.DownloadProductAsync(
                        manifest.ProductId,
                        manifest.Version,
                        token
                    ),
                zipPath,
                description,
                manifest.ContentSha256,
                manifest.DownloadSizeBytes,
                (read, total) =>
                {
                    int percent = total is > 0 ? (int)Math.Min(99, read * 100 / total.Value) : 0;
                    if (percent == lastPercent)
                        return;

                    lastPercent = percent;
                    string size = total is > 0
                        ? $"{FormatHelper.FormatBytes(read)} of {FormatHelper.FormatBytes(total.Value)}"
                        : FormatHelper.FormatBytes(read);
                    progress.Report(
                        new InstallProgress(
                            InstallStage.Downloading,
                            percent,
                            $"Downloading {description}... {size}"
                        )
                    );
                },
                cancellationToken
            );
        }
        catch (PackageIntegrityException ex)
        {
            return StepFailure(progress, InstallStage.Downloading, "PackageVerification", ex);
        }
        catch (InsufficientDiskSpaceException ex)
        {
            return StepFailure(progress, InstallStage.Downloading, "DiskSpace", ex);
        }
        catch (PackageDownloadException ex)
        {
            return StepFailure(progress, InstallStage.Downloading, "Downloading", ex);
        }

        progress.Report(new InstallProgress(InstallStage.Downloading, 100, "Download complete."));
        _logger.LogInformation(
            "Download complete for {ProductId} v{Version}",
            manifest.ProductId,
            manifest.Version
        );

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InstallProgress(InstallStage.Extracting, 0, "Extracting files..."));
        string extractPath = Path.Combine(tempDir, "extracted");

        try
        {
            DiskSpaceGuard.EnsureFreeSpace(
                tempDir,
                DiskSpaceGuard.UncompressedSize(zipPath),
                $"extracting {description}",
                _availableBytes
            );
            ZipFile.ExtractToDirectory(zipPath, extractPath);
            string? productContentPath = ExtractInnerZipIfPresent(tempDir, extractPath, progress);

            progress.Report(
                new InstallProgress(InstallStage.Extracting, 100, "Extraction complete.")
            );
            _logger.LogInformation("Extraction complete for {ProductId}", manifest.ProductId);

            // If two-layer: extractPath has loose files (plugins, SIDs), productContentPath has product binaries
            // If single-layer: extractPath has everything, productContentPath is null
            return (extractPath, productContentPath, null);
        }
        catch (InsufficientDiskSpaceException ex)
        {
            return StepFailure(progress, InstallStage.Extracting, "DiskSpace", ex);
        }
        catch (Exception ex)
        {
            return StepFailure(
                progress,
                InstallStage.Extracting,
                "Extracting",
                ex,
                $"Extraction failed: {ex.Message}"
            );
        }
    }

    private async Task<InstallResult?> CheckTargetDiskSpaceAsync(
        ProductManifest manifest,
        InstallOptions options,
        InstalledProduct? existingProduct,
        string contentPath,
        CancellationToken cancellationToken
    )
    {
        if (options.TargetPath.Contains('{'))
            return null;

        long required = new DirectoryInfo(contentPath)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(file => file.Length);

        if (existingProduct is not null && !manifest.SharedInstallLocation)
        {
            List<string>? tracked = await LoadFileManifest(
                existingProduct.ProductId,
                existingProduct.InstanceUniqueId ?? string.Empty,
                cancellationToken
            );
            long replaced = (tracked ?? [])
                .Select(relative => new FileInfo(
                    Path.Combine(existingProduct.InstalledPath, relative)
                ))
                .Where(file => file.Exists)
                .Sum(file => file.Length);
            required = Math.Max(0, required - replaced);
        }

        try
        {
            DiskSpaceGuard.EnsureFreeSpace(
                options.TargetPath,
                required,
                $"installing {manifest.Title} v{manifest.Version}",
                _availableBytes
            );
            return null;
        }
        catch (InsufficientDiskSpaceException ex)
        {
            _logger.LogError(
                ex,
                "Not enough disk space to install {ProductId}",
                manifest.ProductId
            );
            ReportProgress(InstallStage.Installing, 0, ex.Message);
            return new InstallResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FailedStep = "DiskSpace",
                Exception = ex,
            };
        }
    }

    private (string? extractPath, string? productContentPath, InstallResult? error) StepFailure(
        IProgress<InstallProgress> progress,
        InstallStage stage,
        string failedStep,
        Exception exception,
        string? message = null
    )
    {
        string errorMessage = message ?? exception.Message;
        _logger.LogError(
            exception,
            "Installation failed at step {Step}: {Error}",
            failedStep,
            errorMessage
        );
        progress.Report(new InstallProgress(stage, 0, errorMessage));
        return (
            null,
            null,
            new InstallResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                FailedStep = failedStep,
                Exception = exception,
            }
        );
    }

    private string? ExtractInnerZipIfPresent(
        string tempDir,
        string extractPath,
        IProgress<InstallProgress> progress
    )
    {
        string[] innerZips = Directory.GetFiles(extractPath, "*.zip");
        if (innerZips.Length != 1)
            return null;

        string innerZipPath = innerZips[0];
        string productContentPath = Path.Combine(tempDir, "product-contents");
        _logger.LogInformation(
            "Found inner ZIP {InnerZip}, extracting product contents",
            Path.GetFileName(innerZipPath)
        );
        progress.Report(
            new InstallProgress(
                InstallStage.Extracting,
                50,
                $"Extracting inner archive {Path.GetFileName(innerZipPath)}..."
            )
        );
        DiskSpaceGuard.EnsureFreeSpace(
            tempDir,
            DiskSpaceGuard.UncompressedSize(innerZipPath),
            "extracting the inner archive",
            _availableBytes
        );
        ZipFile.ExtractToDirectory(innerZipPath, productContentPath);
        File.Delete(innerZipPath);

        _logger.LogInformation("Inner ZIP extracted to separate directory");
        return productContentPath;
    }

    private async Task<(
        HashSet<string> handledFiles,
        PluginContext? fileHandlerContext,
        InstallResult? error
    )> HandleFileTypesAsync(
        ProductManifest manifest,
        InstallOptions options,
        string extractPath,
        string instanceUniqueId,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken,
        HashSet<string>? disabledGroups = null
    )
    {
        HashSet<string> handledFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PluginContext? fileHandlerContext = null;

        _logger.LogDebug(
            "SkipFileHandlers={Skip}, FileTypeHandlers={Count}",
            options.SkipFileHandlers,
            _fileTypeHandlers.Count
        );

        if (options.SkipFileHandlers && _fileTypeHandlers.Count > 0)
        {
            CollectHandledFiles(extractPath, handledFiles);
            _logger.LogInformation(
                "Elevated install: excluded {Count} plugin-handled files from copy",
                handledFiles.Count
            );
            return (handledFiles, null, null);
        }

        if (_fileTypeHandlers.Count == 0 || options.SkipFileHandlers)
            return (handledFiles, null, null);

        fileHandlerContext = await BuildPluginContextAsync(
            manifest,
            options,
            instanceUniqueId,
            cancellationToken
        );

        foreach (IFileTypeHandler handler in _fileTypeHandlers)
        {
            List<string> matchingFiles = FindMatchingFiles(extractPath, handler.HandledExtensions);
            if (matchingFiles.Count == 0)
                continue;

            string fileList = string.Join(", ", matchingFiles.Select(Path.GetFileName));
            _logger.LogInformation(
                "File handler claims {Count} files: {Files}",
                matchingFiles.Count,
                fileList
            );
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    0,
                    $"Plugin found {matchingFiles.Count} file(s): {fileList}"
                )
            );

            IReadOnlyList<PluginConfigField> handlerFields = handler.GetFileHandlerConfig(
                matchingFiles,
                fileHandlerContext
            );
            if (handlerFields.Count > 0 && OnFileHandlerConfigNeeded is not null)
            {
                progress.Report(
                    new InstallProgress(
                        InstallStage.RunningPlugins,
                        0,
                        "Waiting for user configuration..."
                    )
                );
                Dictionary<string, string>? userValues = OnFileHandlerConfigNeeded(
                    handlerFields,
                    fileHandlerContext.ConfigValues
                );

                if (userValues is null)
                {
                    _logger.LogInformation(
                        "User cancelled file handler config dialog, aborting installation"
                    );
                    return (
                        handledFiles,
                        fileHandlerContext,
                        new InstallResult
                        {
                            Success = false,
                            ErrorMessage = "Installation cancelled by user.",
                            FailedStep = "FileHandlerConfig",
                        }
                    );
                }

                foreach (KeyValuePair<string, string> kv in userValues)
                {
                    _logger.LogDebug("File handler config: {Key} = {Value}", kv.Key, kv.Value);
                    fileHandlerContext.ConfigValues[kv.Key] = kv.Value;
                }
            }

            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    0,
                    "Processing files with plugin..."
                )
            );
            FileHandlerResult handlerResult = await handler.HandleFilesAsync(
                matchingFiles,
                fileHandlerContext,
                cancellationToken
            );

            foreach (FileHandlerFileResult fr in handlerResult.FileResults)
            {
                string msg = fr.Success
                    ? $"{Path.GetFileName(fr.FilePath)}: {fr.Action}"
                    : $"{Path.GetFileName(fr.FilePath)}: FAILED - {fr.ErrorMessage}";
                progress.Report(new InstallProgress(InstallStage.RunningPlugins, 0, msg));
            }

            if (!handlerResult.Success)
            {
                List<string> failedDetails = handlerResult
                    .FileResults.Where(fr => !fr.Success)
                    .Select(fr => $"{Path.GetFileName(fr.FilePath)}: {fr.ErrorMessage}")
                    .ToList();

                string detailedError =
                    failedDetails.Count > 0
                        ? string.Join("\n", failedDetails)
                        : handlerResult.ErrorMessage ?? "File handler failed";

                _logger.LogError(
                    "File handler failed, aborting installation: {Error}",
                    detailedError
                );
                return (
                    handledFiles,
                    fileHandlerContext,
                    new InstallResult
                    {
                        Success = false,
                        ErrorMessage = detailedError,
                        FailedStep = "FileHandler",
                    }
                );
            }

            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    0,
                    "Plugin processing completed successfully"
                )
            );
            foreach (string handled in matchingFiles)
                handledFiles.Add(handled);
        }

        return (handledFiles, fileHandlerContext, null);
    }

    private void CollectHandledFiles(string extractPath, HashSet<string> handledFiles)
    {
        foreach (IFileTypeHandler handler in _fileTypeHandlers)
        foreach (string file in FindMatchingFiles(extractPath, handler.HandledExtensions))
            handledFiles.Add(file);
    }

    private static List<string> FindMatchingFiles(
        string directory,
        IReadOnlyList<string> extensions
    )
    {
        List<string> files = new List<string>();
        foreach (string ext in extensions)
            files.AddRange(Directory.GetFiles(directory, "*" + ext, SearchOption.AllDirectories));
        return files;
    }

    private async Task<(
        string resolvedPath,
        InstallResult? error
    )> ResolveAndValidateTargetPathAsync(
        ProductManifest manifest,
        InstallOptions options,
        PluginContext? fileHandlerContext,
        PluginContext productContext,
        string extractPath,
        string instanceUniqueId,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        string resolvedTargetPath = options.TargetPath.Replace(
            "{StorkPath}",
            AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            )
        );
        if (resolvedTargetPath != options.TargetPath)
        {
            _logger.LogInformation(
                "Resolved {{StorkPath}} in install path: {Resolved}",
                resolvedTargetPath
            );
        }

        if (resolvedTargetPath.Contains("{InstanceId}"))
        {
            resolvedTargetPath = resolvedTargetPath.Replace("{InstanceId}", instanceUniqueId);
            _logger.LogInformation(
                "Resolved {{InstanceId}} in install path to UniqueId: {Resolved}",
                resolvedTargetPath
            );
        }

        // Safety net for non-interactive paths (CLI): the UI resolves {ProductPath:<id>} up front by
        // asking the user which instance to target, but if an unresolved token reaches here, anchor it
        // to the single installed instance or fail cleanly rather than copying into a literal path.
        string? referencedProductId = ProductPathToken.GetReferencedProductId(resolvedTargetPath);
        if (referencedProductId is not null)
        {
            IReadOnlyList<InstalledProduct> refInstances =
                await _productRepository.GetInstancesAsync(referencedProductId, cancellationToken);
            if (refInstances.Count != 1)
            {
                string reason =
                    refInstances.Count == 0
                        ? $"no instance of '{referencedProductId}' is installed"
                        : $"multiple instances of '{referencedProductId}' are installed; select one before installing";
                return (
                    resolvedTargetPath,
                    new InstallResult
                    {
                        Success = false,
                        ErrorMessage = $"Cannot resolve install path: {reason}.",
                        FailedStep = "ResolveInstallPath",
                    }
                );
            }
            resolvedTargetPath = ProductPathToken.Resolve(
                resolvedTargetPath,
                refInstances[0].InstalledPath
            );
            _logger.LogInformation(
                "Resolved ProductPath token for {ProductId} in install path: {Resolved}",
                referencedProductId,
                resolvedTargetPath
            );
        }

        // The installing product's own resolvers run first, with the product's config - so a product
        // resolves the tokens in its own install path from its own selection, not from another plugin's
        // (e.g. the SID dropdown) or a first-entry fallback. The host-wide chain below is only a fallback.
        if (resolvedTargetPath.Contains('{') && manifest.Plugins is { Length: > 0 })
        {
            List<ProductPluginLoadContext> resolverContexts = [];
            try
            {
                List<IInstallPathResolver> productResolvers = [];
                foreach (StorkPluginInfo pluginInfo in manifest.Plugins)
                {
                    try
                    {
                        if (
                            LoadPlugin(extractPath, pluginInfo, resolverContexts)
                            is IInstallPathResolver resolver
                        )
                            productResolvers.Add(resolver);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Could not load {TypeName} as an install-path resolver",
                            pluginInfo.TypeName
                        );
                    }
                }

                string beforeProduct = resolvedTargetPath;
                resolvedTargetPath = InstallPathResolution.Apply(
                    resolvedTargetPath,
                    productResolvers,
                    productContext
                );
                if (resolvedTargetPath != beforeProduct)
                    _logger.LogInformation(
                        "Product resolved install path from {Original} to {Resolved}",
                        beforeProduct,
                        resolvedTargetPath
                    );
            }
            finally
            {
                foreach (ProductPluginLoadContext resolverContext in resolverContexts)
                {
                    try
                    {
                        resolverContext.Unload();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Install-path resolver context unload failed");
                    }
                }
            }
        }

        if (OnResolveInstallPath is not null)
        {
            string? resolved = OnResolveInstallPath(resolvedTargetPath, fileHandlerContext);
            if (resolved is not null && resolved != resolvedTargetPath)
            {
                _logger.LogInformation(
                    "Plugin resolved install path from {Original} to {Resolved}",
                    resolvedTargetPath,
                    resolved
                );
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        0,
                        $"Resolved install path: {resolved}"
                    )
                );
                resolvedTargetPath = resolved;
            }
        }

        // Check elevation for resolved path (the initial check used the raw template path)
        if (
            resolvedTargetPath != options.TargetPath
            && ElevationHelper.PathRequiresAdmin(resolvedTargetPath)
            && !ElevationHelper.IsRunningAsAdmin()
        )
        {
            _logger.LogInformation("Resolved path {Path} requires elevation", resolvedTargetPath);
            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    0,
                    "Administrator privileges required for resolved path..."
                )
            );

            string? configFilePath = null;
            try
            {
                Dictionary<string, string> elevationConfig = options.PluginConfigValues
                    is { Count: > 0 }
                    ? new Dictionary<string, string>(options.PluginConfigValues)
                    : new Dictionary<string, string>();
                elevationConfig["__instanceUniqueId"] = instanceUniqueId;
                if (options.RemovePreviousFiles)
                    elevationConfig["__removePreviousFiles"] = "true";

                configFilePath = await ElevationConfigFile.WriteAsync(
                    elevationConfig,
                    cancellationToken
                );

                Operation.LastElevatedError = null;
                ReportProgress(InstallStage.Installing, 0, Localize("Elevation_Starting"));
                ElevationResult elevation = ElevationHelper.RunElevatedInstall(
                    manifest.ProductId,
                    manifest.Version,
                    resolvedTargetPath,
                    options.FeedId ?? FirstFeedId(),
                    options.InstanceId,
                    configFilePath,
                    onProgressLine: ReportElevatedProgressLine,
                    onLongRunning: HandleElevatedLongRunning,
                    cancellationToken: cancellationToken
                );
                if (elevation != ElevationResult.Succeeded)
                    return (
                        resolvedTargetPath,
                        new InstallResult
                        {
                            Success = false,
                            ErrorMessage = ElevationFailureMessage(
                                elevation,
                                "Installation cancelled: administrator rights were denied."
                            ),
                            FailedStep =
                                elevation == ElevationResult.DeniedByUser
                                    ? "Elevation"
                                    : "ElevatedInstall",
                        }
                    );

                if (options.PluginConfigValues is { Count: > 0 })
                {
                    await SavePluginConfigValues(
                        manifest.ProductId,
                        instanceUniqueId,
                        options.PluginConfigValues,
                        cancellationToken
                    );
                }
            }
            finally
            {
                if (configFilePath is not null)
                {
                    try
                    {
                        File.Delete(configFilePath);
                    }
                    catch { }
                }
            }

            await RegisterElevatedInstallAsync(
                manifest,
                resolvedTargetPath,
                options.FeedId,
                "(elevated, resolved path)",
                cancellationToken,
                options.InstanceId,
                instanceUniqueId
            );
            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    100,
                    "Installation completed via elevated process."
                )
            );
            return (resolvedTargetPath, new InstallResult { Success = true });
        }

        if (resolvedTargetPath.Contains('{') && resolvedTargetPath.Contains('}'))
        {
            string msg =
                $"Install path contains unresolved template: {resolvedTargetPath}. "
                + "Configure the required plugin settings (e.g., Application paths) before installing.";
            _logger.LogError(msg);
            progress.Report(new InstallProgress(InstallStage.Installing, 0, msg));
            return (
                resolvedTargetPath,
                new InstallResult
                {
                    Success = false,
                    ErrorMessage = msg,
                    FailedStep = "PathResolution",
                }
            );
        }

        string normalizedPath = Path.GetFullPath(resolvedTargetPath);
        IReadOnlyList<InstalledProduct> allProducts = await _productRepository.GetAllAsync(
            cancellationToken
        );
        InstalledProduct? conflict = allProducts.FirstOrDefault(p =>
            string.Equals(
                Path.GetFullPath(p.InstalledPath),
                normalizedPath,
                StringComparison.OrdinalIgnoreCase
            ) && !(p.ProductId == manifest.ProductId && p.InstanceId == options.InstanceId)
        );
        if (conflict is not null && !manifest.SharedInstallLocation)
        {
            string conflictMsg =
                $"Install path '{resolvedTargetPath}' is already used by {conflict.ProductId}/{conflict.InstanceId}. "
                + "Choose a different path or instance identifier.";
            _logger.LogError(conflictMsg);
            progress.Report(new InstallProgress(InstallStage.Installing, 0, conflictMsg));
            return (
                resolvedTargetPath,
                new InstallResult
                {
                    Success = false,
                    ErrorMessage = conflictMsg,
                    FailedStep = "PathConflict",
                }
            );
        }

        if (conflict is not null)
        {
            _logger.LogInformation(
                "Install path '{Path}' is a declared shared install location; {ProductId} installs "
                    + "alongside {ConflictId}/{ConflictInstance}.",
                resolvedTargetPath,
                manifest.ProductId,
                conflict.ProductId,
                conflict.InstanceId
            );
        }

        return (resolvedTargetPath, null);
    }

    private async Task<InstallResult?> CopyFilesToTargetAsync(
        string extractPath,
        string targetPath,
        HashSet<string> handledFiles,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        _logger.LogInformation("Copying files to {TargetPath}", targetPath);
        progress.Report(
            new InstallProgress(InstallStage.Installing, 0, $"Copying files to {targetPath}...")
        );
        Directory.CreateDirectory(targetPath);

        FileOperations fileOps = new FileOperations();
        DeferredFileOps deferredOps = new DeferredFileOps();
        List<string> deferredRenames = new List<string>();

        try
        {
            await CopyDirectoryWithLockHandlingAsync(
                fileOps,
                deferredOps,
                extractPath,
                targetPath,
                deferredRenames,
                handledFiles,
                new Progress<int>(pct =>
                    progress.Report(
                        new InstallProgress(InstallStage.Installing, pct, "Copying files...")
                    )
                ),
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Installation failed at step Installing (file copy): {Error}",
                ex.Message
            );
            progress.Report(
                new InstallProgress(InstallStage.Installing, 0, $"File copy failed: {ex}")
            );
            RevertDeferredRenames(deferredRenames, targetPath);
            return new InstallResult
            {
                Success = false,
                ErrorMessage = "File copy failed: " + ex.Message,
                FailedStep = "Installing",
                Exception = ex,
            };
        }

        progress.Report(new InstallProgress(InstallStage.Installing, 100, "Files copied."));
        return null;
    }

    private Task<InstallResult?> VerifyInstallationAsync(
        ProductManifest manifest,
        string targetPath,
        string tempDir,
        HashSet<string> handledFiles,
        IProgress<InstallProgress> progress
    )
    {
        progress.Report(
            new InstallProgress(InstallStage.Verifying, 0, "Verifying installation...")
        );

        if (!Directory.Exists(targetPath))
            return Task.FromResult<InstallResult?>(
                new InstallResult
                {
                    Success = false,
                    ErrorMessage = "Verification failed: target directory does not exist.",
                    FailedStep = "Verifying",
                }
            );

        string[] installedFiles = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories);
        if (installedFiles.Length == 0)
            return Task.FromResult<InstallResult?>(
                new InstallResult
                {
                    Success = false,
                    ErrorMessage = "Verification failed: no files in target directory.",
                    FailedStep = "Verifying",
                }
            );

        progress.Report(
            new InstallProgress(
                InstallStage.Verifying,
                25,
                $"Target directory contains {installedFiles.Length} file(s)."
            )
        );

        // Cross-check against extracted files
        if (Directory.Exists(tempDir))
        {
            string[] extractedFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
            int expectedCount = extractedFiles.Length - handledFiles.Count;
            if (expectedCount > 0 && installedFiles.Length < expectedCount)
            {
                _logger.LogWarning(
                    "Verification warning: expected {Expected} files, found {Actual}",
                    expectedCount,
                    installedFiles.Length
                );
                progress.Report(
                    new InstallProgress(
                        InstallStage.Verifying,
                        50,
                        $"Warning: Verification warning: expected {expectedCount} files, found {installedFiles.Length}"
                    )
                );
            }
            else
            {
                progress.Report(
                    new InstallProgress(
                        InstallStage.Verifying,
                        50,
                        $"File count verified: {installedFiles.Length} file(s) installed."
                    )
                );
            }
        }

        // Check shortcut targets exist
        if (manifest.Shortcuts is { Length: > 0 })
        {
            foreach (ShortcutInfo shortcut in manifest.Shortcuts)
            {
                string exePath = Path.Combine(targetPath, shortcut.ExeName);
                if (!File.Exists(exePath))
                {
                    _logger.LogWarning(
                        "Shortcut target missing after install: {ExeName}",
                        shortcut.ExeName
                    );
                    progress.Report(
                        new InstallProgress(
                            InstallStage.Verifying,
                            50,
                            $"Warning: Shortcut target missing after install: {shortcut.ExeName}"
                        )
                    );
                }
            }
        }

        // Verify no zero-byte executables
        foreach (string file in installedFiles)
        {
            if (
                (
                    file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                )
                && new FileInfo(file).Length == 0
            )
            {
                string fileName = Path.GetFileName(file);
                return Task.FromResult<InstallResult?>(
                    new InstallResult
                    {
                        Success = false,
                        ErrorMessage = $"Verification failed: '{fileName}' is 0 bytes.",
                        FailedStep = "Verifying",
                    }
                );
            }
        }

        progress.Report(
            new InstallProgress(
                InstallStage.Verifying,
                100,
                "Verification complete - all checks passed."
            )
        );
        return Task.FromResult<InstallResult?>(null);
    }

    private async Task FinalizeInstallationAsync(
        ProductManifest manifest,
        InstallOptions options,
        string resolvedPath,
        PluginContext pluginContext,
        string extractPath,
        string copySourcePath,
        HashSet<string> handledFiles,
        string instanceUniqueId,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken,
        bool skipPostInstall = false
    )
    {
        InstallOptions resolvedOptions = options with { TargetPath = resolvedPath };

        if (skipPostInstall)
        {
            _logger.LogInformation("PostInstall skipped (disabled by user)");
        }
        else
        {
            PluginPhaseResult postInstallResult = await RunPluginPhaseAsync(
                manifest,
                resolvedOptions,
                pluginContext,
                PluginPhase.PostInstall,
                cancellationToken,
                extractPath
            );
            if (!postInstallResult.Success)
            {
                _logger.LogError(
                    "PostInstall plugin phase failed: {Error}",
                    postInstallResult.ErrorMessage
                );
                ReportProgress(
                    InstallStage.Installing,
                    0,
                    $"PostInstall failed: {postInstallResult.ErrorMessage}"
                );

                throw new InvalidOperationException(
                    $"PostInstall failed: {postInstallResult.ErrorMessage}"
                );
            }
        }

        progress.Report(new InstallProgress(InstallStage.Installing, 80, "Creating shortcuts..."));
        CreateShortcuts(manifest, resolvedPath);

        progress.Report(
            new InstallProgress(InstallStage.Installing, 85, "Applying environment variables...")
        );
        await ApplyEnvironmentVariablesAsync(
            manifest,
            instanceUniqueId,
            resolvedPath,
            cancellationToken
        );

        await SavePluginConfigValues(
            manifest.ProductId,
            instanceUniqueId,
            options.PluginConfigValues,
            cancellationToken
        );

        progress.Report(
            new InstallProgress(InstallStage.Installing, 90, "Saving file manifest...")
        );
        await SaveFileManifestAsync(
            manifest.ProductId,
            instanceUniqueId,
            resolvedPath,
            manifest.ExcludeFiles,
            copySourcePath,
            handledFiles,
            cancellationToken
        );

        progress.Report(new InstallProgress(InstallStage.Installing, 95, "Registering product..."));
        await RegisterElevatedInstallAsync(
            manifest,
            resolvedPath,
            options.FeedId,
            "",
            cancellationToken,
            options.InstanceId,
            instanceUniqueId
        );
    }

    private void CleanupTempDirectory(string tempDir, IProgress<InstallProgress>? progress = null)
    {
        try
        {
            if (!Directory.Exists(tempDir))
                return;

            foreach (ProductPluginLoadContext ctx in Operation.PluginContexts)
            {
                try
                {
                    ctx.Unload();
                }
                catch { }
            }
            Operation.PluginContexts.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            progress?.Report(
                new InstallProgress(InstallStage.Installing, 0, "Cleaning up temporary files...")
            );

            try
            {
                Directory.Delete(tempDir, true);
                progress?.Report(
                    new InstallProgress(InstallStage.Installing, 0, "Cleanup complete.")
                );
                return;
            }
            catch (Exception) when (Directory.Exists(tempDir))
            {
                // Native DLLs (like SNI) can't be unloaded until process exit.
                // Delete what we can, leave the rest for next startup cleanup.
                foreach (
                    string file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
                )
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch { }
                }
                progress?.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        0,
                        "Cleanup partially complete. Remaining files will be cleaned on next startup."
                    )
                );
                return;
            }
        }
        catch (Exception ex)
        {
            progress?.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    0,
                    $"Warning: temporary files could not be cleaned up: {ex.Message}"
                )
            );
            _logger.LogWarning(ex, "Temporary directory could not be deleted: {TempDir}", tempDir);
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        InstalledProduct installed,
        ProductManifest newManifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation(
            "Starting update of {ProductId} from {OldVersion} to {NewVersion}",
            installed.ProductId,
            installed.Version,
            newManifest.Version
        );

        string? hostVersionError = CheckMinHostVersion(newManifest);
        if (hostVersionError is not null)
            throw new InvalidOperationException(hostVersionError);

        using ProductLockAcquisition operationLock = ProductOperationLock.TryAcquire(
            installed.ProductId,
            installed.InstanceId,
            "update"
        );
        if (!operationLock.Acquired)
            throw new InvalidOperationException(operationLock.DescribeConflict(installed.Title));

        using OperationScope operationScope = BeginOperation(progress);

        List<string>? lockCheckManifest = await LoadFileManifest(
            installed.ProductId,
            installed.InstanceUniqueId ?? string.Empty,
            cancellationToken
        );
        List<string> trackedPaths =
            lockCheckManifest is { Count: > 0 } && Directory.Exists(installed.InstalledPath)
                ? lockCheckManifest
                    .Select(relativePath => Path.Combine(installed.InstalledPath, relativePath))
                    .Where(File.Exists)
                    .ToList()
                : [];
        _logger.LogInformation(
            "Checking {Count} tracked files for locks before updating {ProductId}",
            trackedPaths.Count,
            installed.ProductId
        );
        IReadOnlyList<LockedFileInfo> lockedFiles = _fileLockDetector.GetLockedFiles(
            trackedPaths,
            cancellationToken
        );
        if (lockedFiles.Count > 0)
        {
            if (OnLockedFilesDetected is not null)
            {
                if (
                    !ResolveLockedFilesBeforeUpdate(
                        lockedFiles,
                        trackedPaths,
                        installed,
                        cancellationToken
                    )
                )
                {
                    throw new OperationCanceledException(
                        $"Update of {installed.Title} cancelled: files in use were not released."
                    );
                }
            }
            else
            {
                foreach (LockedFileInfo lockedFile in lockedFiles)
                {
                    string processNames = string.Join(
                        ", ",
                        lockedFile.Processes.Select(p => p.ProcessName)
                    );
                    _logger.LogWarning(
                        "Locked file during update of {ProductId}: {File} (used by {Processes})",
                        installed.ProductId,
                        lockedFile.FileName,
                        processNames
                    );
                }
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        0,
                        $"Warning: {lockedFiles.Count} file(s) in use. They will be replaced on restart."
                    )
                );
            }
        }

        string? backupPath = null;
        string? metadataBackupPath = null;
        if (options.CreateBackup && Directory.Exists(installed.InstalledPath))
        {
            IReadOnlyList<string> backupFiles = CollectBackupFiles(
                installed.InstalledPath,
                lockCheckManifest,
                newManifest
            );
            long backupStartTicks = Environment.TickCount64;
            if (backupFiles.Count > 0)
            {
                _logger.LogInformation(
                    "Creating backup of {ProductId} ({Count} files) before update",
                    installed.ProductId,
                    backupFiles.Count
                );
                backupPath = await _backupService.CreateBackupAsync(
                    installed.ProductId,
                    installed.InstalledPath,
                    backupFiles,
                    cancellationToken
                );
            }
            else
            {
                _logger.LogWarning(
                    "No files to back up for {ProductId}; the update runs without a backup",
                    installed.ProductId
                );
            }

            metadataBackupPath = await CreateMetadataBackupAsync(installed, cancellationToken);
            _logger.LogInformation(
                "Backup of {ProductId} complete in {ElapsedMs}ms",
                installed.ProductId,
                Environment.TickCount64 - backupStartTicks
            );
        }

        HashSet<string> preUpdateFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(installed.InstalledPath))
        {
            try
            {
                foreach (
                    string existing in Directory.GetFiles(
                        installed.InstalledPath,
                        "*",
                        SearchOption.AllDirectories
                    )
                )
                    preUpdateFiles.Add(existing);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not snapshot {InstalledPath} before update",
                    installed.InstalledPath
                );
            }
        }

        try
        {
            _logger.LogDebug(
                "Removing old environment variables for {ProductId}",
                installed.ProductId
            );
            try
            {
                List<AppliedEnvironmentVariable> notRemoved =
                    await _envVarService.RemoveTrackedAsync(
                        installed.ProductId,
                        installed.InstanceUniqueId ?? string.Empty,
                        cancellationToken
                    );
                if (notRemoved.Count > 0)
                    _logger.LogWarning(
                        "{Count} environment variable change(s) of {ProductId} could not be removed and stay tracked",
                        notRemoved.Count,
                        installed.ProductId
                    );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Old environment variables of {ProductId} could not be removed",
                    installed.ProductId
                );
            }

            // The old files are removed inside InstallInternalAsync, after the plugin's PreInstall
            // succeeds (see RemovePreviousFiles) - so a failing PreInstall leaves the existing install
            // untouched and there is nothing to roll back. PreInstall is also where a product stops its
            // own services, which unlocks their files for the delete.
            _logger.LogDebug(
                "Running InstallAsync for new version {NewVersion}",
                newManifest.Version
            );
            InstallResult result = await InstallInternalAsync(
                newManifest,
                options with
                {
                    RemovePreviousFiles = true,
                },
                progress,
                cancellationToken
            );
            if (!result.Success)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "Installation failed.");
            }

            if (backupPath is not null)
            {
                InstalledProduct? updated = await _productRepository.GetByIdAsync(
                    newManifest.ProductId,
                    options.InstanceId,
                    cancellationToken
                );
                if (updated is not null)
                    await _productRepository.UpdateAsync(
                        updated with
                        {
                            BackupPath = backupPath,
                        },
                        cancellationToken
                    );
            }

            _logger.LogInformation(
                "Update of {ProductId} from {OldVersion} to {NewVersion} complete",
                installed.ProductId,
                installed.Version,
                newManifest.Version
            );

            await _feedReportService.NotifyFeedChangedAsync(
                options.FeedId ?? installed.FeedId,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update failed for {ProductId}", installed.ProductId);

            bool cancelled = cancellationToken.IsCancellationRequested;
            if (!cancelled && !ShouldRollBackAfterFailure(installed, newManifest, ex))
            {
                _logger.LogWarning(
                    "Rollback declined for {ProductId}; keeping the updated files.",
                    installed.ProductId
                );
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        0,
                        $"Update failed but the new version was kept (not rolled back): {ex.Message}"
                    )
                );
                await RegisterKeptUpdateAsync(
                    installed,
                    newManifest,
                    options,
                    backupPath,
                    cancellationToken
                );
                return;
            }

            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    0,
                    $"Update failed for {installed.ProductId}, restoring backup"
                )
            );

            using (CancellationTokenSource rollbackPluginTimeout = new(RollbackPluginTimeout))
            {
                await StopProductServicesForRollbackAsync(
                    installed,
                    newManifest,
                    rollbackPluginTimeout.Token
                );
            }

            if (backupPath is not null && Directory.Exists(installed.InstalledPath))
            {
                try
                {
                    string[] currentFiles = Directory.GetFiles(
                        installed.InstalledPath,
                        "*",
                        SearchOption.AllDirectories
                    );
                    int removed = 0;
                    foreach (string current in currentFiles)
                    {
                        if (preUpdateFiles.Contains(current))
                            continue;
                        try
                        {
                            File.Delete(current);
                            removed++;
                        }
                        catch (Exception delEx)
                        {
                            _logger.LogWarning(
                                delEx,
                                "Could not remove newly-added file during rollback: {File}",
                                current
                            );
                        }
                    }
                    _logger.LogInformation(
                        "Rollback removed {Count} newly-added file(s) for {ProductId}",
                        removed,
                        installed.ProductId
                    );
                }
                catch (Exception scanEx)
                {
                    _logger.LogWarning(
                        scanEx,
                        "Could not enumerate files during rollback for {ProductId}",
                        installed.ProductId
                    );
                }
            }

            if (backupPath is not null)
            {
                _logger.LogInformation(
                    "Restoring backup for {ProductId} from {BackupPath}",
                    installed.ProductId,
                    backupPath
                );
                await _backupService.RestoreBackupAsync(
                    backupPath,
                    installed.InstalledPath,
                    CancellationToken.None
                );
            }

            await RestoreMetadataAsync(installed, metadataBackupPath);
            if (cancelled)
                throw new OperationCanceledException(cancellationToken);
            throw;
        }
    }

    private bool ResolveLockedFilesBeforeUpdate(
        IReadOnlyList<LockedFileInfo> lockedFiles,
        IReadOnlyList<string> trackedPaths,
        InstalledProduct installed,
        CancellationToken cancellationToken
    )
    {
        for (int prompt = 0; prompt < MaxLockedFilesPrompts; prompt++)
        {
            LockedFilesAction action = OnLockedFilesDetected!(
                lockedFiles,
                _fileLockDetector,
                installed.InstalledPath
            );

            if (action == LockedFilesAction.RenameAndContinue)
            {
                Operation.DeferRemainingLockedFiles = true;
                return true;
            }

            if (action != LockedFilesAction.Retry)
                return false;

            lockedFiles = _fileLockDetector.GetLockedFiles(trackedPaths, cancellationToken);
            if (lockedFiles.Count == 0)
                return true;
        }

        return false;
    }

    private static IReadOnlyList<string> CollectBackupFiles(
        string installPath,
        IReadOnlyList<string>? trackedFiles,
        ProductManifest newManifest
    )
    {
        HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (trackedFiles is { Count: > 0 })
            files.UnionWith(trackedFiles);
        else if (!newManifest.SharedInstallLocation)
            files.UnionWith(
                Directory
                    .GetFiles(installPath, "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(installPath, file))
            );

        string[] preservedPatterns =
        [
            .. newManifest.ExcludeFiles ?? [],
            .. newManifest.PreserveOnSwitch ?? [],
        ];
        foreach (string preserved in ResolveExcludedFiles(installPath, preservedPatterns))
            files.Add(Path.GetRelativePath(installPath, preserved));

        return files.ToList();
    }

    private async Task<string?> CreateMetadataBackupAsync(
        InstalledProduct installed,
        CancellationToken cancellationToken
    )
    {
        string uniqueId = installed.InstanceUniqueId ?? string.Empty;
        string configDir = StorkPaths.StorkConfigDir;
        string metadataDir = StorkPaths.ProductMetadataDir(installed.ProductId, uniqueId);

        List<string> files = [];
        if (Directory.Exists(metadataDir))
            files.AddRange(
                Directory
                    .GetFiles(metadataDir, "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(configDir, file))
            );
        foreach (
            string sibling in new[]
            {
                StorkPaths.FileManifestPath(installed.ProductId, uniqueId),
                StorkPaths.InstancePluginConfigPath(installed.ProductId, uniqueId),
            }
        )
        {
            if (File.Exists(sibling))
                files.Add(Path.GetRelativePath(configDir, sibling));
        }

        if (files.Count == 0)
            return null;

        return await _backupService.CreateBackupAsync(
            MetadataBackupKey(installed),
            configDir,
            files,
            cancellationToken
        );
    }

    private async Task RestoreMetadataAsync(InstalledProduct installed, string? metadataBackupPath)
    {
        if (metadataBackupPath is null)
            return;

        string metadataDir = StorkPaths.ProductMetadataDir(
            installed.ProductId,
            installed.InstanceUniqueId ?? string.Empty
        );
        try
        {
            if (Directory.Exists(metadataDir))
                Directory.Delete(metadataDir, recursive: true);

            await _backupService.RestoreBackupAsync(
                metadataBackupPath,
                StorkPaths.StorkConfigDir,
                CancellationToken.None
            );
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.LogError(
                ex,
                "Could not restore the .stork metadata of {ProductId} from {BackupPath}",
                installed.ProductId,
                metadataBackupPath
            );
        }
    }

    private static string MetadataBackupKey(InstalledProduct installed) =>
        $"{installed.ProductId}_{installed.InstanceUniqueId}.stork";

    /// <summary>
    /// Before restoring a backup, lets each product plugin release its file locks (typically by stopping
    /// its own services) via <see cref="IRollbackAwarePlugin"/>, so the restore does not fail on files
    /// held open by a still-running service. Loads the plugins from the persistent metadata directory
    /// (they are unloaded by then) and is fully best-effort - any failure (e.g. no admin rights) is
    /// logged and the rollback proceeds.
    /// </summary>
    private async Task StopProductServicesForRollbackAsync(
        InstalledProduct installed,
        ProductManifest newManifest,
        CancellationToken cancellationToken
    )
    {
        if (newManifest.Plugins is not { Length: > 0 })
            return;

        string metadataDir = StorkPaths.ProductMetadataDir(
            installed.ProductId,
            installed.InstanceUniqueId ?? string.Empty
        );
        if (!Directory.Exists(Path.Combine(metadataDir, "plugins")))
            return;

        PluginContext context = new PluginContext
        {
            ProductId = installed.ProductId,
            InstanceId = installed.InstanceId,
            InstanceUniqueId = installed.InstanceUniqueId ?? string.Empty,
            Version = installed.Version,
            InstallPath = installed.InstalledPath,
            StorkConfigDirectory = GetStorkConfigDir(),
            Log = message => _logger.LogInformation("[Rollback] {Message}", message),
        };

        List<ProductPluginLoadContext> contexts = [];
        try
        {
            foreach (StorkPluginInfo pluginInfo in newManifest.Plugins)
            {
                try
                {
                    IStorkPlugin? plugin = LoadPlugin(metadataDir, pluginInfo, contexts);
                    if (plugin is IRollbackAwarePlugin rollbackAware)
                        await rollbackAware.PrepareForRollbackAsync(context, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "PrepareForRollback failed for {TypeName} on {ProductId}; continuing rollback",
                        pluginInfo.TypeName,
                        installed.ProductId
                    );
                }
            }
        }
        finally
        {
            foreach (ProductPluginLoadContext pluginContext in contexts)
            {
                try
                {
                    pluginContext.Unload();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Rollback plugin context unload failed");
                }
            }
        }
    }

    /// <summary>
    /// After a failed update, asks whether to roll back to the previous version or keep the updated
    /// files. Some late failures (e.g. a Windows service that starts too slowly) leave a usable
    /// install, where a rollback would only discard already-applied files and database changes.
    /// Headless callers (no prompt handler) keep the safe default of rolling back.
    /// </summary>
    private bool ShouldRollBackAfterFailure(
        InstalledProduct installed,
        ProductManifest newManifest,
        Exception ex
    )
    {
        if (OnPrompt is null)
            return true;

        PluginPromptResult choice = OnPrompt(
            new PluginPrompt
            {
                Title = Localize("Rollback_Title"),
                Message =
                    Localize(
                        "Rollback_Message",
                        installed.Title,
                        installed.Version,
                        newManifest.Version
                    )
                    + Environment.NewLine
                    + Environment.NewLine
                    + Localize("Rollback_KeepHint"),
                Detail = ex.Message,
                Options = [Localize("Rollback_Keep"), Localize("Rollback_Restore")],
                DefaultOptionIndex = 1,
            }
        );

        // Roll back unless the user explicitly chose "keep" (index 0).
        return choice.Cancelled || choice.ChosenIndex != 0;
    }

    private async Task RegisterKeptUpdateAsync(
        InstalledProduct installed,
        ProductManifest newManifest,
        InstallOptions options,
        string? backupPath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            InstalledProduct? current = await _productRepository.GetByIdAsync(
                newManifest.ProductId,
                options.InstanceId,
                cancellationToken
            );
            InstalledProduct baseRecord = current ?? installed;
            await _productRepository.UpdateAsync(
                baseRecord with
                {
                    Version = newManifest.Version,
                    FeedId = options.FeedId ?? baseRecord.FeedId,
                    BackupPath = backupPath,
                },
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not update installed record after a kept update for {ProductId}",
                newManifest.ProductId
            );
        }
    }

    /// <summary>
    /// After a failed fresh install, asks whether to remove the partial install or keep it. Late
    /// failures (a slow-starting service) often leave a usable install where a removal would only
    /// discard already-copied files. Headless callers (no prompt) default to removing it.
    /// </summary>
    private bool ShouldRemoveAfterInstallFailure(ProductManifest manifest, Exception ex)
    {
        if (OnPrompt is null)
            return true;

        PluginPromptResult choice = OnPrompt(
            new PluginPrompt
            {
                Title = Localize("Rollback_InstallTitle"),
                Message =
                    Localize("Rollback_InstallMessage", manifest.Title, manifest.Version)
                    + Environment.NewLine
                    + Environment.NewLine
                    + Localize("Rollback_InstallHint"),
                Detail = ex.Message,
                Options = [Localize("Rollback_KeepInstall"), Localize("Rollback_RemoveInstall")],
                DefaultOptionIndex = 1,
            }
        );

        // Remove unless the user explicitly chose "keep" (index 0).
        return choice.Cancelled || choice.ChosenIndex != 0;
    }

    private void TryRemoveInstallDir(string? installPath)
    {
        if (
            string.IsNullOrEmpty(installPath)
            || installPath.Contains('{')
            || !Directory.Exists(installPath)
        )
            return;

        try
        {
            Directory.Delete(installPath, recursive: true);
            _logger.LogInformation(
                "Removed install directory {Path} after failed install",
                installPath
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not remove install directory {Path} after failed install",
                installPath
            );
        }
    }

    /// <inheritdoc />
    public Task<InstallResult> SwitchChannelAsync(
        InstalledProduct installed,
        ProductManifest newChannelManifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotImplementedException(
            "Channel switching will be implemented in a later phase."
        );
    }

    /// <inheritdoc />
    public async Task UninstallAsync(
        InstalledProduct product,
        CancellationToken cancellationToken = default
    )
    {
        using ProductLockAcquisition operationLock = ProductOperationLock.TryAcquire(
            product.ProductId,
            product.InstanceId,
            "uninstall"
        );
        if (!operationLock.Acquired)
            throw new InvalidOperationException(operationLock.DescribeConflict(product.Title));

        _logger.LogInformation(
            "Delegating uninstall of {ProductId} to UninstallService",
            product.ProductId
        );
        await _uninstallService.UninstallAsync(product, progress: null, cancellationToken);
    }

    public async Task<InstallResult> ReExecutePluginsAsync(
        InstalledProduct product,
        ReExecuteOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    )
    {
        using ProductLockAcquisition operationLock = ProductOperationLock.TryAcquire(
            product.ProductId,
            product.InstanceId,
            "re-execute"
        );
        if (!operationLock.Acquired)
            return new InstallResult
            {
                Success = false,
                ErrorMessage = operationLock.DescribeConflict(product.Title),
                FailedStep = "Coordination",
            };

        using OperationScope operationScope = BeginOperation(progress);

        try
        {
            ReportProgress(
                InstallStage.RunningPlugins,
                0,
                $"Loading plugin data for {product.Title}..."
            );

            string storkDir = StorkPaths.ProductMetadataDir(
                product.ProductId,
                product.InstanceUniqueId ?? string.Empty
            );
            string manifestPath = Path.Combine(storkDir, "manifest.json");

            if (!File.Exists(manifestPath))
            {
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage =
                        "No plugin data found for this product. The product may need to be reinstalled.",
                };
            }

            string manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            ProductManifest? manifest =
                System.Text.Json.JsonSerializer.Deserialize<ProductManifest>(
                    manifestJson,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    }
                );

            if (manifest is not null && CheckMinHostVersion(manifest) is string hostVersionError)
            {
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = hostVersionError,
                    FailedStep = "HostVersion",
                };
            }

            string storkFilesDir = Path.Combine(storkDir, "files");
            bool hasFileHandlerFiles =
                Directory.Exists(storkFilesDir) && Directory.GetFiles(storkFilesDir).Length > 0;
            bool hasPlugins = manifest?.Plugins is { Length: > 0 };

            if (!hasPlugins && !hasFileHandlerFiles)
            {
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage =
                        "This product has no plugins or file handler data to re-execute.",
                };
            }

            ReportProgress(InstallStage.RunningPlugins, 10, "Building action groups...");

            List<PluginActionGroup> groups = [];

            if (hasPlugins)
            {
                PluginEnvironment environment = await BuildPluginEnvironmentAsync(
                    manifest!,
                    cancellationToken,
                    product.InstanceId
                );

                foreach (StorkPluginInfo pluginInfo in manifest!.Plugins!)
                {
                    try
                    {
                        IStorkPlugin? plugin = LoadPlugin(
                            storkDir,
                            pluginInfo,
                            Operation.PluginContexts
                        );
                        if (plugin is null)
                        {
                            _logger.LogWarning(
                                "Plugin {TypeName} not found in .stork/",
                                pluginInfo.TypeName
                            );
                            continue;
                        }

                        if (plugin is IInteractiveStorkPlugin interactive)
                            CurrentInteractivePlugin = interactive;

                        if (plugin is IDescribableStorkPlugin describable)
                        {
                            IReadOnlyList<PluginActionDescription> descriptions =
                                describable.GetActionDescriptions(environment);

                            foreach (PluginActionDescription desc in descriptions)
                            {
                                bool defaultEnabled =
                                    desc.Phase == PluginActionPhase.PreInstall
                                        ? options.RunPreInstall
                                        : options.RunPostInstall;

                                groups.Add(
                                    new PluginActionGroup
                                    {
                                        GroupId =
                                            $"{desc.Phase.ToString().ToLowerInvariant()}-{pluginInfo.TypeName}-{desc.Title}",
                                        Title = desc.Title,
                                        Phase = desc.Phase,
                                        IsEnabled = defaultEnabled && desc.IsEnabled,
                                        Fields = desc.Fields,
                                        Descriptions = [desc],
                                    }
                                );
                            }
                        }
                        else
                        {
                            IReadOnlyList<PluginConfigField> fields = plugin.GetConfigurationSchema(
                                environment
                            );
                            string shortName = pluginInfo.TypeName.Contains('.')
                                ? pluginInfo.TypeName[(pluginInfo.TypeName.LastIndexOf('.') + 1)..]
                                : pluginInfo.TypeName;

                            groups.Add(
                                new PluginActionGroup
                                {
                                    GroupId = $"preinstall-{pluginInfo.TypeName}",
                                    Title = $"PreInstall: {shortName}",
                                    Phase = PluginActionPhase.PreInstall,
                                    IsEnabled = options.RunPreInstall,
                                    Fields = fields,
                                    Descriptions = [],
                                }
                            );

                            groups.Add(
                                new PluginActionGroup
                                {
                                    GroupId = $"postinstall-{pluginInfo.TypeName}",
                                    Title = $"PostInstall: {shortName}",
                                    Phase = PluginActionPhase.PostInstall,
                                    IsEnabled = options.RunPostInstall,
                                    Fields = [],
                                    Descriptions = [],
                                }
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to load plugin {TypeName}",
                            pluginInfo.TypeName
                        );
                    }
                }
            }

            if (hasFileHandlerFiles && _fileTypeHandlers.Count > 0 && options.RunFileHandlers)
            {
                ReportProgress(InstallStage.RunningPlugins, 15, "Running file handlers...");
                string[] savedFiles = Directory.GetFiles(storkFilesDir);

                foreach (IFileTypeHandler handler in _fileTypeHandlers)
                {
                    try
                    {
                        List<string> matchingFiles = savedFiles
                            .Where(f =>
                                handler.HandledExtensions.Any(ext =>
                                    f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                                )
                            )
                            .ToList();

                        if (matchingFiles.Count == 0)
                            continue;

                        PluginContext fileContext = new PluginContext
                        {
                            ProductId = product.ProductId,
                            InstanceId = product.InstanceId,
                            InstanceUniqueId = product.InstanceUniqueId ?? string.Empty,
                            Version = product.Version,
                            PreviousVersion = product.Version,
                            InstallPath = product.InstalledPath,
                            StorkConfigDirectory = GetStorkConfigDir(),
                        };

                        IReadOnlyList<PluginConfigField> configFields =
                            handler.GetFileHandlerConfig(matchingFiles, fileContext);

                        if (configFields.Count > 0 && OnFileHandlerConfigNeeded is not null)
                        {
                            ReportProgress(
                                InstallStage.RunningPlugins,
                                18,
                                "Waiting for file handler configuration..."
                            );
                            Dictionary<string, string>? fileHandlerValues =
                                OnFileHandlerConfigNeeded(
                                    configFields,
                                    new Dictionary<string, string>()
                                );
                            if (fileHandlerValues is null)
                                return new InstallResult
                                {
                                    Success = false,
                                    ErrorMessage = "File handler configuration cancelled.",
                                };
                            fileContext.ConfigValues = fileHandlerValues;
                        }

                        string handlerName = handler is IStorkDropPlugin sdp
                            ? sdp.DisplayName
                            : handler.GetType().Name;

                        ReportProgress(
                            InstallStage.RunningPlugins,
                            20,
                            $"Processing {matchingFiles.Count} file(s) with {handlerName}..."
                        );
                        FileHandlerResult handlerResult = await handler.HandleFilesAsync(
                            matchingFiles,
                            fileContext,
                            cancellationToken
                        );

                        if (!handlerResult.Success)
                        {
                            return new InstallResult
                            {
                                Success = false,
                                ErrorMessage =
                                    handlerResult.ErrorMessage
                                    ?? $"File handler {handlerName} failed.",
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "File handler failed during re-execution");
                        return new InstallResult
                        {
                            Success = false,
                            ErrorMessage = $"File handler failed: {ex.Message}",
                        };
                    }
                }
            }

            Dictionary<string, string> previousValues = await LoadPluginConfigValues(
                product.ProductId,
                product.InstanceUniqueId ?? string.Empty,
                cancellationToken
            );
            Dictionary<string, string>? configValues = options.PluginConfigValues;

            if (configValues is null && groups.Count > 0)
            {
                if (OnActionGroupConfigNeeded is not null)
                {
                    ReportProgress(InstallStage.RunningPlugins, 15, "Waiting for configuration...");
                    configValues = OnActionGroupConfigNeeded(groups, previousValues);
                }
                else if (OnPluginConfigNeeded is not null)
                {
                    List<PluginConfigField> flatFields = groups.SelectMany(g => g.Fields).ToList();
                    if (flatFields.Count > 0)
                    {
                        ReportProgress(
                            InstallStage.RunningPlugins,
                            15,
                            "Waiting for configuration..."
                        );
                        configValues = OnPluginConfigNeeded(flatFields, previousValues);
                    }
                }

                if (configValues is null)
                {
                    return new InstallResult
                    {
                        Success = false,
                        ErrorMessage = "Configuration cancelled.",
                    };
                }
            }

            HashSet<string> disabledGroups = new HashSet<string>();
            if (configValues is not null)
            {
                foreach (string key in configValues.Keys)
                {
                    if (key.StartsWith("__group_enabled_") && configValues[key] == "false")
                        disabledGroups.Add(key["__group_enabled_".Length..]);
                }
            }

            Dictionary<string, string> effectiveConfig =
                configValues ?? options.PluginConfigValues ?? new Dictionary<string, string>();

            // Plugin phases (e.g. PostInstall) may write into the install dir. When that path needs
            // admin and we are not elevated, re-run them in an elevated child - config was already
            // gathered above, file handlers already ran here in the parent.
            if (
                hasPlugins
                && manifest!.Plugins!.Any(p =>
                    !disabledGroups.Contains($"preinstall-{p.TypeName}")
                    || !disabledGroups.Contains($"postinstall-{p.TypeName}")
                )
                && !ElevationHelper.IsRunningAsAdmin()
                && ElevationHelper.PathRequiresAdmin(product.InstalledPath)
            )
            {
                return await ReExecuteElevatedAsync(
                    product,
                    effectiveConfig,
                    options,
                    cancellationToken
                );
            }

            if (hasPlugins)
            {
                InstallOptions installOptions = new InstallOptions(
                    TargetPath: product.InstalledPath,
                    FeedId: product.FeedId,
                    PluginConfigValues: effectiveConfig
                );

                PluginContext context = new PluginContext
                {
                    ProductId = product.ProductId,
                    InstanceId = product.InstanceId,
                    InstanceUniqueId = product.InstanceUniqueId ?? string.Empty,
                    Version = product.Version,
                    PreviousVersion = product.Version,
                    InstallPath = product.InstalledPath,
                    StorkConfigDirectory = GetStorkConfigDir(),
                    ConfigValues = effectiveConfig,
                    ProductMetadata = manifest.Metadata is not null
                        ? new Dictionary<string, string>(manifest.Metadata)
                        : new Dictionary<string, string>(),
                    Log = message => _logger.LogInformation("[Plugin] {Message}", message),
                    Prompt = OnPrompt,
                    PickInstalledInstance = OnPickInstalledInstance,
                    GetInstalledInstances = await CreateInstalledInstancesLookupAsync(
                        cancellationToken
                    ),
                };

                bool preEnabled = manifest!.Plugins!.Any(p =>
                    !disabledGroups.Contains($"preinstall-{p.TypeName}")
                );
                bool postEnabled = manifest.Plugins.Any(p =>
                    !disabledGroups.Contains($"postinstall-{p.TypeName}")
                );

                if (preEnabled)
                {
                    ReportProgress(InstallStage.RunningPlugins, 40, "Running PreInstall...");
                    PluginPhaseResult preResult = await RunPluginPhaseAsync(
                        manifest,
                        installOptions,
                        context,
                        PluginPhase.PreInstall,
                        cancellationToken
                    );
                    if (!preResult.Success)
                    {
                        return new InstallResult
                        {
                            Success = false,
                            ErrorMessage = preResult.ErrorMessage ?? "PreInstall failed.",
                        };
                    }
                }

                if (postEnabled)
                {
                    ReportProgress(InstallStage.RunningPlugins, 70, "Running PostInstall...");
                    PluginPhaseResult postResult = await RunPluginPhaseAsync(
                        manifest,
                        installOptions,
                        context,
                        PluginPhase.PostInstall,
                        cancellationToken
                    );
                    if (!postResult.Success)
                    {
                        return new InstallResult
                        {
                            Success = false,
                            ErrorMessage = postResult.ErrorMessage ?? "PostInstall failed.",
                        };
                    }
                }
            }

            ReportProgress(InstallStage.RunningPlugins, 90, "Saving configuration...");
            await SavePluginConfigValues(
                product.ProductId,
                product.InstanceUniqueId ?? string.Empty,
                configValues,
                cancellationToken
            );

            ReportProgress(
                InstallStage.Verifying,
                100,
                $"Plugin actions for {product.Title} completed successfully."
            );

            await _activityLog.LogAsync(
                new ActivityLogEntry(
                    Id: Guid.NewGuid().ToString(),
                    Timestamp: DateTime.UtcNow,
                    Action: "ReExecute",
                    ProductId: product.ProductId,
                    Details: $"Re-executed plugin actions for {product.Title} v{product.Version}",
                    Success: true
                ),
                cancellationToken
            );

            return new InstallResult { Success = true };
        }
        catch (OperationCanceledException)
        {
            return new InstallResult { Success = false, ErrorMessage = "Operation cancelled." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-execute plugins for {ProductId}", product.ProductId);
            return new InstallResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Exception = ex,
            };
        }
        finally
        {
            foreach (ProductPluginLoadContext ctx in Operation.PluginContexts)
            {
                try
                {
                    ctx.Unload();
                }
                catch { }
            }
            Operation.PluginContexts.Clear();
            CurrentInteractivePlugin = null;
        }
    }

    private async Task<InstallResult> ReExecuteElevatedAsync(
        InstalledProduct product,
        Dictionary<string, string> configValues,
        ReExecuteOptions options,
        CancellationToken cancellationToken
    )
    {
        string configFilePath = await ElevationConfigFile.WriteAsync(
            configValues,
            cancellationToken
        );
        try
        {
            return RunElevatedReExecute(product, options, configFilePath, cancellationToken);
        }
        finally
        {
            ElevationConfigFile.TryDelete(configFilePath);
        }
    }

    private InstallResult RunElevatedReExecute(
        InstalledProduct product,
        ReExecuteOptions options,
        string configFilePath,
        CancellationToken cancellationToken
    )
    {
        Operation.LastElevatedError = null;
        ReportProgress(InstallStage.RunningPlugins, 0, Localize("Elevation_Starting"));
        ElevationResult elevation = ElevationHelper.RunElevatedReExecute(
            product.ProductId,
            product.InstanceId,
            options.RunPreInstall,
            options.RunPostInstall,
            configFilePath,
            onProgressLine: ReportElevatedProgressLine,
            onLongRunning: HandleElevatedLongRunning,
            cancellationToken: cancellationToken
        );

        if (elevation != ElevationResult.Succeeded)
        {
            return new InstallResult
            {
                Success = false,
                ErrorMessage = ElevationFailureMessage(
                    elevation,
                    "Plugin actions cancelled: administrator rights were denied."
                ),
                FailedStep =
                    elevation == ElevationResult.DeniedByUser ? "Elevation" : "ElevatedReExecute",
            };
        }

        return new InstallResult { Success = true };
    }

    private sealed class PluginPhaseResult
    {
        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }
    }

    private enum PluginPhase
    {
        PreInstall,
        PostInstall,
        PreUninstall,
        PostUninstall,
    }

    private async Task<PluginPhaseResult> RunPluginPhaseAsync(
        ProductManifest manifest,
        InstallOptions options,
        PluginContext context,
        PluginPhase phase,
        CancellationToken cancellationToken,
        string? extractPath = null
    )
    {
        if (manifest.Plugins is not { Length: > 0 })
            return new PluginPhaseResult { Success = true };

        foreach (StorkPluginInfo pluginInfo in manifest.Plugins)
        {
            IStorkPlugin? plugin = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // PreInstall/PostInstall: use extraction dir if available (guarantees fresh DLL)
                // Uninstall: use .stork/ under the target dir (extraction dir no longer exists)
                string metadataSearchPath = StorkPaths.ProductMetadataDir(
                    manifest.ProductId,
                    context.InstanceUniqueId
                );
                string pluginSearchPath;
                if (
                    extractPath is not null
                    && (
                        phase == PluginPhase.PreInstall
                        || phase == PluginPhase.PostInstall
                        || (
                            phase == PluginPhase.PostUninstall
                            && !File.Exists(Path.Combine(metadataSearchPath, pluginInfo.Assembly))
                        )
                    )
                )
                    pluginSearchPath = extractPath;
                else
                    pluginSearchPath = metadataSearchPath;
                plugin = LoadPlugin(pluginSearchPath, pluginInfo, Operation.PluginContexts);
                if (plugin is null)
                {
                    await LogPluginResult(
                        manifest.ProductId,
                        $"Plugin {pluginInfo.TypeName} could not be instantiated (null).",
                        false,
                        cancellationToken
                    );
                    if (phase == PluginPhase.PreInstall || phase == PluginPhase.PreUninstall)
                    {
                        return new PluginPhaseResult
                        {
                            Success = false,
                            ErrorMessage =
                                $"Plugin {pluginInfo.TypeName} could not be instantiated.",
                        };
                    }
                    continue;
                }

                if (phase == PluginPhase.PreInstall)
                {
                    if (plugin is IValidatingStorkPlugin validating)
                    {
                        IReadOnlyList<PluginValidationError> errors =
                            validating.ValidateConfiguration(context);
                        if (errors.Count > 0)
                        {
                            string errorText = string.Join(
                                "; ",
                                errors.Select(e => $"{e.FieldKey}: {e.Message}")
                            );
                            await LogPluginResult(
                                manifest.ProductId,
                                $"Validation failed ({pluginInfo.TypeName}): {errorText}",
                                false,
                                cancellationToken
                            );
                            return new PluginPhaseResult
                            {
                                Success = false,
                                ErrorMessage = errorText,
                            };
                        }
                    }

                    PluginPreInstallResult preResult = await plugin.PreInstallAsync(
                        context,
                        cancellationToken
                    );

                    if (preResult.Severity == PluginResultSeverity.Warning)
                    {
                        bool proceed = await HandlePreInstallWarningAsync(
                            manifest.ProductId,
                            pluginInfo.TypeName,
                            preResult,
                            cancellationToken
                        );
                        if (!proceed)
                        {
                            return new PluginPhaseResult
                            {
                                Success = false,
                                ErrorMessage =
                                    preResult.Message ?? "Cancelled by user after warning.",
                            };
                        }
                    }
                    else if (!preResult.Success)
                    {
                        string errorMsg = preResult.Message ?? "Pre-install check failed.";
                        if (preResult.ValidationErrors.Count > 0)
                        {
                            errorMsg +=
                                " "
                                + string.Join(
                                    "; ",
                                    preResult.ValidationErrors.Select(e =>
                                        $"{e.FieldKey}: {e.Message}"
                                    )
                                );
                        }
                        await LogPluginResult(
                            manifest.ProductId,
                            $"PreInstall failed ({pluginInfo.TypeName}): {errorMsg}",
                            false,
                            cancellationToken
                        );
                        return new PluginPhaseResult { Success = false, ErrorMessage = errorMsg };
                    }
                }
                else if (phase == PluginPhase.PostInstall)
                {
                    await plugin.PostInstallAsync(context, cancellationToken);
                }
                else if (phase == PluginPhase.PreUninstall)
                {
                    PluginPreInstallResult preResult = await plugin.PreUninstallAsync(
                        context,
                        cancellationToken
                    );
                    if (!preResult.Success)
                    {
                        string errorMsg = preResult.Message ?? "Pre-uninstall check failed.";
                        await LogPluginResult(
                            manifest.ProductId,
                            $"PreUninstall failed ({pluginInfo.TypeName}): {errorMsg}",
                            false,
                            cancellationToken
                        );
                        return new PluginPhaseResult { Success = false, ErrorMessage = errorMsg };
                    }
                }
                else if (phase == PluginPhase.PostUninstall)
                {
                    await plugin.PostUninstallAsync(context, cancellationToken);
                }

                await LogPluginResult(
                    manifest.ProductId,
                    $"{phase}: {pluginInfo.TypeName}",
                    true,
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                await LogPluginResult(
                    manifest.ProductId,
                    $"{phase} failed ({pluginInfo.TypeName}): {ex.Message}",
                    false,
                    cancellationToken
                );
                if (phase == PluginPhase.PostUninstall)
                    continue;

                return new PluginPhaseResult { Success = false, ErrorMessage = ex.Message };
            }
            finally
            {
                try
                {
                    plugin?.Cleanup();
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(
                        cleanupEx,
                        "Plugin cleanup failed for {TypeName}",
                        pluginInfo.TypeName
                    );
                }
            }
        }

        return new PluginPhaseResult { Success = true };
    }

    private async Task CopyDirectoryWithLockHandlingAsync(
        FileOperations fileOps,
        DeferredFileOps deferredOps,
        string sourceDir,
        string targetDir,
        List<string> deferredRenames,
        HashSet<string> excludedFiles,
        IProgress<int>? progress,
        CancellationToken cancellationToken
    )
    {
        DirectoryInfo source = new DirectoryInfo(sourceDir);
        FileInfo[] allFiles = source.GetFiles("*", SearchOption.AllDirectories);
        int totalFiles = allFiles.Length;
        int processedFiles = 0;

        foreach (FileInfo file in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (excludedFiles.Contains(file.FullName))
            {
                processedFiles++;
                continue;
            }

            string relativePath = Path.GetRelativePath(sourceDir, file.FullName);
            string targetPath = Path.Combine(targetDir, relativePath);

            string? targetFileDir = Path.GetDirectoryName(targetPath);
            if (targetFileDir is not null)
                Directory.CreateDirectory(targetFileDir);

            bool deferred = false;

            if (File.Exists(targetPath) && _fileLockDetector.IsFileLocked(targetPath))
            {
                if (!TryResolveLockedFile(targetPath))
                {
                    ReportProgress(
                        InstallStage.Installing,
                        0,
                        $"Warning: {Path.GetFileName(targetPath)} is in use, will be replaced on restart"
                    );
                    DeferLockedFile(
                        fileOps,
                        deferredOps,
                        deferredRenames,
                        file.FullName,
                        targetPath,
                        cancellationToken
                    );
                    deferred = true;
                }
            }

            if (!deferred)
            {
                try
                {
                    await fileOps.CopyFileAsync(file.FullName, targetPath, cancellationToken);
                }
                catch (Exception ex)
                    when (File.Exists(targetPath)
                        && (ex is IOException or UnauthorizedAccessException)
                    )
                {
                    if (!TryResolveLockedFile(targetPath))
                    {
                        ReportProgress(
                            InstallStage.Installing,
                            0,
                            $"Warning: {Path.GetFileName(targetPath)} is in use, will be replaced on restart"
                        );
                        DeferLockedFile(
                            fileOps,
                            deferredOps,
                            deferredRenames,
                            file.FullName,
                            targetPath,
                            cancellationToken
                        );
                    }
                    else
                    {
                        try
                        {
                            await fileOps.CopyFileAsync(
                                file.FullName,
                                targetPath,
                                cancellationToken
                            );
                        }
                        catch (Exception retryEx)
                            when (retryEx is IOException or UnauthorizedAccessException)
                        {
                            ReportProgress(
                                InstallStage.Installing,
                                0,
                                $"Warning: {Path.GetFileName(targetPath)} is in use, will be replaced on restart"
                            );
                            DeferLockedFile(
                                fileOps,
                                deferredOps,
                                deferredRenames,
                                file.FullName,
                                targetPath,
                                cancellationToken
                            );
                        }
                    }
                }
            }

            processedFiles++;
            int percentage =
                totalFiles > 0 ? (int)((double)processedFiles / totalFiles * 100) : 100;
            progress?.Report(percentage);
        }
    }

    private bool TryResolveLockedFile(string filePath)
    {
        if (Operation.DeferRemainingLockedFiles)
            return false;

        if (OnLockedFilesDetected is null)
            return false;

        IReadOnlyList<LockedFileInfo> lockedFiles = _fileLockDetector.GetLockedFiles([filePath]);

        if (lockedFiles.Count == 0)
            return true;

        LockedFilesAction action = OnLockedFilesDetected(
            lockedFiles,
            _fileLockDetector,
            Path.GetDirectoryName(filePath)!
        );

        if (action == LockedFilesAction.RenameAndContinue)
        {
            Operation.DeferRemainingLockedFiles = true;
            return false;
        }

        return action == LockedFilesAction.Retry && !_fileLockDetector.IsFileLocked(filePath);
    }

    private static void DeferLockedFile(
        FileOperations fileOps,
        DeferredFileOps deferredOps,
        List<string> deferredRenames,
        string sourceFile,
        string targetPath,
        CancellationToken cancellationToken
    )
    {
        string delFileName = $"DEL_{Guid.NewGuid():N}_{Path.GetFileName(targetPath)}";
        string delPath = Path.Combine(Path.GetDirectoryName(targetPath)!, delFileName);

        File.Move(targetPath, delPath);
        deferredRenames.Add(delPath);
        deferredOps.ScheduleDeleteOnReboot(delPath);
        File.Copy(sourceFile, targetPath, true);
    }

    private static void RevertDeferredRenames(List<string> deferredRenames, string targetDir)
    {
        foreach (string delPath in deferredRenames)
        {
            try
            {
                string fileName = Path.GetFileName(delPath);
                int secondUnderscore = fileName.IndexOf('_', 4);
                if (secondUnderscore >= 0 && secondUnderscore + 1 < fileName.Length)
                {
                    string originalName = fileName.Substring(secondUnderscore + 1);
                    string originalPath = Path.Combine(
                        Path.GetDirectoryName(delPath)!,
                        originalName
                    );

                    if (File.Exists(originalPath))
                        File.Delete(originalPath);

                    if (File.Exists(delPath))
                        File.Move(delPath, originalPath);
                }
            }
            catch { }
        }
    }

    private static HashSet<string> ResolveExcludedFiles(string installPath, string[]? excludeFiles)
    {
        HashSet<string> excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (excludeFiles is { Length: > 0 } && Directory.Exists(installPath))
        {
            foreach (string pattern in excludeFiles)
            {
                foreach (
                    string file in Directory.GetFiles(
                        installPath,
                        pattern,
                        SearchOption.AllDirectories
                    )
                )
                {
                    excluded.Add(file);
                }
            }
        }

        return excluded;
    }

    private async Task SaveFileManifestAsync(
        string productId,
        string uniqueId,
        string installPath,
        string[]? excludeFiles,
        string contentSourcePath,
        HashSet<string> handledFiles,
        CancellationToken cancellationToken
    )
    {
        try
        {
            string configDir = GetStorkConfigDir();
            Directory.CreateDirectory(configDir);
            string manifestPath = StorkPaths.FileManifestPath(productId, uniqueId);

            HashSet<string> excluded = ResolveExcludedFiles(installPath, excludeFiles);

            string[] sourceFiles = Directory.Exists(contentSourcePath)
                ? Directory.GetFiles(contentSourcePath, "*", SearchOption.AllDirectories)
                : Array.Empty<string>();

            List<(string RelativePath, string TargetPath)> toHash = new List<(string, string)>();
            foreach (string file in sourceFiles)
            {
                // Files claimed by plugin file handlers were not copied into the target.
                if (handledFiles.Contains(file))
                    continue;

                string relativePath = Path.GetRelativePath(contentSourcePath, file);
                string targetPath = Path.Combine(installPath, relativePath);

                if (excluded.Contains(targetPath))
                    continue;
                if (!File.Exists(targetPath))
                    continue;

                toHash.Add((relativePath, targetPath));
            }

            System.Collections.Concurrent.ConcurrentBag<TrackedFile> tracked =
                new System.Collections.Concurrent.ConcurrentBag<TrackedFile>();

            ParallelOptions parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken,
            };
            await Parallel.ForEachAsync(
                toHash,
                parallelOptions,
                async (item, ct) =>
                {
                    string sha = await FileHasher.ComputeSha256Async(item.TargetPath, ct);
                    tracked.Add(
                        new TrackedFile(
                            item.RelativePath,
                            sha,
                            new FileInfo(item.TargetPath).Length
                        )
                    );
                }
            );

            await FileManifestStore.WriteAsync(manifestPath, tracked.ToList(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save file manifest for {ProductId}", productId);
            ReportProgress(
                InstallStage.Installing,
                0,
                $"Warning: Could not save file manifest: {ex.Message}"
            );
        }
    }

    private async Task<IStorkPlugin?> DownloadAndLoadPluginAsync(
        ProductManifest manifest,
        StorkPluginInfo pluginInfo,
        CancellationToken cancellationToken,
        string? feedId = null
    )
    {
        string tempDir = Path.Combine(StorkPaths.PluginTempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            IRegistryClient registryClient = GetClientForFeed(feedId);
            string zipPath = Path.Combine(tempDir, "package.zip");
            await _downloader.DownloadAsync(
                token =>
                    registryClient.DownloadProductAsync(
                        manifest.ProductId,
                        manifest.Version,
                        token
                    ),
                zipPath,
                $"plugin package {manifest.Title} v{manifest.Version}",
                manifest.ContentSha256,
                manifest.DownloadSizeBytes,
                onProgress: null,
                cancellationToken
            );

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                ZipFile.ExtractToDirectory(zipPath, tempDir);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting plugin package in {TempDir}", tempDir);
                CleanupTempDirectory(tempDir);
                throw;
            }

            return LoadPlugin(tempDir, pluginInfo);
        }
        catch
        {
            CleanupTempDirectory(tempDir);
            throw;
        }
    }

    private void CleanupTempDirectory(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Plugin temp directory could not be deleted: {TempDir}",
                tempDir
            );
        }
    }

    private static IStorkPlugin? LoadPlugin(
        string basePath,
        StorkPluginInfo pluginInfo,
        List<ProductPluginLoadContext>? trackContexts = null
    )
    {
        string assemblyPath = Path.GetFullPath(Path.Combine(basePath, pluginInfo.Assembly));
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException(
                $"Plugin assembly not found: {pluginInfo.Assembly} (searched at: {assemblyPath})"
            );

        string pluginDir = Path.GetDirectoryName(assemblyPath)!;
        ProductPluginLoadContext loadContext = new(pluginDir);
        trackContexts?.Add(loadContext);
        System.Reflection.Assembly assembly = loadContext.LoadFromStream(
            new System.IO.MemoryStream(File.ReadAllBytes(assemblyPath))
        );

        // Use throwOnError: true to surface the actual dependency issue rather than getting null
        Type? pluginType;
        try
        {
            pluginType = assembly.GetType(pluginInfo.TypeName, throwOnError: true);
        }
        catch (Exception ex)
        {
            throw new TypeLoadException(
                $"Type '{pluginInfo.TypeName}' could not be loaded from {pluginInfo.Assembly}: {ex.Message}",
                ex
            );
        }

        if (!typeof(IStorkPlugin).IsAssignableFrom(pluginType))
            throw new InvalidCastException(
                $"{pluginInfo.TypeName} does not implement IStorkPlugin"
            );

        object? instance = Activator.CreateInstance(pluginType);
        if (instance is null)
            return null;

        if (instance is not IStorkPlugin plugin)
            throw new InvalidCastException(
                $"{pluginInfo.TypeName} could not be cast to IStorkPlugin."
            );

        return plugin;
    }

    private async Task<PluginEnvironment> BuildPluginEnvironmentAsync(
        ProductManifest manifest,
        CancellationToken cancellationToken,
        string instanceId = InstanceIdHelper.DefaultInstanceId
    )
    {
        InstalledProduct? previousInstall = await _productRepository.GetByIdAsync(
            manifest.ProductId,
            instanceId,
            cancellationToken: cancellationToken
        );
        string uniqueId = previousInstall?.InstanceUniqueId ?? string.Empty;
        Dictionary<string, string> previousValues = await LoadPluginConfigValues(
            manifest.ProductId,
            uniqueId,
            cancellationToken
        );

        return new PluginEnvironment
        {
            StorkConfigDirectory = GetStorkConfigDir(),
            PreviousVersion = previousInstall?.Version,
            PreviousConfigValues = previousValues,
        };
    }

    private async Task<bool> HandlePreInstallWarningAsync(
        string productId,
        string typeName,
        PluginPreInstallResult result,
        CancellationToken cancellationToken
    )
    {
        string message = result.Message ?? "A pre-install warning was raised.";
        await LogPluginResult(
            productId,
            $"PreInstall warning ({typeName}): {message}",
            true,
            cancellationToken
        );

        if (OnPrompt is null)
        {
            _logger.LogWarning(
                "PreInstall warning for {ProductId} accepted automatically (no prompt handler): {Message}",
                productId,
                message
            );
            return true;
        }

        PluginPrompt prompt = new PluginPrompt
        {
            Title = "Warning",
            Message = message,
            Options = { "Install anyway", "Cancel" },
            DefaultOptionIndex = 1,
        };
        PluginPromptResult response = OnPrompt(prompt);
        return response.ChosenIndex == 0;
    }

    /// <summary>
    /// Removes the previous install's tracked files and old .stork metadata during an update. Called
    /// from InstallInternalAsync after the plugin's PreInstall succeeds (not before), so a failed
    /// PreInstall leaves the existing install intact and there is nothing to roll back. Best-effort per
    /// file: a still-locked file is skipped and simply overwritten by the copy that follows.
    /// </summary>
    private async Task RemoveOldTrackedFilesAsync(
        InstalledProduct installed,
        ProductManifest newManifest,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        if (!Directory.Exists(installed.InstalledPath))
            return;

        _logger.LogDebug(
            "Removing tracked files from old installation at {InstalledPath}",
            installed.InstalledPath
        );

        List<string>? trackedFiles = await LoadFileManifest(
            installed.ProductId,
            installed.InstanceUniqueId ?? string.Empty,
            cancellationToken
        );

        if (newManifest.SharedInstallLocation)
        {
            _logger.LogInformation(
                "Shared install location for {ProductId}; overwriting in place without "
                    + "deleting existing files.",
                installed.ProductId
            );
        }
        else if (trackedFiles is not null)
        {
            HashSet<string> excluded = ResolveExcludedFiles(
                installed.InstalledPath,
                newManifest.ExcludeFiles
            );

            foreach (string relativePath in trackedFiles)
            {
                string fullPath = Path.Combine(installed.InstalledPath, relativePath);
                if (excluded.Contains(fullPath))
                {
                    continue;
                }

                try
                {
                    if (File.Exists(fullPath))
                        File.Delete(fullPath);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    _logger.LogDebug("Could not delete {File}, will overwrite", fullPath);
                }
            }
        }

        string oldStorkDir = StorkPaths.ProductMetadataDir(
            installed.ProductId,
            installed.InstanceUniqueId ?? string.Empty
        );
        try
        {
            if (Directory.Exists(oldStorkDir))
                Directory.Delete(oldStorkDir, true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Could not delete old .stork directory");
            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    0,
                    $"Warning: Could not fully delete old installation at {installed.InstalledPath}, will overwrite in place"
                )
            );
        }
    }

    private async Task<PluginContext> BuildPluginContextAsync(
        ProductManifest manifest,
        InstallOptions options,
        string instanceUniqueId,
        CancellationToken cancellationToken
    )
    {
        InstalledProduct? previousInstall = await _productRepository.GetByIdAsync(
            manifest.ProductId,
            options.InstanceId,
            cancellationToken: cancellationToken
        );
        OperationState operation = Operation;

        return new PluginContext
        {
            ProductId = manifest.ProductId,
            InstanceId = options.InstanceId,
            InstanceUniqueId = instanceUniqueId,
            Version = manifest.Version,
            PreviousVersion = previousInstall?.Version,
            InstallPath = options.TargetPath,
            StorkConfigDirectory = GetStorkConfigDir(),
            ConfigValues = options.PluginConfigValues ?? new Dictionary<string, string>(),
            ProductMetadata = manifest.Metadata is not null
                ? new Dictionary<string, string>(manifest.Metadata)
                : new Dictionary<string, string>(),
            Log = message =>
            {
                _logger.LogInformation("[Plugin] {Message}", message);
                operation.Progress?.Report(
                    new InstallProgress(InstallStage.RunningPlugins, 0, message)
                );
            },
            Prompt = OnPrompt,
            PickInstalledInstance = OnPickInstalledInstance,
            GetInstalledInstances = await CreateInstalledInstancesLookupAsync(cancellationToken),
        };
    }

    private static string GetStorkConfigDir() => StorkPaths.ConfigDir;

    private async Task SavePluginConfigValues(
        string productId,
        string uniqueId,
        Dictionary<string, string>? values,
        CancellationToken cancellationToken
    )
    {
        if (values is null || values.Count == 0)
            return;

        string configDir = GetStorkConfigDir();
        Directory.CreateDirectory(configDir);
        string filePath = StorkPaths.InstancePluginConfigPath(productId, uniqueId);
        string json = System.Text.Json.JsonSerializer.Serialize(
            values,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
        );
        await SafeFileWriter.WriteAtomicWithBackupAsync(filePath, json, cancellationToken);
    }

    private static async Task<List<string>?> LoadFileManifest(
        string productId,
        string uniqueId,
        CancellationToken cancellationToken
    )
    {
        string path = StorkPaths.FileManifestPath(productId, uniqueId);
        if (!File.Exists(path))
        {
            string legacyPath = StorkPaths.LegacyFileManifestPath(productId);
            if (File.Exists(legacyPath))
                path = legacyPath;
            else
                return null;
        }

        return await FileManifestStore.ReadPathsAsync(path, cancellationToken);
    }

    private void ReportProgress(InstallStage stage, int percentage, string message)
    {
        _operation.Value?.Progress?.Report(new InstallProgress(stage, percentage, message));
    }

    /// <summary>
    /// Forwards a log line emitted by the elevated child process to the install UI, stripping the
    /// leading Serilog timestamp+level prefix (e.g. "2026-08-13 11:45:16.012 +02:00 [INF] ") so the
    /// UI shows clean status text. Falls back to the raw line if parsing fails.
    /// </summary>
    private void ReportElevatedProgressLine(string line)
    {
        string message = StripLogPrefix(line);
        if (LooksLikeError(line))
            Operation.LastElevatedError = message;
        ReportProgress(InstallStage.Installing, 0, message);
    }

    private static bool LooksLikeError(string rawLine) =>
        rawLine.Contains("[ERR]", StringComparison.Ordinal)
        || rawLine.Contains("denied", StringComparison.OrdinalIgnoreCase)
        || rawLine.Contains("failed", StringComparison.OrdinalIgnoreCase)
        || rawLine.Contains("exception", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the result for an elevated child that did not succeed. A genuine refusal
    /// (<see cref="ElevationResult.DeniedByUser"/>) keeps the "administrator rights were denied"
    /// wording; a child that started elevated but failed inside surfaces its real error instead of
    /// being mislabelled as a refusal.
    /// </summary>
    private string ElevationFailureMessage(ElevationResult elevation, string deniedMessage)
    {
        if (elevation == ElevationResult.DeniedByUser)
            return deniedMessage;

        string? lastError = Operation.LastElevatedError;
        return string.IsNullOrEmpty(lastError)
            ? "The elevated step failed. See the log for details."
            : $"The elevated step failed: {lastError}";
    }

    /// <summary>
    /// Removes the Serilog line prefix up to and including the "[LVL] " level token, leaving the
    /// message (and any embedded context such as "[Plugin] ..."). Returns the input unchanged when
    /// no recognizable prefix is present.
    /// </summary>
    private static string StripLogPrefix(string line)
    {
        // Serilog default: "<timestamp> [LVL] <message>". Cut after the first "] " that follows a
        // short bracketed level token near the start of the line.
        int levelClose = line.IndexOf("] ", StringComparison.Ordinal);
        if (levelClose > 0 && levelClose < 80)
        {
            int levelOpen = line.LastIndexOf('[', levelClose);
            if (levelOpen >= 0 && levelClose - levelOpen <= 6)
                return line[(levelClose + 2)..].TrimStart();
        }

        return line;
    }

    /// <summary>
    /// Builds and shows the "installation is taking longer than expected" prompt via <see cref="OnPrompt"/>.
    /// Returns true when the user chooses to keep waiting or when no prompt handler is registered
    /// (headless: a running install is never aborted because of elapsed time); false on cancel/dismiss.
    /// </summary>
    internal bool HandleElevatedLongRunning(string currentStep)
    {
        if (OnPrompt is null)
            return true;

        string step = string.IsNullOrWhiteSpace(currentStep)
            ? string.Empty
            : StripLogPrefix(currentStep);

        PluginPrompt prompt = new PluginPrompt
        {
            Title = Localize("Elevation_TakingLong_Title"),
            Message = Localize("Elevation_TakingLong_Message", step),
            Options = { Localize("Elevation_WaitMore"), Localize("Elevation_Cancel") },
            DefaultOptionIndex = 0,
        };

        PluginPromptResult response = OnPrompt(prompt);
        return response.ChosenIndex == 0;
    }

    public Task<Dictionary<string, string>> LoadSavedPluginConfigAsync(
        string productId,
        string instanceUniqueId,
        CancellationToken cancellationToken = default
    ) => LoadPluginConfigValues(productId, instanceUniqueId, cancellationToken);

    private async Task<Dictionary<string, string>> LoadPluginConfigValues(
        string productId,
        string uniqueId,
        CancellationToken cancellationToken
    )
    {
        string filePath = StorkPaths.InstancePluginConfigPath(productId, uniqueId);
        if (!File.Exists(filePath))
        {
            string legacyPath = StorkPaths.LegacyPluginConfigPath(productId);
            if (File.Exists(legacyPath))
                filePath = legacyPath;
            else
                return new Dictionary<string, string>();
        }

        string? json = await AtomicFile.ReadAllTextAsync(
            filePath,
            cancellationToken: cancellationToken
        );
        return json is null
            ? new Dictionary<string, string>()
            : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>();
    }

    private async Task LogPluginResult(
        string productId,
        string details,
        bool success,
        CancellationToken cancellationToken
    )
    {
        if (success)
            _logger.LogInformation("Plugin result for {ProductId}: {Details}", productId, details);
        else
            _logger.LogWarning("Plugin result for {ProductId}: {Details}", productId, details);

        ReportProgress(InstallStage.RunningPlugins, 0, success ? details : $"Warning: {details}");

        await _activityLog.LogAsync(
            new ActivityLogEntry(
                Id: Guid.NewGuid().ToString(),
                Timestamp: DateTime.UtcNow,
                Action: "Plugin",
                ProductId: productId,
                Details: details,
                Success: success
            ),
            cancellationToken
        );
    }

    private async Task ApplyEnvironmentVariablesAsync(
        ProductManifest manifest,
        string uniqueId,
        string installPath,
        CancellationToken cancellationToken
    )
    {
        if (manifest.EnvironmentVariables is not { Length: > 0 })
            return;

        try
        {
            await _envVarService.ApplyTrackedAsync(
                manifest.ProductId,
                uniqueId,
                manifest.EnvironmentVariables,
                installPath,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Environment variables could not be applied");
            ReportProgress(
                InstallStage.Installing,
                0,
                $"Warning: Environment variables could not be applied: {ex.Message}"
            );
        }
    }

    private void CreateShortcuts(ProductManifest manifest, string installPath)
    {
        if (manifest.Shortcuts is not { Length: > 0 })
            return;

        try
        {
            string shortcutFolderName = manifest.ShortcutFolder ?? "StorkDrop";
            string startMenuFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                "Programs",
                shortcutFolderName
            );
            Directory.CreateDirectory(startMenuFolder);

            foreach (ShortcutInfo shortcut in manifest.Shortcuts)
            {
                string exePath = Path.Combine(installPath, shortcut.ExeName);
                if (!File.Exists(exePath))
                    continue;

                string linkPath = Path.Combine(startMenuFolder, shortcut.DisplayName + ".lnk");

                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null)
                    continue;

                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic link = shell.CreateShortcut(linkPath);
                link.TargetPath = exePath;
                link.WorkingDirectory = installPath;
                link.Description = shortcut.DisplayName;

                if (shortcut.IconPath is not null)
                {
                    string iconFullPath = Path.Combine(installPath, shortcut.IconPath);
                    if (File.Exists(iconFullPath))
                        link.IconLocation = iconFullPath;
                }

                link.Save();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shortcuts could not be created");
            ReportProgress(
                InstallStage.Installing,
                0,
                $"Warning: Shortcuts could not be created: {ex.Message}"
            );
        }
    }
}
