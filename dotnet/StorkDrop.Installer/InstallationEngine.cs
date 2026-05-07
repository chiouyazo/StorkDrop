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
    private readonly List<IFileTypeHandler> _fileTypeHandlers;
    private IProgress<InstallProgress>? _currentProgress;

    /// <inheritdoc />
    /// <inheritdoc />
    public InstallPathResolverCallback? OnResolveInstallPath { get; set; }

    /// <inheritdoc />
    public FileHandlerConfigCallback? OnFileHandlerConfigNeeded { get; set; }

    /// <inheritdoc />
    public FileHandlerConfigCallback? OnPluginConfigNeeded { get; set; }

    public ActionGroupConfigCallback? OnActionGroupConfigNeeded { get; set; }
    public LockedFilesCallback? OnLockedFilesDetected { get; set; }
    public IInteractiveStorkPlugin? CurrentInteractivePlugin { get; private set; }

    private static readonly JsonSerializerOptions FileManifestJsonOptions =
        new JsonSerializerOptions { WriteIndented = true };

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
        ILogger<InstallationEngine> logger
    )
    {
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
        _logger = logger;

        _logger.LogInformation(
            "InstallationEngine initialized with {PluginCount} plugins, {HandlerCount} file type handlers",
            plugins.Count(),
            _fileTypeHandlers.Count
        );
    }

    private IRegistryClient GetClientForFeed(string? feedId)
    {
        if (string.IsNullOrEmpty(feedId))
        {
            IReadOnlyList<FeedInfo> feeds = _feedRegistry.GetFeeds();
            if (feeds.Count == 0)
                throw new InvalidOperationException("No feeds configured.");
            return _feedRegistry.GetClient(feeds[0].Id);
        }
        return _feedRegistry.GetClient(feedId);
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
                IStorkPlugin? plugin = LoadPlugin(extractPath, pluginInfo, _activePluginContexts);
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
        _currentProgress = progress;
        _logger.LogInformation(
            "Starting installation of {ProductId} v{Version} to {TargetPath}",
            manifest.ProductId,
            manifest.Version,
            options.TargetPath
        );

        InstallResult? elevationResult = await TryElevateInstallAsync(
            manifest,
            options,
            options.TargetPath,
            progress,
            cancellationToken
        );
        if (elevationResult is not null)
            return elevationResult;

        string tempDir = Path.Combine(StorkPaths.TempDir, Guid.NewGuid().ToString());

        try
        {
            Directory.CreateDirectory(tempDir);

            cancellationToken.ThrowIfCancellationRequested();
            (string? extractPath, string? productContentPath) = await DownloadAndExtractAsync(
                manifest,
                options,
                tempDir,
                progress,
                cancellationToken
            );
            if (extractPath is null)
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = "Extraction failed.",
                    FailedStep = "Extracting",
                };

            string copySourcePath = productContentPath ?? extractPath;

            cancellationToken.ThrowIfCancellationRequested();
            (
                HashSet<string> handledFiles,
                PluginContext? fileHandlerContext,
                InstallResult? fileHandlerError
            ) = await HandleFileTypesAsync(
                manifest,
                options,
                extractPath,
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
                        options.InstanceId,
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
                        _activePluginContexts
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

            cancellationToken.ThrowIfCancellationRequested();
            PluginContext pluginContext = await BuildPluginContextAsync(
                manifest,
                options,
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

            (string resolvedPath, InstallResult? pathError) =
                await ResolveAndValidateTargetPathAsync(
                    manifest,
                    options,
                    fileHandlerContext,
                    progress,
                    cancellationToken
                );
            if (pathError is not null)
                return pathError;

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

            InstallResult? copyError = await CopyFilesToTargetAsync(
                copySourcePath,
                resolvedPath,
                handledFiles,
                progress,
                cancellationToken
            );
            if (copyError is not null)
                return copyError;

            // Unload plugin contexts from PreInstall phase before copying to .stork/
            // (the DLLs may already exist from a previous install and be locked)
            foreach (ProductPluginLoadContext ctx in _activePluginContexts)
            {
                try
                {
                    ctx.Unload();
                }
                catch { }
            }
            _activePluginContexts.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Copy plugin directory to .stork/plugins/ and save manifest for uninstall
            string pluginsExtractDir = Path.Combine(extractPath, "plugins");
            if (Directory.Exists(pluginsExtractDir))
            {
                string storkPluginsDir = Path.Combine(resolvedPath, ".stork", "plugins");
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
                string storkFilesDir = Path.Combine(resolvedPath, ".stork", "files");
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
                string storkDir = Path.Combine(resolvedPath, ".stork");
                Directory.CreateDirectory(storkDir);
                string manifestJson = System.Text.Json.JsonSerializer.Serialize(
                    manifest,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                );
                await File.WriteAllTextAsync(
                    Path.Combine(storkDir, "manifest.json"),
                    manifestJson,
                    cancellationToken
                );
                _logger.LogDebug("Saved product manifest to .stork/manifest.json");
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
                return verifyError;

            await FinalizeInstallationAsync(
                manifest,
                options,
                resolvedPath,
                pluginContext,
                extractPath,
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
            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    0,
                    "Installation cancelled. Cleaning up..."
                )
            );
            _logger.LogWarning("Installation of {ProductId} cancelled by user", manifest.ProductId);
            return new InstallResult
            {
                Success = false,
                ErrorMessage = "Installation cancelled by user.",
                FailedStep = "Cancelled",
            };
        }
        catch (Exception ex)
        {
            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    0,
                    $"Installation failed: {ex}. Cleaning up..."
                )
            );
            _logger.LogError(
                ex,
                "Installation failed for {ProductId}: {Error}",
                manifest.ProductId,
                ex.Message
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

    private async Task<InstallResult?> TryElevateInstallAsync(
        ProductManifest manifest,
        InstallOptions options,
        string targetPath,
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
            if (options.PluginConfigValues is { Count: > 0 })
            {
                configFilePath = Path.Combine(
                    StorkPaths.TempDir,
                    $"elevation-config-{Guid.NewGuid()}.json"
                );
                Directory.CreateDirectory(StorkPaths.TempDir);
                string json = System.Text.Json.JsonSerializer.Serialize(options.PluginConfigValues);
                await File.WriteAllTextAsync(configFilePath, json, cancellationToken);
                _logger.LogDebug(
                    "Saved plugin config to {Path} for elevated process",
                    configFilePath
                );
            }

            bool elevated = ElevationHelper.RunElevatedInstall(
                manifest.ProductId,
                manifest.Version,
                targetPath,
                options.FeedId ?? _feedRegistry.GetFeeds()[0].Id,
                options.InstanceId,
                configFilePath
            );

            if (!elevated)
            {
                _logger.LogWarning("Elevation denied by user for {ProductId}", manifest.ProductId);
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        0,
                        $"Warning: Elevation denied by user for {manifest.ProductId}"
                    )
                );
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = "Installation cancelled: administrator rights were denied.",
                    FailedStep = "Elevation",
                };
            }

            await RegisterElevatedInstallAsync(
                manifest,
                targetPath,
                options.FeedId,
                "(elevated)",
                cancellationToken,
                options.InstanceId
            );
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
        string instanceId = InstanceIdHelper.DefaultInstanceId
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
            BadgeColor: manifest.BadgeColor
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

    private async Task<(string? extractPath, string? productContentPath)> DownloadAndExtractAsync(
        ProductManifest manifest,
        InstallOptions options,
        string tempDir,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        _logger.LogInformation(
            "Downloading product {ProductId} v{Version}",
            manifest.ProductId,
            manifest.Version
        );
        progress.Report(
            new InstallProgress(
                InstallStage.Downloading,
                0,
                $"Downloading {manifest.Title} v{manifest.Version}..."
            )
        );

        IRegistryClient registryClient = GetClientForFeed(options.FeedId);
        using Stream downloadStream = await registryClient.DownloadProductAsync(
            manifest.ProductId,
            manifest.Version,
            cancellationToken
        );
        string zipPath = Path.Combine(tempDir, $"{manifest.ProductId}-{manifest.Version}.zip");
        await using (FileStream fileStream = File.Create(zipPath))
        {
            await downloadStream.CopyToAsync(fileStream, cancellationToken);
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
            ZipFile.ExtractToDirectory(zipPath, extractPath);
            string? productContentPath = ExtractInnerZipIfPresent(tempDir, extractPath, progress);

            progress.Report(
                new InstallProgress(InstallStage.Extracting, 100, "Extraction complete.")
            );
            _logger.LogInformation("Extraction complete for {ProductId}", manifest.ProductId);

            // If two-layer: extractPath has loose files (plugins, SIDs), productContentPath has product binaries
            // If single-layer: extractPath has everything, productContentPath is null
            return (extractPath, productContentPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Installation failed at step Extracting: {Error}", ex.Message);
            progress.Report(
                new InstallProgress(InstallStage.Extracting, 0, $"Extraction failed: {ex}")
            );
            return (null, null);
        }
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

        fileHandlerContext = await BuildPluginContextAsync(manifest, options, cancellationToken);

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
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        // Resolve built-in {StorkPath} template (points to StorkDrop's own install directory)
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
                if (options.PluginConfigValues is { Count: > 0 })
                {
                    configFilePath = Path.Combine(
                        StorkPaths.TempDir,
                        $"elevation-config-{Guid.NewGuid()}.json"
                    );
                    Directory.CreateDirectory(StorkPaths.TempDir);
                    string configJson = System.Text.Json.JsonSerializer.Serialize(
                        options.PluginConfigValues
                    );
                    await File.WriteAllTextAsync(configFilePath, configJson, cancellationToken);
                }

                bool elevated = ElevationHelper.RunElevatedInstall(
                    manifest.ProductId,
                    manifest.Version,
                    resolvedTargetPath,
                    options.FeedId ?? _feedRegistry.GetFeeds()[0].Id,
                    options.InstanceId,
                    configFilePath
                );
                if (!elevated)
                    return (
                        resolvedTargetPath,
                        new InstallResult
                        {
                            Success = false,
                            ErrorMessage =
                                "Installation cancelled: administrator rights were denied.",
                            FailedStep = "Elevation",
                        }
                    );
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
                options.InstanceId
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
            options.InstanceId,
            resolvedPath,
            cancellationToken
        );

        await SavePluginConfigValues(
            manifest.ProductId,
            options.InstanceId,
            options.PluginConfigValues,
            cancellationToken
        );

        progress.Report(
            new InstallProgress(InstallStage.Installing, 90, "Saving file manifest...")
        );
        await SaveFileManifestAsync(
            manifest.ProductId,
            options.InstanceId,
            resolvedPath,
            cancellationToken
        );

        progress.Report(new InstallProgress(InstallStage.Installing, 95, "Registering product..."));
        await RegisterElevatedInstallAsync(
            manifest,
            resolvedPath,
            options.FeedId,
            "",
            cancellationToken,
            options.InstanceId
        );
    }

    private void CleanupTempDirectory(string tempDir, IProgress<InstallProgress>? progress = null)
    {
        try
        {
            if (!Directory.Exists(tempDir))
                return;

            foreach (ProductPluginLoadContext ctx in _activePluginContexts)
            {
                try
                {
                    ctx.Unload();
                }
                catch { }
            }
            _activePluginContexts.Clear();
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
        IReadOnlyList<LockedFileInfo> lockedFiles = _fileLockDetector.GetLockedFiles(
            installed.InstalledPath
        );
        if (lockedFiles.Count > 0)
        {
            if (OnLockedFilesDetected is not null)
            {
                OnLockedFilesDetected(lockedFiles, _fileLockDetector, installed.InstalledPath);
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
        if (options.CreateBackup && Directory.Exists(installed.InstalledPath))
        {
            _logger.LogInformation(
                "Creating backup of {ProductId} before update",
                installed.ProductId
            );
            backupPath = await _backupService.CreateBackupAsync(
                installed.ProductId,
                installed.InstalledPath,
                cancellationToken
            );
        }

        try
        {
            _logger.LogDebug(
                "Removing old environment variables for {ProductId}",
                installed.ProductId
            );
            try
            {
                List<AppliedEnvironmentVariable> oldEnvVars = await _envVarService.LoadAppliedAsync(
                    installed.ProductId,
                    installed.InstanceId,
                    cancellationToken
                );
                if (oldEnvVars.Count > 0)
                    await _envVarService.RemoveAsync(oldEnvVars);
                _envVarService.DeleteTracking(installed.ProductId, installed.InstanceId);
            }
            catch
            {
                // Best-effort
            }

            if (Directory.Exists(installed.InstalledPath))
            {
                _logger.LogDebug(
                    "Removing tracked files from old installation at {InstalledPath}",
                    installed.InstalledPath
                );

                List<string>? trackedFiles = await LoadFileManifest(
                    installed.ProductId,
                    installed.InstanceId,
                    cancellationToken
                );

                if (trackedFiles is not null)
                {
                    foreach (string relativePath in trackedFiles)
                    {
                        string fullPath = Path.Combine(installed.InstalledPath, relativePath);
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

                string oldStorkDir = Path.Combine(installed.InstalledPath, ".stork");
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

            _logger.LogDebug(
                "Running InstallAsync for new version {NewVersion}",
                newManifest.Version
            );
            InstallResult result = await InstallAsync(
                newManifest,
                options,
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
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Update failed for {ProductId}, restoring backup",
                installed.ProductId
            );
            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    0,
                    $"Update failed for {installed.ProductId}, restoring backup"
                )
            );
            if (backupPath is not null)
                await _backupService.RestoreBackupAsync(
                    backupPath,
                    installed.InstalledPath,
                    cancellationToken
                );
            throw;
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
        _currentProgress = progress;

        try
        {
            ReportProgress(
                InstallStage.RunningPlugins,
                0,
                $"Loading plugin data for {product.Title}..."
            );

            string storkDir = Path.Combine(product.InstalledPath, ".stork");
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
                            _activePluginContexts
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
                            Version = product.Version,
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
                product.InstanceId,
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
                    Version = product.Version,
                    InstallPath = product.InstalledPath,
                    StorkConfigDirectory = GetStorkConfigDir(),
                    ConfigValues = effectiveConfig,
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
                product.InstanceId,
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
            foreach (ProductPluginLoadContext ctx in _activePluginContexts)
            {
                try
                {
                    ctx.Unload();
                }
                catch { }
            }
            _activePluginContexts.Clear();
            CurrentInteractivePlugin = null;
            _currentProgress = null;
        }
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
                string pluginSearchPath;
                if (
                    extractPath is not null
                    && (phase == PluginPhase.PreInstall || phase == PluginPhase.PostInstall)
                )
                    pluginSearchPath = extractPath;
                else
                    pluginSearchPath = Path.Combine(options.TargetPath, ".stork");
                plugin = LoadPlugin(pluginSearchPath, pluginInfo, _activePluginContexts);
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
                    if (!preResult.Success)
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
                        await fileOps.CopyFileAsync(file.FullName, targetPath, cancellationToken);
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
        if (OnLockedFilesDetected is null)
            return false;

        IReadOnlyList<LockedFileInfo> lockedFiles = _fileLockDetector.GetLockedFiles(
            Path.GetDirectoryName(filePath)!
        );

        if (lockedFiles.Count == 0)
            return true;

        LockedFilesAction action = OnLockedFilesDetected(
            lockedFiles,
            _fileLockDetector,
            Path.GetDirectoryName(filePath)!
        );

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
        string pendingFileName = $"NEW_{Guid.NewGuid():N}_{Path.GetFileName(targetPath)}";
        string pendingPath = Path.Combine(Path.GetDirectoryName(targetPath)!, pendingFileName);

        File.Copy(sourceFile, pendingPath, true);
        deferredRenames.Add(pendingPath);
        deferredOps.ScheduleMoveOnReboot(pendingPath, targetPath);
    }

    private static void RevertDeferredRenames(List<string> deferredRenames, string targetDir)
    {
        foreach (string pendingPath in deferredRenames)
        {
            try
            {
                if (File.Exists(pendingPath))
                    File.Delete(pendingPath);
            }
            catch
            {
                // Best-effort revert
            }
        }
    }

    private async Task SaveFileManifestAsync(
        string productId,
        string instanceId,
        string installPath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            string configDir = GetStorkConfigDir();
            Directory.CreateDirectory(configDir);
            string manifestPath = StorkPaths.FileManifestPath(productId, instanceId);

            string[] allFiles = Directory.GetFiles(installPath, "*", SearchOption.AllDirectories);
            List<string> relativePaths = new List<string>();
            foreach (string file in allFiles)
            {
                relativePaths.Add(Path.GetRelativePath(installPath, file));
            }

            string json = JsonSerializer.Serialize(relativePaths, FileManifestJsonOptions);
            await File.WriteAllTextAsync(manifestPath, json, cancellationToken);
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
            using Stream downloadStream = await registryClient.DownloadProductAsync(
                manifest.ProductId,
                manifest.Version,
                cancellationToken
            );
            string zipPath = Path.Combine(tempDir, "package.zip");
            await using (FileStream fileStream = File.Create(zipPath))
            {
                await downloadStream.CopyToAsync(fileStream, cancellationToken);
            }

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

    private readonly List<ProductPluginLoadContext> _activePluginContexts = [];

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
        System.Reflection.Assembly assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

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
        Dictionary<string, string> previousValues = await LoadPluginConfigValues(
            manifest.ProductId,
            instanceId,
            cancellationToken
        );
        InstalledProduct? previousInstall = await _productRepository.GetByIdAsync(
            manifest.ProductId,
            instanceId,
            cancellationToken: cancellationToken
        );

        return new PluginEnvironment
        {
            StorkConfigDirectory = GetStorkConfigDir(),
            PreviousVersion = previousInstall?.Version,
            PreviousConfigValues = previousValues,
        };
    }

    private async Task<PluginContext> BuildPluginContextAsync(
        ProductManifest manifest,
        InstallOptions options,
        CancellationToken cancellationToken
    )
    {
        return new PluginContext
        {
            ProductId = manifest.ProductId,
            InstanceId = options.InstanceId,
            Version = manifest.Version,
            InstallPath = options.TargetPath,
            StorkConfigDirectory = GetStorkConfigDir(),
            ConfigValues = options.PluginConfigValues ?? new Dictionary<string, string>(),
            Log = message =>
            {
                _logger.LogInformation("[Plugin] {Message}", message);
                _currentProgress?.Report(
                    new InstallProgress(InstallStage.RunningPlugins, 0, message)
                );
            },
        };
    }

    private static string GetStorkConfigDir() => StorkPaths.ConfigDir;

    private async Task SavePluginConfigValues(
        string productId,
        string instanceId,
        Dictionary<string, string>? values,
        CancellationToken cancellationToken
    )
    {
        if (values is null || values.Count == 0)
            return;

        string configDir = GetStorkConfigDir();
        Directory.CreateDirectory(configDir);
        string filePath = StorkPaths.InstancePluginConfigPath(productId, instanceId);
        string json = System.Text.Json.JsonSerializer.Serialize(
            values,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
        );
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private static async Task<List<string>?> LoadFileManifest(
        string productId,
        string instanceId,
        CancellationToken cancellationToken
    )
    {
        string path = StorkPaths.FileManifestPath(productId, instanceId);
        if (!File.Exists(path))
        {
            string legacyPath = StorkPaths.LegacyFileManifestPath(productId);
            if (File.Exists(legacyPath))
                path = legacyPath;
            else
                return null;
        }

        string json = await File.ReadAllTextAsync(path, cancellationToken);
        return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
    }

    private void ReportProgress(InstallStage stage, int percentage, string message)
    {
        _currentProgress?.Report(new InstallProgress(stage, percentage, message));
    }

    private async Task<Dictionary<string, string>> LoadPluginConfigValues(
        string productId,
        string instanceId,
        CancellationToken cancellationToken
    )
    {
        string filePath = StorkPaths.InstancePluginConfigPath(productId, instanceId);
        if (!File.Exists(filePath))
        {
            string legacyPath = StorkPaths.LegacyPluginConfigPath(productId);
            if (File.Exists(legacyPath))
                filePath = legacyPath;
            else
                return new Dictionary<string, string>();
        }

        string json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
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
        string instanceId,
        string installPath,
        CancellationToken cancellationToken
    )
    {
        if (manifest.EnvironmentVariables is not { Length: > 0 })
            return;

        try
        {
            List<AppliedEnvironmentVariable> applied = await _envVarService.ApplyAsync(
                manifest.EnvironmentVariables,
                installPath
            );
            await _envVarService.SaveAppliedAsync(
                manifest.ProductId,
                instanceId,
                applied,
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
