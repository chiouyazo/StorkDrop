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
    private readonly IRegistryClient _registryClient;
    private readonly IProductRepository _productRepository;
    private readonly IBackupService _backupService;
    private readonly IFileLockDetector _fileLockDetector;
    private readonly IActivityLog _activityLog;
    private readonly IConfigurationService _configurationService;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<InstallationEngine> _logger;
    private readonly EnvironmentVariableService _envVarService;
    private readonly List<IFileTypeHandler> _fileTypeHandlers;

    /// <inheritdoc />
    public FileHandlerConfigCallback? OnFileHandlerConfigNeeded { get; set; }

    private static readonly JsonSerializerOptions FileManifestJsonOptions =
        new JsonSerializerOptions { WriteIndented = true };

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationEngine"/> class.
    /// </summary>
    public InstallationEngine(
        IRegistryClient registryClient,
        IProductRepository productRepository,
        IBackupService backupService,
        IFileLockDetector fileLockDetector,
        IActivityLog activityLog,
        IConfigurationService configurationService,
        IEncryptionService encryptionService,
        IEnumerable<IStorkDropPlugin> plugins,
        EnvironmentVariableService envVarService,
        ILogger<InstallationEngine> logger
    )
    {
        _registryClient = registryClient;
        _productRepository = productRepository;
        _backupService = backupService;
        _fileLockDetector = fileLockDetector;
        _activityLog = activityLog;
        _configurationService = configurationService;
        _encryptionService = encryptionService;
        _fileTypeHandlers = plugins.OfType<IFileTypeHandler>().ToList();
        _envVarService = envVarService;
        _logger = logger;
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
        string tempDir = Path.Combine(Path.GetTempPath(), "StorkDrop", Guid.NewGuid().ToString());

        try
        {
            if (
                ElevationHelper.PathRequiresAdmin(options.TargetPath)
                && !ElevationHelper.IsRunningAsAdmin()
            )
            {
                bool elevated = ElevationHelper.RunElevatedInstall(
                    manifest.ProductId,
                    manifest.Version,
                    options.TargetPath
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
            progress.Report(
                new InstallProgress(
                    InstallStage.Downloading,
                    0,
                    $"Downloading {manifest.Title} v{manifest.Version}..."
                )
            );
            using Stream downloadStream = await _registryClient.DownloadProductAsync(
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

            cancellationToken.ThrowIfCancellationRequested();

            // Step: Extract
            progress.Report(new InstallProgress(InstallStage.Extracting, 0, "Extracting files..."));
            string extractPath = Path.Combine(tempDir, "extracted");
            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting {ZipPath}", zipPath);
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
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = preInstallPhaseResult.ErrorMessage,
                    FailedStep = "PreInstall",
                };
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Step: Handle custom file types (plugins claim files before copy)
            HashSet<string> handledFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_fileTypeHandlers.Count > 0)
            {
                PluginContext fileHandlerContext = await BuildPluginContextAsync(
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
                        _logger.LogInformation(
                            "File handler claims {Count} files with extensions {Extensions}",
                            matchingFiles.Count,
                            string.Join(", ", handler.HandledExtensions)
                        );

                        // Ask handler if it needs user config for these files
                        IReadOnlyList<PluginConfigField> handlerFields =
                            handler.GetFileHandlerConfig(matchingFiles, fileHandlerContext);

                        if (handlerFields.Count > 0 && OnFileHandlerConfigNeeded is not null)
                        {
                            Dictionary<string, string>? userValues = OnFileHandlerConfigNeeded(
                                handlerFields,
                                fileHandlerContext.ConfigValues
                            );

                            if (userValues is null)
                            {
                                _logger.LogInformation("User cancelled file handler config dialog");
                                continue; // Skip this handler
                            }

                            // Merge user selections into context
                            foreach (KeyValuePair<string, string> kv in userValues)
                            {
                                fileHandlerContext.ConfigValues[kv.Key] = kv.Value;
                            }
                        }

                        FileHandlerResult handlerResult = await handler.HandleFilesAsync(
                            matchingFiles,
                            fileHandlerContext,
                            cancellationToken
                        );

                        if (!handlerResult.Success)
                        {
                            _logger.LogWarning(
                                "File handler failed: {Error}",
                                handlerResult.ErrorMessage
                            );
                        }

                        foreach (string handled in matchingFiles)
                        {
                            handledFiles.Add(handled);
                        }
                    }
                }
            }

            // Step: Copy files (excluding handled files)
            progress.Report(new InstallProgress(InstallStage.Installing, 0, "Copying files..."));
            Directory.CreateDirectory(options.TargetPath);
            FileOperations fileOps = new FileOperations();
            DeferredFileOps deferredOps = new DeferredFileOps();
            List<string> deferredRenames = new List<string>();

            try
            {
                await CopyDirectoryWithLockHandlingAsync(
                    fileOps,
                    deferredOps,
                    extractPath,
                    options.TargetPath,
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
                RevertDeferredRenames(deferredRenames, options.TargetPath);
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
            if (
                !Directory.Exists(options.TargetPath)
                || Directory.GetFiles(options.TargetPath, "*", SearchOption.AllDirectories).Length
                    == 0
            )
            {
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = "Verification failed: no files in target directory.",
                    FailedStep = "Verifying",
                };
            }
            progress.Report(
                new InstallProgress(InstallStage.Verifying, 100, "Verification complete.")
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

            CreateShortcuts(manifest, options.TargetPath);
            await ApplyEnvironmentVariablesAsync(manifest, options.TargetPath, cancellationToken);

            await SavePluginConfigValues(
                manifest.ProductId,
                options.PluginConfigValues,
                cancellationToken
            );

            // Track installed files
            await SaveFileManifestAsync(manifest.ProductId, options.TargetPath, cancellationToken);

            InstalledProduct installed = new InstalledProduct(
                ProductId: manifest.ProductId,
                Title: manifest.Title,
                Version: manifest.Version,
                InstalledPath: options.TargetPath,
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
                    Details: $"{manifest.Title} v{manifest.Version} installed to {options.TargetPath}",
                    Success: true
                ),
                cancellationToken
            );

            return new InstallResult { Success = true };
        }
        catch (OperationCanceledException)
        {
            return new InstallResult
            {
                Success = false,
                ErrorMessage = "Installation cancelled by user.",
                FailedStep = "Cancelled",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Installation failed for {ProductId}", manifest.ProductId);
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
                    Directory.Delete(tempDir, true);
            }
            catch (Exception ex)
            {
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
            backupPath = await _backupService.CreateBackupAsync(
                installed.ProductId,
                installed.InstalledPath,
                cancellationToken
            );
        }

        try
        {
            // Remove old environment variables before installing new ones
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

            if (Directory.Exists(installed.InstalledPath))
                Directory.Delete(installed.InstalledPath, true);

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
        }
        catch
        {
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
        UninstallService uninstaller = new UninstallService(
            _productRepository,
            _activityLog,
            _fileLockDetector,
            _envVarService
        );
        await uninstaller.UninstallAsync(product, cancellationToken);
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

            using Stream downloadStream = await _registryClient.DownloadProductAsync(
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
