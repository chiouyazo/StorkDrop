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

    /// <inheritdoc />
    /// <inheritdoc />
    public InstallPathResolverCallback? OnResolveInstallPath { get; set; }

    /// <inheritdoc />
    public FileHandlerConfigCallback? OnFileHandlerConfigNeeded { get; set; }

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
        CancellationToken cancellationToken = default
    )
    {
        if (manifest.Plugins is not { Length: > 0 })
            return Array.Empty<PluginConfigField>();

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
                    cancellationToken
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
                allFields.AddRange(fields);
            }
            catch (Exception ex)
            {
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

    /// <inheritdoc />
    public async Task<InstallResult> InstallAsync(
        ProductManifest manifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation(
            "Starting installation of {ProductId} v{Version} to {TargetPath}",
            manifest.ProductId,
            manifest.Version,
            options.TargetPath
        );
        string tempDir = Path.Combine(Path.GetTempPath(), "StorkDrop", Guid.NewGuid().ToString());

        try
        {
            if (options.TargetPath.Contains('{'))
            {
                _logger.LogInformation(
                    "Target path contains template variables, deferring elevation check until after resolution"
                );
            }
            else if (
                ElevationHelper.PathRequiresAdmin(options.TargetPath)
                && !ElevationHelper.IsRunningAsAdmin()
            )
            {
                _logger.LogInformation(
                    "Elevation required, spawning elevated process for {ProductId}",
                    manifest.ProductId
                );
                bool elevated = ElevationHelper.RunElevatedInstall(
                    manifest.ProductId,
                    manifest.Version,
                    options.TargetPath,
                    options.FeedId ?? _feedRegistry.GetFeeds()[0].Id
                );
                if (!elevated)
                {
                    _logger.LogWarning(
                        "Elevation denied by user for {ProductId}",
                        manifest.ProductId
                    );
                    return new InstallResult
                    {
                        Success = false,
                        ErrorMessage = "Installation cancelled: administrator rights were denied.",
                        FailedStep = "Elevation",
                    };
                }

                InstalledProduct elevatedProduct = new InstalledProduct(
                    ProductId: manifest.ProductId,
                    Title: manifest.Title,
                    Version: manifest.Version,
                    InstalledPath: options.TargetPath,
                    InstalledDate: DateTime.UtcNow,
                    FeedId: options.FeedId,
                    BackupPath: null
                );
                await _productRepository.AddAsync(elevatedProduct, cancellationToken);

                await _activityLog.LogAsync(
                    new ActivityLogEntry(
                        Id: Guid.NewGuid().ToString(),
                        Timestamp: DateTime.UtcNow,
                        Action: "Install",
                        ProductId: manifest.ProductId,
                        Details: $"{manifest.Title} v{manifest.Version} installed to {options.TargetPath} (elevated)",
                        Success: true
                    ),
                    cancellationToken
                );

                return new InstallResult { Success = true };
            }

            Directory.CreateDirectory(tempDir);

            cancellationToken.ThrowIfCancellationRequested();

            // Step: Download
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
            progress.Report(
                new InstallProgress(InstallStage.Downloading, 100, "Download complete.")
            );
            _logger.LogInformation(
                "Download complete for {ProductId} v{Version}, extracting to {TempDir}",
                manifest.ProductId,
                manifest.Version,
                tempDir
            );

            cancellationToken.ThrowIfCancellationRequested();

            // Step: Extract
            progress.Report(new InstallProgress(InstallStage.Extracting, 0, "Extracting files..."));
            string extractPath = Path.Combine(tempDir, "extracted");
            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractPath);

                // Check for inner ZIP: if the extracted contents contain a single .zip file,
                // extract it as the actual product contents (two-layer packaging)
                string[] innerZips = Directory.GetFiles(extractPath, "*.zip");
                if (innerZips.Length == 1)
                {
                    string innerZipPath = innerZips[0];
                    string innerExtractPath = Path.Combine(tempDir, "inner-extracted");
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
                    ZipFile.ExtractToDirectory(innerZipPath, innerExtractPath);

                    // Remove the inner ZIP from the outer extraction so it's not copied/handled
                    File.Delete(innerZipPath);

                    // Move inner contents into the extract path
                    foreach (
                        string file in Directory.GetFiles(
                            innerExtractPath,
                            "*",
                            SearchOption.AllDirectories
                        )
                    )
                    {
                        string relativePath = Path.GetRelativePath(innerExtractPath, file);
                        string targetFile = Path.Combine(extractPath, relativePath);
                        string? targetDir = Path.GetDirectoryName(targetFile);
                        if (targetDir is not null)
                            Directory.CreateDirectory(targetDir);
                        File.Move(file, targetFile, overwrite: true);
                    }

                    Directory.Delete(innerExtractPath, true);
                    _logger.LogInformation("Inner ZIP extracted successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Installation failed at step Extracting: {Error}", ex.Message);
                CleanupTempDirectory(tempDir);
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = "Extraction failed: " + ex.Message,
                    FailedStep = "Extracting",
                    Exception = ex,
                };
            }
            progress.Report(
                new InstallProgress(InstallStage.Extracting, 100, "Extraction complete.")
            );
            _logger.LogInformation(
                "Extraction complete, running pre-install plugins for {ProductId}",
                manifest.ProductId
            );

            cancellationToken.ThrowIfCancellationRequested();

            // Step: PreInstall plugins
            PluginContext pluginContext = await BuildPluginContextAsync(
                manifest,
                options,
                cancellationToken
            );
            PluginPhaseResult preInstallPhaseResult = await RunPluginPhaseAsync(
                manifest,
                options,
                pluginContext,
                PluginPhase.PreInstall,
                cancellationToken
            );
            if (!preInstallPhaseResult.Success)
            {
                _logger.LogError(
                    "Installation failed at step PreInstall: {Error}",
                    preInstallPhaseResult.ErrorMessage
                );
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = preInstallPhaseResult.ErrorMessage,
                    FailedStep = "PreInstall",
                };
            }
            _logger.LogInformation(
                "Pre-install complete, handling custom file types for {ProductId}",
                manifest.ProductId
            );

            cancellationToken.ThrowIfCancellationRequested();

            // Step: Handle custom file types (plugins claim files before copy)
            HashSet<string> handledFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PluginContext? fileHandlerContext = null;

            _logger.LogDebug(
                "SkipFileHandlers={Skip}, FileTypeHandlers={Count}",
                options.SkipFileHandlers,
                _fileTypeHandlers.Count
            );

            // When skipping file handlers (elevated process), still exclude their file extensions
            if (options.SkipFileHandlers && _fileTypeHandlers.Count > 0)
            {
                foreach (IFileTypeHandler handler in _fileTypeHandlers)
                {
                    foreach (string ext in handler.HandledExtensions)
                    {
                        foreach (
                            string file in Directory.GetFiles(
                                extractPath,
                                "*" + ext,
                                SearchOption.AllDirectories
                            )
                        )
                        {
                            handledFiles.Add(file);
                        }
                    }
                }
                _logger.LogInformation(
                    "Elevated install: excluded {Count} plugin-handled files from copy",
                    handledFiles.Count
                );
            }

            if (_fileTypeHandlers.Count > 0 && !options.SkipFileHandlers)
            {
                fileHandlerContext = await BuildPluginContextAsync(
                    manifest,
                    options,
                    cancellationToken
                );

                foreach (IFileTypeHandler handler in _fileTypeHandlers)
                {
                    List<string> matchingFiles = new List<string>();
                    foreach (string ext in handler.HandledExtensions)
                    {
                        foreach (
                            string file in Directory.GetFiles(
                                extractPath,
                                "*" + ext,
                                SearchOption.AllDirectories
                            )
                        )
                        {
                            matchingFiles.Add(file);
                        }
                    }

                    if (matchingFiles.Count > 0)
                    {
                        string fileList = string.Join(", ", matchingFiles.Select(Path.GetFileName));
                        _logger.LogInformation(
                            "File handler claims {Count} files with extensions {Extensions}: {Files}",
                            matchingFiles.Count,
                            string.Join(", ", handler.HandledExtensions),
                            fileList
                        );
                        progress.Report(
                            new InstallProgress(
                                InstallStage.RunningPlugins,
                                0,
                                $"Plugin found {matchingFiles.Count} file(s): {fileList}"
                            )
                        );

                        // Ask handler if it needs user config for these files
                        IReadOnlyList<PluginConfigField> handlerFields =
                            handler.GetFileHandlerConfig(matchingFiles, fileHandlerContext);

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
                                progress.Report(
                                    new InstallProgress(
                                        InstallStage.RunningPlugins,
                                        0,
                                        "Installation cancelled by user."
                                    )
                                );
                                return new InstallResult
                                {
                                    Success = false,
                                    ErrorMessage = "Installation cancelled by user.",
                                    FailedStep = "FileHandlerConfig",
                                };
                            }

                            foreach (KeyValuePair<string, string> kv in userValues)
                            {
                                _logger.LogDebug(
                                    "File handler config: {Key} = {Value}",
                                    kv.Key,
                                    kv.Value
                                );
                                progress.Report(
                                    new InstallProgress(
                                        InstallStage.RunningPlugins,
                                        0,
                                        $"Config: {kv.Key} = {kv.Value}"
                                    )
                                );
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

                        // Report per-file results
                        foreach (FileHandlerFileResult fr in handlerResult.FileResults)
                        {
                            string msg = fr.Success
                                ? $"{Path.GetFileName(fr.FilePath)}: {fr.Action}"
                                : $"{Path.GetFileName(fr.FilePath)}: FAILED - {fr.ErrorMessage}";
                            progress.Report(
                                new InstallProgress(InstallStage.RunningPlugins, 0, msg)
                            );
                        }

                        if (!handlerResult.Success)
                        {
                            _logger.LogError(
                                "File handler failed, aborting installation: {Error}",
                                handlerResult.ErrorMessage
                            );
                            progress.Report(
                                new InstallProgress(
                                    InstallStage.RunningPlugins,
                                    0,
                                    $"Plugin processing failed: {handlerResult.ErrorMessage}"
                                )
                            );
                            progress.Report(
                                new InstallProgress(
                                    InstallStage.RunningPlugins,
                                    0,
                                    "Installation aborted due to plugin failure. Cleaning up..."
                                )
                            );
                            return new InstallResult
                            {
                                Success = false,
                                ErrorMessage = handlerResult.ErrorMessage,
                                FailedStep = "FileHandler",
                            };
                        }
                        else
                        {
                            progress.Report(
                                new InstallProgress(
                                    InstallStage.RunningPlugins,
                                    0,
                                    "Plugin processing completed successfully"
                                )
                            );
                        }

                        foreach (string handled in matchingFiles)
                        {
                            handledFiles.Add(handled);
                        }
                    }
                }
            }

            // Allow plugins to resolve templates in the target path (e.g. {ACMEPath})
            string resolvedTargetPath = options.TargetPath;
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
                _logger.LogInformation(
                    "Resolved path {Path} requires elevation, spawning elevated process",
                    resolvedTargetPath
                );
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        0,
                        "Administrator privileges required for resolved path..."
                    )
                );
                bool elevated = ElevationHelper.RunElevatedInstall(
                    manifest.ProductId,
                    manifest.Version,
                    resolvedTargetPath,
                    options.FeedId ?? _feedRegistry.GetFeeds()[0].Id
                );
                if (!elevated)
                {
                    return new InstallResult
                    {
                        Success = false,
                        ErrorMessage = "Installation cancelled: administrator rights were denied.",
                        FailedStep = "Elevation",
                    };
                }

                InstalledProduct elevatedProduct = new InstalledProduct(
                    ProductId: manifest.ProductId,
                    Title: manifest.Title,
                    Version: manifest.Version,
                    InstalledPath: resolvedTargetPath,
                    InstalledDate: DateTime.UtcNow,
                    FeedId: options.FeedId,
                    BackupPath: null
                );
                await _productRepository.AddAsync(elevatedProduct, cancellationToken);
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        100,
                        $"Installation completed via elevated process."
                    )
                );
                return new InstallResult { Success = true };
            }

            // Check for unresolved templates in the target path
            if (resolvedTargetPath.Contains('{') && resolvedTargetPath.Contains('}'))
            {
                string msg =
                    $"Install path contains unresolved template: {resolvedTargetPath}. "
                    + "Configure the required plugin settings (e.g., Application paths) before installing.";
                _logger.LogError(msg);
                progress.Report(new InstallProgress(InstallStage.Installing, 0, msg));
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = msg,
                    FailedStep = "PathResolution",
                };
            }

            // Step: Copy files (excluding handled files)
            _logger.LogInformation("Copying files to {TargetPath}", resolvedTargetPath);
            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    0,
                    $"Copying files to {resolvedTargetPath}..."
                )
            );
            Directory.CreateDirectory(resolvedTargetPath);
            FileOperations fileOps = new FileOperations();
            DeferredFileOps deferredOps = new DeferredFileOps();
            List<string> deferredRenames = new List<string>();

            try
            {
                await CopyDirectoryWithLockHandlingAsync(
                    fileOps,
                    deferredOps,
                    extractPath,
                    resolvedTargetPath,
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
                RevertDeferredRenames(deferredRenames, resolvedTargetPath);
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = "File copy failed: " + ex.Message,
                    FailedStep = "Installing",
                    Exception = ex,
                };
            }
            progress.Report(new InstallProgress(InstallStage.Installing, 100, "Files copied."));

            cancellationToken.ThrowIfCancellationRequested();

            // Step: Verify
            progress.Report(
                new InstallProgress(InstallStage.Verifying, 0, "Verifying installation...")
            );

            // 1. Target directory must exist and contain files
            if (!Directory.Exists(resolvedTargetPath))
            {
                progress.Report(
                    new InstallProgress(
                        InstallStage.Verifying,
                        0,
                        "Verification failed: target directory does not exist."
                    )
                );
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = "Verification failed: target directory does not exist.",
                    FailedStep = "Verifying",
                };
            }

            string[] installedFiles = Directory.GetFiles(
                resolvedTargetPath,
                "*",
                SearchOption.AllDirectories
            );
            if (installedFiles.Length == 0)
            {
                progress.Report(
                    new InstallProgress(
                        InstallStage.Verifying,
                        0,
                        "Verification failed: no files in target directory."
                    )
                );
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = "Verification failed: no files in target directory.",
                    FailedStep = "Verifying",
                };
            }

            progress.Report(
                new InstallProgress(
                    InstallStage.Verifying,
                    25,
                    $"Target directory contains {installedFiles.Length} file(s)."
                )
            );

            // 2. Cross-check against extracted files (minus handled files)
            if (Directory.Exists(tempDir))
            {
                string[] extractedFiles = Directory.GetFiles(
                    tempDir,
                    "*",
                    SearchOption.AllDirectories
                );
                int expectedCount = extractedFiles.Length - handledFiles.Count;
                if (expectedCount > 0 && installedFiles.Length < expectedCount)
                {
                    progress.Report(
                        new InstallProgress(
                            InstallStage.Verifying,
                            50,
                            $"Warning: expected at least {expectedCount} files but found {installedFiles.Length}."
                        )
                    );
                    _logger.LogWarning(
                        "Verification warning: expected {Expected} files, found {Actual}",
                        expectedCount,
                        installedFiles.Length
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

            // 3. Check that key executables from shortcuts exist
            if (manifest.Shortcuts is { Length: > 0 })
            {
                foreach (ShortcutInfo shortcut in manifest.Shortcuts)
                {
                    string exePath = Path.Combine(resolvedTargetPath, shortcut.ExeName);
                    if (!File.Exists(exePath))
                    {
                        progress.Report(
                            new InstallProgress(
                                InstallStage.Verifying,
                                75,
                                $"Warning: shortcut target '{shortcut.ExeName}' not found in install directory."
                            )
                        );
                        _logger.LogWarning(
                            "Shortcut target missing after install: {ExeName}",
                            shortcut.ExeName
                        );
                    }
                }
            }

            // 4. Verify no zero-byte executables (corrupted copy)
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
                    progress.Report(
                        new InstallProgress(
                            InstallStage.Verifying,
                            75,
                            $"Verification failed: '{fileName}' is 0 bytes (corrupted)."
                        )
                    );
                    return new InstallResult
                    {
                        Success = false,
                        ErrorMessage = $"Verification failed: '{fileName}' is 0 bytes.",
                        FailedStep = "Verifying",
                    };
                }
            }

            progress.Report(
                new InstallProgress(
                    InstallStage.Verifying,
                    100,
                    "Verification complete - all checks passed."
                )
            );

            // Step: PostInstall plugins
            PluginPhaseResult postInstallPhaseResult = await RunPluginPhaseAsync(
                manifest,
                options,
                pluginContext,
                PluginPhase.PostInstall,
                cancellationToken
            );
            if (!postInstallPhaseResult.Success)
            {
                _logger.LogWarning(
                    "PostInstall plugin phase failed: {Error}",
                    postInstallPhaseResult.ErrorMessage
                );
            }

            progress.Report(
                new InstallProgress(InstallStage.Installing, 80, "Creating shortcuts...")
            );
            _logger.LogDebug("Creating shortcuts for {ProductId}", manifest.ProductId);
            CreateShortcuts(manifest, resolvedTargetPath);

            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    85,
                    "Applying environment variables..."
                )
            );
            _logger.LogDebug("Applying environment variables for {ProductId}", manifest.ProductId);
            await ApplyEnvironmentVariablesAsync(manifest, resolvedTargetPath, cancellationToken);

            await SavePluginConfigValues(
                manifest.ProductId,
                options.PluginConfigValues,
                cancellationToken
            );

            progress.Report(
                new InstallProgress(InstallStage.Installing, 90, "Saving file manifest...")
            );
            _logger.LogDebug("Saving file manifest for {ProductId}", manifest.ProductId);
            await SaveFileManifestAsync(manifest.ProductId, resolvedTargetPath, cancellationToken);

            progress.Report(
                new InstallProgress(InstallStage.Installing, 95, "Registering product...")
            );
            _logger.LogDebug("Registering installed product {ProductId}", manifest.ProductId);
            InstalledProduct installed = new InstalledProduct(
                ProductId: manifest.ProductId,
                Title: manifest.Title,
                Version: manifest.Version,
                InstalledPath: resolvedTargetPath,
                InstalledDate: DateTime.UtcNow,
                FeedId: options.FeedId,
                BackupPath: null
            );
            await _productRepository.AddAsync(installed, cancellationToken);

            await _activityLog.LogAsync(
                new ActivityLogEntry(
                    Id: Guid.NewGuid().ToString(),
                    Timestamp: DateTime.UtcNow,
                    Action: "Install",
                    ProductId: manifest.ProductId,
                    Details: $"{manifest.Title} v{manifest.Version} installed to {resolvedTargetPath}",
                    Success: true
                ),
                cancellationToken
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
                    $"Installation failed: {ex.Message}. Cleaning up..."
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
            try
            {
                if (Directory.Exists(tempDir))
                {
                    progress.Report(
                        new InstallProgress(
                            InstallStage.Installing,
                            0,
                            "Cleaning up temporary files..."
                        )
                    );
                    Directory.Delete(tempDir, true);
                    progress.Report(
                        new InstallProgress(InstallStage.Installing, 0, "Cleanup complete.")
                    );
                }
            }
            catch (Exception ex)
            {
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        0,
                        $"Warning: temporary files could not be cleaned up: {ex.Message}"
                    )
                );
                _logger.LogWarning(
                    ex,
                    "Temporary directory could not be deleted: {TempDir}",
                    tempDir
                );
            }
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
        if (Directory.Exists(installed.InstalledPath))
        {
            foreach (
                string file in Directory.GetFiles(
                    installed.InstalledPath,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                string ext = Path.GetExtension(file);
                if (
                    !ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                    && !ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                )
                    continue;

                if (_fileLockDetector.IsFileLocked(file))
                {
                    IReadOnlyList<string> processes = _fileLockDetector.GetLockingProcesses(file);
                    string processNames =
                        processes.Count > 0 ? string.Join(", ", processes) : string.Empty;
                    throw new FileLockedException(Path.GetFileName(file), processNames);
                }
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
            // Remove old environment variables before installing new ones
            _logger.LogDebug(
                "Removing old environment variables for {ProductId}",
                installed.ProductId
            );
            try
            {
                List<AppliedEnvironmentVariable> oldEnvVars = await _envVarService.LoadAppliedAsync(
                    installed.ProductId,
                    cancellationToken
                );
                if (oldEnvVars.Count > 0)
                    _envVarService.Remove(oldEnvVars);
                _envVarService.DeleteTracking(installed.ProductId);
            }
            catch
            {
                // Best-effort
            }

            _logger.LogDebug(
                "Deleting old installation at {InstalledPath}",
                installed.InstalledPath
            );
            if (Directory.Exists(installed.InstalledPath))
                Directory.Delete(installed.InstalledPath, true);

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
    public async Task UninstallAsync(
        InstalledProduct product,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation(
            "Delegating uninstall of {ProductId} to UninstallService",
            product.ProductId
        );
        await _uninstallService.UninstallAsync(product, cancellationToken);
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
        CancellationToken cancellationToken
    )
    {
        if (manifest.Plugins is not { Length: > 0 })
            return new PluginPhaseResult { Success = true };

        foreach (StorkPluginInfo pluginInfo in manifest.Plugins)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                IStorkPlugin? plugin = LoadPlugin(options.TargetPath, pluginInfo);
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
                if (phase == PluginPhase.PreInstall || phase == PluginPhase.PreUninstall)
                {
                    return new PluginPhaseResult { Success = false, ErrorMessage = ex.Message };
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

            if (File.Exists(targetPath) && _fileLockDetector.IsFileLocked(targetPath))
            {
                // File-in-use handling: rename locked file, place new file, schedule delete
                string delFileName = $"DEL_{Guid.NewGuid():N}_{Path.GetFileName(targetPath)}";
                string delPath = Path.Combine(Path.GetDirectoryName(targetPath)!, delFileName);
                File.Move(targetPath, delPath);
                deferredRenames.Add(delPath);

                await fileOps.CopyFileAsync(file.FullName, targetPath, cancellationToken);
                deferredOps.ScheduleDeleteOnReboot(delPath);
            }
            else
            {
                await fileOps.CopyFileAsync(file.FullName, targetPath, cancellationToken);
            }

            processedFiles++;
            int percentage =
                totalFiles > 0 ? (int)((double)processedFiles / totalFiles * 100) : 100;
            progress?.Report(percentage);
        }
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
            catch
            {
                // Best-effort revert
            }
        }
    }

    private async Task SaveFileManifestAsync(
        string productId,
        string installPath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            string configDir = GetStorkConfigDir();
            Directory.CreateDirectory(configDir);
            string manifestPath = Path.Combine(configDir, $"{productId}.files.json");

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
        }
    }

    private async Task<IStorkPlugin?> DownloadAndLoadPluginAsync(
        ProductManifest manifest,
        StorkPluginInfo pluginInfo,
        CancellationToken cancellationToken
    )
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            "StorkDrop",
            "plugin-temp",
            Guid.NewGuid().ToString()
        );
        Directory.CreateDirectory(tempDir);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            IRegistryClient registryClient = GetClientForFeed(null);
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

    private static IStorkPlugin? LoadPlugin(string basePath, StorkPluginInfo pluginInfo)
    {
        string assemblyPath = Path.GetFullPath(Path.Combine(basePath, pluginInfo.Assembly));
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"Plugin assembly not found: {pluginInfo.Assembly}");

        System.Reflection.Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(
            assemblyPath
        );
        Type? pluginType =
            assembly.GetType(pluginInfo.TypeName)
            ?? throw new TypeLoadException($"Type not found: {pluginInfo.TypeName}");

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
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, string> previousValues = await LoadPluginConfigValues(
            manifest.ProductId,
            cancellationToken
        );
        InstalledProduct? previousInstall = await _productRepository.GetByIdAsync(
            manifest.ProductId,
            cancellationToken
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
            Version = manifest.Version,
            InstallPath = options.TargetPath,
            StorkConfigDirectory = GetStorkConfigDir(),
            ConfigValues = options.PluginConfigValues ?? new Dictionary<string, string>(),
        };
    }

    private static string GetStorkConfigDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StorkDrop",
            "Config"
        );

    private async Task SavePluginConfigValues(
        string productId,
        Dictionary<string, string>? values,
        CancellationToken cancellationToken
    )
    {
        if (values is null || values.Count == 0)
            return;

        string configDir = GetStorkConfigDir();
        Directory.CreateDirectory(configDir);
        string filePath = Path.Combine(configDir, $"plugin-config-{productId}.json");
        string json = System.Text.Json.JsonSerializer.Serialize(
            values,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
        );
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private async Task<Dictionary<string, string>> LoadPluginConfigValues(
        string productId,
        CancellationToken cancellationToken
    )
    {
        string filePath = Path.Combine(GetStorkConfigDir(), $"plugin-config-{productId}.json");
        if (!File.Exists(filePath))
            return new Dictionary<string, string>();

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
        string installPath,
        CancellationToken cancellationToken
    )
    {
        if (manifest.EnvironmentVariables is not { Length: > 0 })
            return;

        try
        {
            List<AppliedEnvironmentVariable> applied = _envVarService.Apply(
                manifest.EnvironmentVariables,
                installPath
            );
            await _envVarService.SaveAppliedAsync(manifest.ProductId, applied, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Environment variables could not be applied");
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
        }
    }
}
