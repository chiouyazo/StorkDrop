using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Demo.Plugins;

namespace StorkDrop.Demo.Services;

internal sealed class DemoInstallationEngine : IInstallationEngine
{
    private readonly IProductRepository _productRepository;
    private readonly IActivityLog _activityLog;
    private readonly DemoInteractivePlugin _interactivePlugin = new DemoInteractivePlugin();

    public DemoInstallationEngine(IProductRepository productRepository, IActivityLog activityLog)
    {
        _productRepository = productRepository;
        _activityLog = activityLog;
    }

    public InstallPathResolverCallback? OnResolveInstallPath { get; set; }
    public FileHandlerConfigCallback? OnFileHandlerConfigNeeded { get; set; }
    public FileHandlerConfigCallback? OnPluginConfigNeeded { get; set; }
    public ActionGroupConfigCallback? OnActionGroupConfigNeeded { get; set; }
    public IInteractiveStorkPlugin? CurrentInteractivePlugin => _interactivePlugin;

    public Task<IReadOnlyList<PluginActionGroup>> GetActionGroupsAsync(
        ProductManifest manifest,
        string? feedId = null,
        CancellationToken cancellationToken = default
    )
    {
        if (manifest.Plugins is not { Length: > 0 })
            return Task.FromResult<IReadOnlyList<PluginActionGroup>>([]);

        IReadOnlyList<PluginActionDescription> descriptions =
            _interactivePlugin.GetActionDescriptions(new PluginEnvironment());

        List<PluginActionGroup> groups = descriptions
            .Select(desc => new PluginActionGroup
            {
                GroupId = $"{desc.Phase.ToString().ToLowerInvariant()}-demo-{desc.Title}",
                Title = desc.Title,
                Phase = desc.Phase,
                IsEnabled = desc.IsEnabled,
                Fields = desc.Fields,
                Descriptions = [desc],
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<PluginActionGroup>>(groups);
    }

    public Task<IReadOnlyList<PluginConfigField>> GetPluginConfigurationAsync(
        ProductManifest manifest,
        string? feedId = null,
        CancellationToken cancellationToken = default
    )
    {
        if (manifest.Plugins is not { Length: > 0 })
            return Task.FromResult<IReadOnlyList<PluginConfigField>>([]);

        return Task.FromResult(_interactivePlugin.GetConfigurationSchema(new PluginEnvironment()));
    }

    public async Task<InstallResult> InstallAsync(
        ProductManifest manifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    )
    {
        for (int i = 0; i <= 30; i += 5)
        {
            progress.Report(
                new InstallProgress(
                    InstallStage.Downloading,
                    i,
                    i == 0
                        ? $"Downloading {manifest.Title} v{manifest.Version}..."
                        : $"Downloading... {i * 3}% ({i * 1.5:F1} MB / {manifest.DownloadSizeBytes / 1_000_000.0:F1} MB)"
                )
            );
            await Task.Delay(300, cancellationToken);
        }

        progress.Report(new InstallProgress(InstallStage.Extracting, 35, "Extracting package..."));
        await Task.Delay(500, cancellationToken);
        progress.Report(new InstallProgress(InstallStage.Extracting, 45, "Extraction complete."));
        await Task.Delay(300, cancellationToken);

        if (manifest.ProductId == "nova-dashboard" && OnFileHandlerConfigNeeded is not null)
        {
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    48,
                    "Plugin found 2 file(s): schema-update.sql, seed-data.sql"
                )
            );
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    48,
                    "Waiting for file handler configuration..."
                )
            );

            List<PluginConfigField> fileFields =
            [
                new PluginConfigField
                {
                    Key = "target-db",
                    Label = "Target Database",
                    FieldType = PluginFieldType.Dropdown,
                    Required = true,
                    Options =
                    [
                        new PluginOptionItem
                        {
                            Value = "production",
                            Label = "Production (db-prod-01)",
                        },
                        new PluginOptionItem
                        {
                            Value = "staging",
                            Label = "Staging (db-staging-01)",
                        },
                    ],
                },
                new PluginConfigField
                {
                    Key = "deploy-schema",
                    Label = "Deploy schema-update.sql",
                    FieldType = PluginFieldType.Checkbox,
                    DefaultValue = "true",
                },
                new PluginConfigField
                {
                    Key = "deploy-seed",
                    Label = "Deploy seed-data.sql",
                    FieldType = PluginFieldType.Checkbox,
                    DefaultValue = "true",
                },
            ];

