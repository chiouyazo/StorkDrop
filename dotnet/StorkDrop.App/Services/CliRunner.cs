using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Installer;

namespace StorkDrop.App.Services;

internal sealed class CliRunner
{
    private readonly IFeedRegistry _feedRegistry;
    private readonly IInstallationEngine _engine;
    private readonly InstallationCoordinator _coordinator;
    private readonly IProductRepository _productRepository;

    public CliRunner(IServiceProvider services)
    {
        _feedRegistry = services.GetRequiredService<IFeedRegistry>();
        _engine = services.GetRequiredService<IInstallationEngine>();
        _coordinator = services.GetRequiredService<InstallationCoordinator>();
        _productRepository = services.GetRequiredService<IProductRepository>();
    }

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 3)
        {
            PrintHelp();
            return 1;
        }

        string command = args[2].ToLowerInvariant();

        try
        {
            return command switch
            {
                "install" => await InstallAsync(args),
                "uninstall" => await UninstallAsync(args),
                "update" => await UpdateAsync(args),
                "re-execute" => await ReExecuteAsync(args),
                "list" => await ListAsync(),
                "versions" => await VersionsAsync(args),
                "help" => PrintCommandHelp(args.Length > 3 ? args[3] : null),
                _ => Error($"Unknown command '{command}'. Run --cli help for usage."),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> InstallAsync(string[] args)
    {
        if (args.Length < 4)
            return Error("Missing product ID. Usage: storkdrop --cli install <productId>");

        string productId = args[3];
        string? version = GetFlag(args, "--version");
        string? path = GetFlag(args, "--path");
        Dictionary<string, string> configValues = ParseConfigValues(args);

        (ProductManifest? manifest, string? feedId) = await FindManifestInFeedsAsync(
            productId,
            version
        );
        if (manifest is null || feedId is null)
        {
            return version is not null
                ? Error($"Version '{version}' not found for product '{productId}'.")
                : Error($"Product '{productId}' not found in any configured feed.");
        }

        string targetPath = path ?? manifest.RecommendedInstallPath ?? string.Empty;
        if (string.IsNullOrEmpty(targetPath))
            return Error(
                "No install path specified and product has no recommended path. Use --path."
            );

        int configError = await ValidatePluginConfigAsync(manifest, feedId, configValues);
        if (configError != 0)
            return configError;

        SetupPluginConfigCallbacks(configValues);

        Console.WriteLine($"Installing {manifest.Title} v{manifest.Version} to {targetPath}");

        InstallOptions options = new InstallOptions(
            TargetPath: targetPath,
            FeedId: feedId,
            PluginConfigValues: configValues.Count > 0 ? configValues : null
        );
        Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
        {
            if (!string.IsNullOrEmpty(p.Message))
                Console.WriteLine($"[{p.Percentage}%] {p.Message}");
        });

        InstallResult result = await _coordinator.InstallWithIsolationAsync(
            manifest,
            options,
            progress,
            CancellationToken.None
        );

        if (!result.Success)
            return Error($"Installation failed: {result.ErrorMessage}");

        Console.WriteLine($"Successfully installed {manifest.Title} v{manifest.Version}");
        return 0;
    }

    private async Task<int> UninstallAsync(string[] args)
    {
        if (args.Length < 4)
            return Error("Missing product ID. Usage: storkdrop --cli uninstall <productId>");

        string productId = args[3];
        InstalledProduct? installed = await _productRepository.GetByIdAsync(productId);
        if (installed is null)
            return Error($"Product '{productId}' is not installed.");

        Console.WriteLine($"Uninstalling {installed.Title} v{installed.Version}");
        await _engine.UninstallAsync(installed);
        Console.WriteLine($"Successfully uninstalled {installed.Title}");
        return 0;
    }

    private async Task<int> UpdateAsync(string[] args)
    {
        if (args.Length < 4)
            return Error("Missing product ID. Usage: storkdrop --cli update <productId>");

        string productId = args[3];
        string? version = GetFlag(args, "--version");
        Dictionary<string, string> configValues = ParseConfigValues(args);

        InstalledProduct? installed = await _productRepository.GetByIdAsync(productId);
        if (installed is null)
            return Error($"Product '{productId}' is not installed.");

        (ProductManifest? manifest, string? feedId) = await FindManifestInFeedsAsync(
            productId,
            version
        );
        if (manifest is null || feedId is null)
        {
            return version is not null
                ? Error($"Version '{version}' not found for product '{productId}'.")
                : Error($"No update found for product '{productId}' in any configured feed.");
        }

        int configError = await ValidatePluginConfigAsync(manifest, feedId, configValues);
        if (configError != 0)
            return configError;

        SetupPluginConfigCallbacks(configValues);

        Console.WriteLine(
            $"Updating {manifest.Title} from v{installed.Version} to v{manifest.Version}"
        );

        InstallOptions options = new InstallOptions(
            TargetPath: installed.InstalledPath,
            FeedId: feedId,
            PluginConfigValues: configValues.Count > 0 ? configValues : null
        );
        Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
        {
            if (!string.IsNullOrEmpty(p.Message))
                Console.WriteLine($"[{p.Percentage}%] {p.Message}");
        });

        InstallResult result = await _coordinator.UpdateWithIsolationAsync(
            installed,
            manifest,
            options,
            progress,
            CancellationToken.None
        );

        if (!result.Success)
            return Error($"Update failed: {result.ErrorMessage}");

        Console.WriteLine($"Successfully updated {manifest.Title} to v{manifest.Version}");
        return 0;
    }

    private async Task<int> ReExecuteAsync(string[] args)
    {
        if (args.Length < 4)
            return Error("Missing product ID. Usage: storkdrop --cli re-execute <productId>");

        string productId = args[3];
        Dictionary<string, string> configValues = ParseConfigValues(args);
        bool skipPre = args.Any(a => a == "--skip-pre");
        bool skipPost = args.Any(a => a == "--skip-post");
        bool runFiles = args.Any(a => a == "--run-files");

        InstalledProduct? installed = await _productRepository.GetByIdAsync(productId);
        if (installed is null)
            return Error($"Product '{productId}' is not installed.");

        if (configValues.Count > 0)
            SetupPluginConfigCallbacks(configValues);

        Console.WriteLine(
            $"Re-executing plugin actions for {installed.Title} v{installed.Version}"
        );

        ReExecuteOptions reExecuteOptions = new ReExecuteOptions
        {
            RunPreInstall = !skipPre,
            RunPostInstall = !skipPost,
            RunFileHandlers = runFiles,
            PluginConfigValues = configValues.Count > 0 ? configValues : null,
        };

        Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
        {
            if (!string.IsNullOrEmpty(p.Message))
                Console.WriteLine($"[{p.Percentage}%] {p.Message}");
        });

        InstallResult result = await _coordinator.ReExecutePluginsWithIsolationAsync(
            installed,
            reExecuteOptions,
            progress,
            CancellationToken.None
        );

        if (!result.Success)
            return Error($"Plugin actions failed: {result.ErrorMessage}");

        Console.WriteLine($"Successfully re-executed plugin actions for {installed.Title}");
        return 0;
    }

    private async Task<int> ListAsync()
    {
        Console.WriteLine(
            $"{"Product ID", -40} {"Title", -30} {"Version", -12} {"Feed", -20} {"Type"}"
        );
        Console.WriteLine(new string('-', 115));

        foreach (FeedInfo feed in _feedRegistry.GetFeeds())
        {
            try
            {
                IRegistryClient client = _feedRegistry.GetClient(feed.Id);
                IReadOnlyList<ProductManifest> products = await client.GetAllProductsAsync();
                foreach (ProductManifest p in products)
                {
                    Console.WriteLine(
                        $"{p.ProductId, -40} {p.Title, -30} {p.Version, -12} {feed.Name, -20} {p.InstallType}"
                    );
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Failed to load products from feed '{feed.Name}': {ex.Message}"
                );
            }
        }

        return 0;
    }

    private async Task<int> VersionsAsync(string[] args)
    {
        if (args.Length < 4)
            return Error("Missing product ID. Usage: storkdrop --cli versions <productId>");

        string productId = args[3];
        bool found = false;

        foreach (FeedInfo feed in _feedRegistry.GetFeeds())
        {
            try
            {
                IRegistryClient client = _feedRegistry.GetClient(feed.Id);
                IReadOnlyList<string> versions = await client.GetAvailableVersionsAsync(productId);
                if (versions.Count > 0)
                {
                    found = true;
                    Console.WriteLine($"Versions for '{productId}' on feed '{feed.Name}':");
                    foreach (string v in versions)
                        Console.WriteLine($"  {v}");
                }
            }
            catch { }
        }

        if (!found)
            return Error($"Product '{productId}' not found in any configured feed.");

        return 0;
    }

    private async Task<(ProductManifest? Manifest, string? FeedId)> FindManifestInFeedsAsync(
        string productId,
        string? version
    )
    {
        foreach (FeedInfo feed in _feedRegistry.GetFeeds())
        {
            try
            {
                IRegistryClient client = _feedRegistry.GetClient(feed.Id);
                ProductManifest? manifest = version is not null
                    ? await client.GetProductManifestAsync(productId, version)
                    : await client.GetProductManifestAsync(productId);

                if (manifest is not null)
                    return (manifest, feed.Id);
            }
            catch { }
        }

        return (null, null);
    }

    private async Task<int> ValidatePluginConfigAsync(
        ProductManifest manifest,
        string feedId,
        Dictionary<string, string> configValues
    )
    {
        if (manifest.Plugins is not { Length: > 0 })
            return 0;

        IReadOnlyList<PluginConfigField> schema = await _engine.GetPluginConfigurationAsync(
            manifest,
            feedId
        );

        List<string> missing = schema
            .Where(f => f.Required && !configValues.ContainsKey(f.Key))
            .Select(f => $"  --config {f.Key}=<value>  ({f.Label})")
            .ToList();

        if (missing.Count == 0)
            return 0;

        Console.Error.WriteLine("Missing required plugin configuration:");
        foreach (string m in missing)
            Console.Error.WriteLine(m);
        return 1;
    }

    private void SetupPluginConfigCallbacks(Dictionary<string, string> configValues)
    {
        _engine.OnPluginConfigNeeded = (fields, currentValues) =>
            configValues.Count > 0 ? configValues : null;

        _engine.OnFileHandlerConfigNeeded = (fields, currentValues) =>
            configValues.Count > 0 ? configValues : null;
    }

    private static Dictionary<string, string> ParseConfigValues(string[] args)
    {
        Dictionary<string, string> values = new();

        string? configFilePath = GetFlag(args, "--config-file");
        if (configFilePath is not null)
        {
            if (!File.Exists(configFilePath))
            {
                Console.Error.WriteLine($"Config file not found: {configFilePath}");
            }
            else
            {
                try
                {
                    string json = File.ReadAllText(configFilePath);
                    Dictionary<string, string>? fileValues = JsonSerializer.Deserialize<
                        Dictionary<string, string>
                    >(json);
                    if (fileValues is not null)
                    {
                        foreach ((string key, string value) in fileValues)
                            values[key] = value;
                    }
                }
                catch (JsonException ex)
                {
                    Console.Error.WriteLine($"Failed to parse config file: {ex.Message}");
                }
            }
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--config" && i + 1 < args.Length)
            {
                string pair = args[i + 1];
                int eqIndex = pair.IndexOf('=');
                if (eqIndex > 0)
                    values[pair[..eqIndex]] = pair[(eqIndex + 1)..];
                else
                    Console.Error.WriteLine($"Invalid config format '{pair}'. Expected key=value.");
                i++;
            }
        }

        return values;
    }

    private static string? GetFlag(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == flag)
                return args[i + 1];
        }
        return null;
    }

    private static int Error(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("StorkDrop CLI");
        Console.WriteLine();
        Console.WriteLine("Usage: storkdrop --cli <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  install <productId>      Install a product");
        Console.WriteLine("  uninstall <productId>    Uninstall a product");
        Console.WriteLine("  update <productId>       Update an installed product");
        Console.WriteLine(
            "  re-execute <productId>   Re-run plugin actions on an installed product"
        );
        Console.WriteLine("  list                     List all available products");
        Console.WriteLine("  versions <productId>     List available versions for a product");
        Console.WriteLine("  help [command]           Show help for a command");
        Console.WriteLine();
        Console.WriteLine(
            "Run 'storkdrop --cli help <command>' for details on a specific command."
        );
    }

    private static int PrintCommandHelp(string? command)
    {
        switch (command?.ToLowerInvariant())
        {
            case "install":
                Console.WriteLine("Usage: storkdrop --cli install <productId> [options]");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine(
                    "  --version <version>     Install a specific version (default: latest)"
                );
                Console.WriteLine(
                    "  --path <path>           Install path (default: manifest's recommendedInstallPath)"
                );
                Console.WriteLine("  --config-file <path>    JSON file with plugin config values");
                Console.WriteLine(
                    "  --config key=value      Set a plugin config value (repeatable)"
                );
                Console.WriteLine();
                Console.WriteLine("Config file format:");
                Console.WriteLine("  {");
                Console.WriteLine("    \"target-database\": \"Production\",");
                Console.WriteLine("    \"smtp-server\": \"mail.example.com\"");
                Console.WriteLine("  }");
                break;

            case "uninstall":
                Console.WriteLine("Usage: storkdrop --cli uninstall <productId>");
                break;

            case "update":
                Console.WriteLine("Usage: storkdrop --cli update <productId> [options]");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine(
                    "  --version <version>     Update to a specific version (default: latest)"
                );
                Console.WriteLine("  --config-file <path>    JSON file with plugin config values");
                Console.WriteLine(
                    "  --config key=value      Set a plugin config value (repeatable)"
                );
                break;

            case "re-execute":
                Console.WriteLine("Usage: storkdrop --cli re-execute <productId> [options]");
                Console.WriteLine();
                Console.WriteLine(
                    "Re-runs plugin actions (PreInstall + PostInstall) on an installed product."
                );
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --config-file <path>    JSON file with plugin config values");
                Console.WriteLine(
                    "  --config key=value      Set a plugin config value (repeatable)"
                );
                Console.WriteLine("  --skip-pre              Skip the PreInstall phase");
                Console.WriteLine("  --skip-post             Skip the PostInstall phase");
                Console.WriteLine(
                    "  --run-files             Also run file handlers (requires .stork/files/)"
                );
                break;

            case "list":
                Console.WriteLine("Usage: storkdrop --cli list");
                Console.WriteLine();
                Console.WriteLine("Lists all available products from all configured feeds.");
                break;

            case "versions":
                Console.WriteLine("Usage: storkdrop --cli versions <productId>");
                Console.WriteLine();
                Console.WriteLine("Lists all available versions for a product across all feeds.");
                break;

            default:
                PrintHelp();
                break;
        }

        return 0;
    }
}
