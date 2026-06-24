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
    private readonly IConfigurationService _configurationService;
    private readonly IEncryptionService _encryptionService;

    public CliRunner(IServiceProvider services)
    {
        _feedRegistry = services.GetRequiredService<IFeedRegistry>();
        _engine = services.GetRequiredService<IInstallationEngine>();
        _coordinator = services.GetRequiredService<InstallationCoordinator>();
        _productRepository = services.GetRequiredService<IProductRepository>();
        _configurationService = services.GetRequiredService<IConfigurationService>();
        _encryptionService = services.GetRequiredService<IEncryptionService>();
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
                "apply" => await ApplyAsync(args),
                "add-feed" => await AddFeedAsync(args),
                "remove-feed" => await RemoveFeedAsync(args),
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
            return Error(
                "Missing product ID. Usage: storkdrop --cli install <productId> [--instance <id>]"
            );

        string productId = args[3];
        string? version = GetFlag(args, "--version");
        string? path = GetFlag(args, "--path");
        string instanceId = GetFlag(args, "--instance") ?? InstanceIdHelper.DefaultInstanceId;
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
            InstanceId: instanceId,
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
            return Error(
                "Missing product ID. Usage: storkdrop --cli uninstall <productId> [--instance <id>]"
            );

        string productId = args[3];
        InstalledProduct? installed = await FindInstalledProduct(productId, args);
        if (installed is null)
            return Error($"Product '{productId}' is not installed.");

        Console.WriteLine(
            $"Uninstalling {installed.Title} v{installed.Version} ({installed.InstanceUniqueId})"
        );
        await _engine.UninstallAsync(installed);
        Console.WriteLine($"Successfully uninstalled {installed.Title}");
        return 0;
    }

    private async Task<int> UpdateAsync(string[] args)
    {
        if (args.Length < 4)
            return Error(
                "Missing product ID. Usage: storkdrop --cli update <productId> [--instance <id>]"
            );

        string productId = args[3];
        string? version = GetFlag(args, "--version");
        Dictionary<string, string> configValues = ParseConfigValues(args);

        InstalledProduct? installed = await FindInstalledProduct(productId, args);
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
            return Error(
                "Missing product ID. Usage: storkdrop --cli re-execute <productId> [--instance <id>]"
            );

        string productId = args[3];
        Dictionary<string, string> configValues = ParseConfigValues(args);
        bool skipPre = args.Any(a => a == "--skip-pre");
        bool skipPost = args.Any(a => a == "--skip-post");
        bool runFiles = args.Any(a => a == "--run-files");

        InstalledProduct? installed = await FindInstalledProduct(productId, args);
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

    private async Task<int> AddFeedAsync(string[] args)
    {
        string? url = GetFlag(args, "--url");
        if (string.IsNullOrWhiteSpace(url))
            return Error(
                "Missing --url. Usage: storkdrop --cli add-feed --url <url> [--id <id>] [--name <name>] [--repo <repo>] [--user <u>] [--password <p>]"
            );

        string? id = GetFlag(args, "--id");
        string? name = GetFlag(args, "--name");
        string? repo = GetFlag(args, "--repo");
        string? user = GetFlag(args, "--user");
        string? password = GetFlag(args, "--password");

        AppConfiguration config = await _configurationService.LoadAsync() ?? DefaultConfiguration();

        List<FeedConfiguration> feeds = config.Feeds.ToList();
        feeds.RemoveAll(f =>
            (id is not null && string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase))
            || (
                string.Equals(f.Url, url, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.Repository ?? "", repo ?? "", StringComparison.OrdinalIgnoreCase)
            )
        );

        FeedConfiguration feed = new(
            Id: id ?? Guid.NewGuid().ToString(),
            Name: name ?? new Uri(url).Host,
            Url: url,
            Repository: string.IsNullOrWhiteSpace(repo) ? null : repo,
            Username: string.IsNullOrWhiteSpace(user) ? null : user,
            EncryptedPassword: string.IsNullOrEmpty(password)
                ? null
                : _encryptionService.Encrypt(password),
            PluginId: null
        );
        feeds.Add(feed);

        await _configurationService.SaveAsync(config with { Feeds = feeds.ToArray() });
        await _feedRegistry.ReloadAsync();

        Console.WriteLine($"Added feed '{feed.Name}' ({feed.Id}) -> {feed.Url}");

        bool ok = await _feedRegistry.TestConnectionAsync(feed.Id);
        Console.WriteLine(ok ? "Connection OK." : "Warning: connection test failed.");
        return 0;
    }

    private async Task<int> RemoveFeedAsync(string[] args)
    {
        if (args.Length < 4)
            return Error(
                "Missing feed identifier. Usage: storkdrop --cli remove-feed <id|name|url>"
            );

        string token = args[3];
        AppConfiguration? config = await _configurationService.LoadAsync();
        if (config is null)
            return Error("No configuration found.");

        List<FeedConfiguration> feeds = config.Feeds.ToList();
        int removed = feeds.RemoveAll(f =>
            string.Equals(f.Id, token, StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.Name, token, StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.Url, token, StringComparison.OrdinalIgnoreCase)
        );

        if (removed == 0)
            return Error($"No feed matched '{token}'.");

        await _configurationService.SaveAsync(config with { Feeds = feeds.ToArray() });
        await _feedRegistry.ReloadAsync();
        Console.WriteLine($"Removed {removed} feed(s) matching '{token}'.");
        return 0;
    }

    private async Task<int> ApplyAsync(string[] args)
    {
        if (args.Length < 4)
            return Error(
                "Missing manifest path. Usage: storkdrop --cli apply <manifest.json> [--report <path>] [--continue-on-error]"
            );

        string manifestPath = args[3];
        if (!File.Exists(manifestPath))
            return Error($"Manifest file not found: {manifestPath}");

        bool continueOnError = args.Any(a => a == "--continue-on-error");
        string reportPath =
            GetFlag(args, "--report")
            ?? Path.Combine(Path.GetTempPath(), "storkdrop-apply-result.json");

        EnvironmentManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<EnvironmentManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }
        catch (JsonException ex)
        {
            return Error($"Failed to parse manifest: {ex.Message}");
        }

        if (manifest is null || manifest.Products.Count == 0)
            return Error("Manifest contains no products.");

        List<PlanNode> plan = await BuildInstallPlanAsync(manifest);

        EnvironmentApplyReport report = new EnvironmentApplyReport();
        bool allOk = true;

        foreach (PlanNode node in plan)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            EnvironmentApplyStep step = new EnvironmentApplyStep
            {
                Id = node.Id,
                Version = node.Manifest?.Version,
            };

            if (node.Manifest is null || node.FeedId is null)
            {
                step.Ok = false;
                step.Error = node.ResolveError ?? "Product not found in any configured feed.";
            }
            else
            {
                (bool ok, string? err) = await InstallPlanNodeAsync(node);
                step.Ok = ok;
                step.Error = err;
            }

            sw.Stop();
            step.DurationMs = sw.ElapsedMilliseconds;
            report.Steps.Add(step);

            Console.WriteLine(
                step.Ok ? $"[OK]   {step.Id} {step.Version}" : $"[FAIL] {step.Id}: {step.Error}"
            );

            if (!step.Ok)
            {
                allOk = false;
                if (!continueOnError)
                    break;
            }
        }

        report.Success = allOk;
        WriteReport(reportPath, report);
        Console.WriteLine($"Apply report written to {reportPath}");
        return allOk ? 0 : 1;
    }

    private async Task<(bool Ok, string? Error)> InstallPlanNodeAsync(PlanNode node)
    {
        try
        {
            ProductManifest manifest = node.Manifest!;
            string targetPath = node.Path ?? manifest.RecommendedInstallPath ?? string.Empty;
            if (string.IsNullOrEmpty(targetPath))
                return (false, "No install path and manifest has no recommended path.");

            SetupPluginConfigCallbacks(node.Config);

            InstallOptions options = new InstallOptions(
                TargetPath: targetPath,
                InstanceId: InstanceIdHelper.DefaultInstanceId,
                FeedId: node.FeedId!,
                PluginConfigValues: node.Config.Count > 0 ? node.Config : null
            );

            InstallResult result = await _coordinator.InstallWithIsolationAsync(
                manifest,
                options,
                new Progress<InstallProgress>(p =>
                {
                    if (!string.IsNullOrEmpty(p.Message))
                        Console.WriteLine($"  [{p.Percentage}%] {p.Message}");
                }),
                CancellationToken.None
            );

            return result.Success ? (true, null) : (false, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task<List<PlanNode>> BuildInstallPlanAsync(EnvironmentManifest manifest)
    {
        Dictionary<string, PlanNode> nodes = new Dictionary<string, PlanNode>(
            StringComparer.OrdinalIgnoreCase
        );
        Queue<string> toResolve = new Queue<string>();

        foreach (EnvironmentManifestProduct p in manifest.Products)
        {
            if (nodes.ContainsKey(p.Id))
                continue;
            nodes[p.Id] = new PlanNode
            {
                Id = p.Id,
                Version = p.Version,
                Path = p.Path,
                Config = p.Config ?? new Dictionary<string, string>(),
            };
            toResolve.Enqueue(p.Id);
        }

        while (toResolve.Count > 0)
        {
            string id = toResolve.Dequeue();
            PlanNode node = nodes[id];
            (ProductManifest? m, string? feedId) = await FindManifestInFeedsAsync(id, node.Version);
            node.Manifest = m;
            node.FeedId = feedId;
            if (m is null)
            {
                node.ResolveError = $"Product '{id}' not found in any configured feed.";
                continue;
            }

            foreach (string required in m.RequiredProductIds ?? [])
            {
                if (nodes.ContainsKey(required))
                    continue;
                nodes[required] = new PlanNode { Id = required };
                toResolve.Enqueue(required);
            }
        }

        return TopologicalSort(nodes);
    }

    private static List<PlanNode> TopologicalSort(Dictionary<string, PlanNode> nodes)
    {
        List<PlanNode> ordered = new List<PlanNode>();
        HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> inProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(PlanNode node)
        {
            if (visited.Contains(node.Id))
                return;
            if (!inProgress.Add(node.Id))
                return;
            foreach (string req in node.Manifest?.RequiredProductIds ?? [])
            {
                if (nodes.TryGetValue(req, out PlanNode? dep))
                    Visit(dep);
            }
            inProgress.Remove(node.Id);
            visited.Add(node.Id);
            ordered.Add(node);
        }

        foreach (PlanNode node in nodes.Values)
            Visit(node);

        return ordered;
    }

    private static void WriteReport(string path, EnvironmentApplyReport report)
    {
        try
        {
            string json = JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                }
            );
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to write apply report: {ex.Message}");
        }
    }

    private static AppConfiguration DefaultConfiguration() =>
        new AppConfiguration(
            Feeds: [],
            AutoStart: false,
            AutoCheckForUpdates: true,
            CheckInterval: TimeSpan.FromHours(6)
        );

    private sealed class PlanNode
    {
        public string Id { get; set; } = "";
        public string? Version { get; set; }
        public string? Path { get; set; }
        public Dictionary<string, string> Config { get; set; } = new Dictionary<string, string>();
        public ProductManifest? Manifest { get; set; }
        public string? FeedId { get; set; }
        public string? ResolveError { get; set; }
    }

    private async Task<int> ListAsync()
    {
        IReadOnlyList<InstalledProduct> installed = await _productRepository.GetAllAsync();
        bool hasNonDefault = installed.Any(p => p.InstanceId != InstanceIdHelper.DefaultInstanceId);
        bool hasUniqueId = installed.Any(p => !string.IsNullOrEmpty(p.InstanceUniqueId));

        if (hasNonDefault)
        {
            if (hasUniqueId)
            {
                Console.WriteLine(
                    $"{"Product ID", -40} {"Title", -30} {"Version", -12} {"Instance", -16} {"UniqueId", -12} {"Feed", -20} {"Type"}"
                );
                Console.WriteLine(new string('-', 147));
            }
            else
            {
                Console.WriteLine(
                    $"{"Product ID", -40} {"Title", -30} {"Version", -12} {"Instance", -16} {"Feed", -20} {"Type"}"
                );
                Console.WriteLine(new string('-', 135));
            }
        }
        else
        {
            if (hasUniqueId)
            {
                Console.WriteLine(
                    $"{"Product ID", -40} {"Title", -30} {"Version", -12} {"UniqueId", -12} {"Feed", -20} {"Type"}"
                );
                Console.WriteLine(new string('-', 127));
            }
            else
            {
                Console.WriteLine(
                    $"{"Product ID", -40} {"Title", -30} {"Version", -12} {"Feed", -20} {"Type"}"
                );
                Console.WriteLine(new string('-', 115));
            }
        }

        foreach (FeedInfo feed in _feedRegistry.GetFeeds())
        {
            try
            {
                IRegistryClient client = _feedRegistry.GetClient(feed.Id);
                IReadOnlyList<ProductManifest> products = await client.GetAllProductsAsync();
                foreach (ProductManifest p in products)
                {
                    if (hasNonDefault)
                    {
                        // Find installed instances for this product to show instance info
                        IReadOnlyList<InstalledProduct> instances =
                            await _productRepository.GetInstancesAsync(p.ProductId);
                        if (instances.Count > 0)
                        {
                            foreach (InstalledProduct inst in instances)
                            {
                                if (hasUniqueId)
                                {
                                    Console.WriteLine(
                                        $"{p.ProductId, -40} {p.Title, -30} {inst.Version, -12} {inst.InstanceId, -16} {inst.InstanceUniqueId ?? "", -12} {feed.Name, -20} {p.InstallType}"
                                    );
                                }
                                else
                                {
                                    Console.WriteLine(
                                        $"{p.ProductId, -40} {p.Title, -30} {inst.Version, -12} {inst.InstanceId, -16} {feed.Name, -20} {p.InstallType}"
                                    );
                                }
                            }
                        }
                        else
                        {
                            if (hasUniqueId)
                            {
                                Console.WriteLine(
                                    $"{p.ProductId, -40} {p.Title, -30} {p.Version, -12} {"", -16} {"", -12} {feed.Name, -20} {p.InstallType}"
                                );
                            }
                            else
                            {
                                Console.WriteLine(
                                    $"{p.ProductId, -40} {p.Title, -30} {p.Version, -12} {"", -16} {feed.Name, -20} {p.InstallType}"
                                );
                            }
                        }
                    }
                    else
                    {
                        if (hasUniqueId)
                        {
                            InstalledProduct? inst = installed.FirstOrDefault(i =>
                                i.ProductId == p.ProductId
                            );
                            Console.WriteLine(
                                $"{p.ProductId, -40} {p.Title, -30} {p.Version, -12} {inst?.InstanceUniqueId ?? "", -12} {feed.Name, -20} {p.InstallType}"
                            );
                        }
                        else
                        {
                            Console.WriteLine(
                                $"{p.ProductId, -40} {p.Title, -30} {p.Version, -12} {feed.Name, -20} {p.InstallType}"
                            );
                        }
                    }
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

    private async Task<InstalledProduct?> FindInstalledProduct(string productId, string[] args)
    {
        string? uniqueId = GetFlag(args, "--id");
        if (uniqueId is not null)
        {
            IReadOnlyList<InstalledProduct> instances = await _productRepository.GetInstancesAsync(
                productId
            );
            return instances.FirstOrDefault(p => p.InstanceUniqueId == uniqueId);
        }

        string instanceId = GetFlag(args, "--instance") ?? InstanceIdHelper.DefaultInstanceId;
        return await _productRepository.GetByIdAsync(productId, instanceId);
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
        Console.WriteLine(
            "  apply <manifest.json>    Install an ordered set of products (env manifest)"
        );
        Console.WriteLine("  add-feed --url <url>     Register a feed (encrypts password locally)");
        Console.WriteLine("  remove-feed <id|name|url> Remove a registered feed");
        Console.WriteLine("  list                     List all available products");
        Console.WriteLine("  versions <productId>     List available versions for a product");
        Console.WriteLine("  help [command]           Show help for a command");
        Console.WriteLine();
        Console.WriteLine("Global Options:");
        Console.WriteLine(
            "  --instance <id>          Target a specific instance (default: \"default\")"
        );
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
                Console.WriteLine("  --instance <id>         Instance name (default: \"default\")");
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
                Console.WriteLine("Usage: storkdrop --cli uninstall <productId> [options]");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --instance <id>         Instance name (default: \"default\")");
                break;

            case "update":
                Console.WriteLine("Usage: storkdrop --cli update <productId> [options]");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine(
                    "  --version <version>     Update to a specific version (default: latest)"
                );
                Console.WriteLine("  --instance <id>         Instance name (default: \"default\")");
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
                Console.WriteLine("  --instance <id>         Instance name (default: \"default\")");
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

            case "apply":
                Console.WriteLine("Usage: storkdrop --cli apply <manifest.json> [options]");
                Console.WriteLine();
                Console.WriteLine(
                    "Installs an ordered set of products described by an environment manifest."
                );
                Console.WriteLine(
                    "Required products (RequiredProductIds) are resolved and installed first."
                );
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine(
                    "  --report <path>         Where to write the JSON result report"
                );
                Console.WriteLine(
                    "                          (default: %TEMP%/storkdrop-apply-result.json)"
                );
                Console.WriteLine("  --continue-on-error     Keep going after a failed product");
                Console.WriteLine();
                Console.WriteLine("Manifest format:");
                Console.WriteLine(
                    "  { \"products\": [ { \"id\": \"my-product\", \"version\": \"1.0.0\","
                );
                Console.WriteLine(
                    "                    \"config\": { \"target-database\": \"Test\" } } ] }"
                );
                break;

            case "add-feed":
                Console.WriteLine("Usage: storkdrop --cli add-feed --url <url> [options]");
                Console.WriteLine();
                Console.WriteLine(
                    "Registers a Nexus feed and encrypts the password locally (DPAPI on Windows)."
                );
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine(
                    "  --id <id>               Feed id (default: generated); replaces an existing feed with the same id"
                );
                Console.WriteLine("  --name <name>           Display name (default: url host)");
                Console.WriteLine("  --repo <repository>     Nexus repository");
                Console.WriteLine("  --user <username>       Feed username");
                Console.WriteLine("  --password <password>   Feed password (stored encrypted)");
                break;

            case "remove-feed":
                Console.WriteLine("Usage: storkdrop --cli remove-feed <id|name|url>");
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