            Dictionary<string, string>? fileConfig = OnFileHandlerConfigNeeded(
                fileFields,
                new Dictionary<string, string>()
            );
            if (fileConfig is null)
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = "File handler configuration cancelled.",
                };

            progress.Report(
                new InstallProgress(InstallStage.RunningPlugins, 50, "Processing SQL files...")
            );
            await Task.Delay(800, cancellationToken);
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    52,
                    "schema-update.sql: Deployed to "
                        + fileConfig.GetValueOrDefault("target-db", "production")
                )
            );
            await Task.Delay(400, cancellationToken);
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    54,
                    "seed-data.sql: Deployed successfully"
                )
            );
            await Task.Delay(200, cancellationToken);
        }

        if (manifest.Plugins is { Length: > 0 })
        {
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    50,
                    "Loading plugin configuration..."
                )
            );

            IReadOnlyList<PluginActionGroup> groups = await GetActionGroupsAsync(
                manifest,
                options.FeedId,
                cancellationToken
            );

            Dictionary<string, string>? pluginConfig = options.PluginConfigValues;
            if (pluginConfig is null && OnActionGroupConfigNeeded is not null && groups.Count > 0)
            {
                pluginConfig = OnActionGroupConfigNeeded(groups, new Dictionary<string, string>());
            }
            else if (pluginConfig is null && OnPluginConfigNeeded is not null)
            {
                IReadOnlyList<PluginConfigField> schema = _interactivePlugin.GetConfigurationSchema(
                    new PluginEnvironment()
                );
                pluginConfig = OnPluginConfigNeeded(schema, new Dictionary<string, string>());
            }

            if (pluginConfig is null)
                return new InstallResult
                {
                    Success = false,
                    ErrorMessage = "Plugin configuration cancelled.",
                };

            progress.Report(
                new InstallProgress(InstallStage.RunningPlugins, 52, "Running PreInstall...")
            );
            await Task.Delay(300, cancellationToken);
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    55,
                    "PreInstall: Validating database connection..."
                )
            );
            await Task.Delay(400, cancellationToken);
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    58,
                    "PreInstall: Connection verified."
                )
            );
            await Task.Delay(200, cancellationToken);
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    60,
                    "PreInstall: All prerequisites met."
                )
            );
            await Task.Delay(200, cancellationToken);
        }

        string resolvedPath = options.TargetPath;
        if (OnResolveInstallPath is not null)
        {
            string? resolved = OnResolveInstallPath(resolvedPath, null);
            if (resolved is not null)
            {
                resolvedPath = resolved;
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        65,
                        $"Resolved install path: {resolvedPath}"
                    )
                );
            }
        }

        string[] simulatedFiles =
        [
            "App.exe",
            "App.dll",
            "App.deps.json",
            "config/default.json",
            "lib/core.dll",
            "lib/data.dll",
        ];
        for (int i = 0; i < simulatedFiles.Length; i++)
        {
            int pct = 70 + (i * 15 / simulatedFiles.Length);
            progress.Report(
                new InstallProgress(InstallStage.Installing, pct, $"Copying {simulatedFiles[i]}...")
            );
            await Task.Delay(200, cancellationToken);
        }

        if (manifest.Plugins is { Length: > 0 })
        {
            progress.Report(
                new InstallProgress(InstallStage.RunningPlugins, 85, "Running PostInstall...")
            );
            await Task.Delay(300, cancellationToken);
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    87,
                    "PostInstall: Creating reporting tables..."
                )
            );
            await Task.Delay(500, cancellationToken);
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    89,
                    "PostInstall: Inserting default configuration..."
                )
            );
            await Task.Delay(400, cancellationToken);
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    91,
                    "PostInstall: Registering scheduled tasks..."
                )
            );
            await Task.Delay(300, cancellationToken);
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    93,
                    "PostInstall: Configuration applied successfully."
                )
            );
            await Task.Delay(200, cancellationToken);
        }

        if (manifest.Shortcuts is { Length: > 0 })
        {
            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    94,
                    $"Creating {manifest.Shortcuts.Length} shortcut(s)..."
                )
            );
            await Task.Delay(300, cancellationToken);
        }

        progress.Report(
            new InstallProgress(InstallStage.Verifying, 96, "Verifying installation...")
        );
        await Task.Delay(300, cancellationToken);
        progress.Report(
            new InstallProgress(
                InstallStage.Verifying,
                100,
                $"Installation of {manifest.Title} v{manifest.Version} completed successfully."
            )
        );

        await _productRepository.AddAsync(
            new InstalledProduct(
                manifest.ProductId,
                manifest.Title,
                manifest.Version,
                resolvedPath,
                DateTime.UtcNow,
                options.FeedId,
                BackupPath: null,
                InstallType: manifest.InstallType
            ),
            cancellationToken
        );

        await _activityLog.LogAsync(
            new ActivityLogEntry(
                Guid.NewGuid().ToString(),
                DateTime.UtcNow,
                "Install",
                manifest.ProductId,
                $"Installed {manifest.Title} v{manifest.Version} to {resolvedPath}",
                true
            ),
            cancellationToken
        );

        return new InstallResult { Success = true };
    }

    public async Task UpdateAsync(
        InstalledProduct installed,
        ProductManifest newManifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    )
    {
        progress.Report(
            new InstallProgress(
                InstallStage.Downloading,
                5,
                $"Creating backup of {installed.Title} v{installed.Version}..."
            )
        );
        await Task.Delay(500, cancellationToken);

        await InstallAsync(newManifest, options, progress, cancellationToken);

        await _activityLog.LogAsync(
            new ActivityLogEntry(
                Guid.NewGuid().ToString(),
                DateTime.UtcNow,
                "Update",
                newManifest.ProductId,
                $"Updated {newManifest.Title} from v{installed.Version} to v{newManifest.Version}",
                true
            ),
            cancellationToken
        );
    }

    public async Task UninstallAsync(
        InstalledProduct product,
        CancellationToken cancellationToken = default
    )
    {
        await _productRepository.RemoveAsync(product.ProductId, cancellationToken);
        await _activityLog.LogAsync(
            new ActivityLogEntry(
                Guid.NewGuid().ToString(),
                DateTime.UtcNow,
                "Uninstall",
                product.ProductId,
                $"Uninstalled {product.Title} v{product.Version} from {product.InstalledPath}",
                true
            ),
            cancellationToken
        );
    }

    public async Task<InstallResult> ReExecutePluginsAsync(
        InstalledProduct product,
        ReExecuteOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    )
    {
        progress.Report(
            new InstallProgress(
                InstallStage.RunningPlugins,
                5,
                $"Loading plugin data for {product.Title}..."
            )
        );
        await Task.Delay(300, cancellationToken);

        progress.Report(
            new InstallProgress(
                InstallStage.RunningPlugins,
                10,
                "Loading plugin configuration schema..."
            )
        );
        await Task.Delay(200, cancellationToken);

        Dictionary<string, string> previousValues = new Dictionary<string, string>
        {
            ["target-database"] = "dev",
            ["schema-name"] = "dbo",
            ["timeout"] = "300",
        };

        ProductManifest? actualManifest = Data
            .DemoProducts.InternalFeedProducts.Concat(Data.DemoProducts.PartnerFeedProducts)
            .FirstOrDefault(p => p.ProductId == product.ProductId);

        List<PluginActionGroup> groups = [];
        if (actualManifest?.Plugins is { Length: > 0 })
        {
            groups.AddRange(
                await GetActionGroupsAsync(actualManifest, product.FeedId, cancellationToken)
            );
        }

        if (product.ProductId == "nova-dashboard")
        {
            groups.Insert(
                0,
                new PluginActionGroup
                {
                    GroupId = "filehandler-SQL Deploy Tools",
                    Title = "File Handler: SQL Deploy Tools",
                    Phase = PluginActionPhase.PreInstall,
                    Fields =
                    [
                        new PluginConfigField
                        {
                            Key = "target-db",
                            Label = "Target Database",
                            FieldType = PluginFieldType.Dropdown,
                            Required = true,
                            Options =
                            [
                                new PluginOptionItem
                                {
                                    Value = "production",
                                    Label = "Production (db-prod-01)",
                                },
                                new PluginOptionItem
                                {
                                    Value = "staging",
                                    Label = "Staging (db-staging-01)",
                                },
                            ],
                        },
                        new PluginConfigField
                        {
                            Key = "deploy-schema",
                            Label = "Deploy schema-update.sql",
                            FieldType = PluginFieldType.Checkbox,
                            DefaultValue = "true",
                        },
                        new PluginConfigField
                        {
                            Key = "deploy-seed",
                            Label = "Deploy seed-data.sql",
                            FieldType = PluginFieldType.Checkbox,
                            DefaultValue = "true",
                        },
                    ],
                }
            );
        }

        Dictionary<string, string>? configValues = null;
        if (OnActionGroupConfigNeeded is not null && groups.Count > 0)
        {
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    15,
                    "Waiting for plugin configuration..."
                )
            );
            configValues = OnActionGroupConfigNeeded(groups, previousValues);
        }
        else if (OnPluginConfigNeeded is not null)
        {
            IReadOnlyList<PluginConfigField> schema = _interactivePlugin.GetConfigurationSchema(
                new PluginEnvironment()
            );
            progress.Report(
                new InstallProgress(
                    InstallStage.RunningPlugins,
                    15,
                    "Waiting for plugin configuration..."
                )
            );
            configValues = OnPluginConfigNeeded(schema, previousValues);
        }

        if (configValues is null)
            return new InstallResult
            {
                Success = false,
                ErrorMessage = "Plugin configuration cancelled.",
            };

        progress.Report(
            new InstallProgress(InstallStage.RunningPlugins, 25, "Running PreInstall...")
        );
        await Task.Delay(400, cancellationToken);
        progress.Report(
            new InstallProgress(
                InstallStage.RunningPlugins,
                30,
                "PreInstall: Validating database connection..."
            )
        );
        await Task.Delay(500, cancellationToken);
        progress.Report(
            new InstallProgress(
                InstallStage.RunningPlugins,
                35,
                "PreInstall: Connection to database verified."
            )
        );
        await Task.Delay(200, cancellationToken);
        progress.Report(
            new InstallProgress(
                InstallStage.RunningPlugins,
                40,
                "PreInstall: Checking schema permissions..."
            )
        );
        await Task.Delay(300, cancellationToken);
        progress.Report(
            new InstallProgress(
                InstallStage.RunningPlugins,
                45,
                "PreInstall: All prerequisites met."
            )
        );
        await Task.Delay(200, cancellationToken);

        progress.Report(
            new InstallProgress(InstallStage.RunningPlugins, 50, "Running PostInstall...")
        );
        await Task.Delay(400, cancellationToken);
        progress.Report(
            new InstallProgress(
                InstallStage.RunningPlugins,
                55,
                "PostInstall: Creating reporting tables..."
            )
        );
        await Task.Delay(600, cancellationToken);
        progress.Report(
            new InstallProgress(
                InstallStage.RunningPlugins,
                65,
                "PostInstall: Inserting default configuration..."
            )
        );
        await Task.Delay(400, cancellationToken);
        progress.Report(
            new InstallProgress(
                InstallStage.RunningPlugins,
                75,
                "PostInstall: Registering scheduled tasks..."
            )
        );
        await Task.Delay(500, cancellationToken);
        progress.Report(
            new InstallProgress(
                InstallStage.RunningPlugins,
                85,
                "PostInstall: Verifying data integrity..."
            )
        );
        await Task.Delay(300, cancellationToken);
        progress.Report(
            new InstallProgress(InstallStage.RunningPlugins, 90, "Saving configuration...")
        );
        await Task.Delay(200, cancellationToken);

        progress.Report(
            new InstallProgress(
                InstallStage.Verifying,
                100,
                $"Plugin actions for {product.Title} completed successfully."
            )
        );

        await _activityLog.LogAsync(
            new ActivityLogEntry(
                Guid.NewGuid().ToString(),
                DateTime.UtcNow,
                "ReExecute",
                product.ProductId,
                $"Re-executed plugin actions for {product.Title} v{product.Version}",
                true
            ),
            cancellationToken
        );

        return new InstallResult { Success = true };
    }
}
