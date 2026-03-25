<p align="center">
  <img src="assets/stork_icon_256.png" alt="StorkDrop" width="128" />
  <br />
  <strong>StorkDrop</strong>
  <br />
  <em>Manifest-driven software deployment for servers and workstations</em>
</p>

---

<p align="center">
  <img src="docs/images/marketplace.png" alt="StorkDrop Marketplace" width="800" />
  <br />
  <em>The StorkDrop marketplace  - browse, install, and update products from multiple feeds</em>
</p>

## What is StorkDrop?

StorkDrop is a self-contained deployment client that installs, updates, and manages software from Nexus OSS registries. Products declare their install behavior in a JSON manifest - no custom installer needed.

It's built for **server software and business tools** that:

- Need regular updates on production servers
- Require custom pre/post-install logic (database migrations, config writes, service restarts)
- Are deployed to multiple machines with slightly different configurations
- Don't justify building a full MSI/NSIS installer for each release

## When to use StorkDrop

| Scenario                          | StorkDrop                           | Traditional installer (MSI/NSIS/Inno) | Fleet management (SCCM/Intune) | Package manager (Chocolatey/WinGet) |
| --------------------------------- | ----------------------------------- | ------------------------------------- | ------------------------------ | ----------------------------------- |
| **Server-side business apps**     | ✅ Built for this                   | ❌ Manual per-machine                 | ⚠️ Overkill                    | ⚠️ No custom logic                  |
| **Custom pre/post install logic** | ✅ Plugin system with dynamic UI    | ❌ Baked into installer               | ❌ Scripts only                | ❌ Scripts only                     |
| **User chooses install path**     | ✅ Per-install dialog               | ✅ Wizard                             | ❌ Centrally managed           | ❌ Fixed paths                      |
| **Multiple feeds/repositories**   | ✅ Multi-feed with per-feed plugins | ❌ N/A                                | ✅ Multiple sources            | ✅ Multiple sources                 |
| **Self-service marketplace**      | ✅ Browse, search, filter           | ❌ No discovery                       | ⚠️ Company portal              | ✅ CLI search                       |
| **Version rollback**              | ✅ Backup + restore                 | ❌ Manual                             | ⚠️ Complex                     | ⚠️ Limited                          |
| **Update notifications**          | ✅ Tray + toast                     | ❌ None                               | ✅ Managed                     | ⚠️ CLI only                         |
| **File-in-use handling**          | ✅ Rename + reboot cleanup          | ⚠️ Varies                             | ✅ Managed                     | ❌ None                             |
| **Plugin extensibility**          | ✅ Dynamic config UI, custom tabs   | ❌ None                               | ❌ None                        | ❌ None                             |
| **Cross-product consistency**     | ✅ One tool, many products          | ❌ Different installer per product    | ✅ Unified                     | ✅ Unified                          |
| **Setup complexity**              | Low - single exe                    | Medium - per product                  | High - infrastructure          | Low - CLI                           |
| **Infrastructure required**       | Nexus OSS (free)                    | None                                  | AD + SCCM/Intune               | Repository                          |

## How it works

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│  Nexus OSS   │     │  StorkDrop   │     │   Target     │
│  (raw repo)  │────>│   Client     │────>│   Machine    │
│              │     │              │     │              │
│  manifest.json     │  Marketplace │     │  C:\Program  │
│  product.zip │     │  Install UI  │     │  Files\...   │
│              │     │  Plugin hooks│     │  Shortcuts   │
└──────────────┘     └──────────────┘     └──────────────┘
```

1. **Publish** your product to a Nexus raw repository with a `manifest.json`
2. **Users** open StorkDrop, browse the marketplace, click Install
3. **StorkDrop** downloads, extracts, runs plugin hooks, creates shortcuts
4. **Updates** are detected automatically and applied with backup/rollback

## Key features

- **Manifest-driven** - no custom installer code, just a JSON file declaring what to install
- **Plugin system** - pre/post install hooks with dynamic configuration UI (10 field types)
- **Multi-feed** - connect to multiple Nexus repositories, each with their own plugin
- **File tracking** - knows exactly which files it installed for clean uninstall
- **File-in-use handling** - renames locked files, schedules cleanup on reboot
- **UAC on demand** - runs as normal user, elevates only when installing to protected paths
- **Serilog logging** - configurable log level, rolling daily files
- **EN/DE** - full English and German translations
- **System tray** - background update checking with toast notifications

## Product manifest

Products are stored in Nexus raw repositories:

```
my-product/
  manifest.json                    Latest manifest
  versions/1.0.0/
    manifest.json                  Version-specific
    my-product-1.0.0.zip           Artifact
```

```jsonc
{
  "productId": "my-product", // Unique ID, used in URLs and file paths
  "title": "My Product", // Display name shown in the marketplace
  "version": "1.0.0", // Semantic version, used for update detection
  "releaseDate": "2026-03-24", // Shown in product detail view
  "installType": "Suite", // Marketplace filter category: Plugin | Suite | Bundle
  "description": "What this does", // Short text shown on the product card
  "releaseNotes": "What's new", // Markdown shown in product detail / update view
  "recommendedInstallPath": "C:\\...", // Pre-filled in the install dialog (user can change)
  "publisher": "My Company", // Marketplace filter + shown in product detail
  "imageUrl": "https://..../icon.png", // Product card image (optional, falls back to icon)
  "downloadSizeBytes": 52428800, // Shown in install dialog next to available disk space
  "requirements": ["Windows 10+"], // Shown in product detail as prerequisites
  "shortcuts": [
    // Start Menu shortcuts created after install
    { "exeName": "MyProduct.exe", "displayName": "My Product" },
    { "exeName": "MyAdmin.exe", "displayName": "My Product Admin" },
  ],
  "shortcutFolder": "My Company", // Start Menu subfolder (default: "StorkDrop")
  "environmentVariables": [
    // Environment variables to set or modify on install (removed on uninstall)
    { "name": "MY_PRODUCT_HOME", "value": "{InstallPath}", "action": "set" },
    { "name": "PATH", "value": "{InstallPath}\\bin", "action": "append", "mustExist": true }
  ],
  "plugins": [
    // DLLs with pre/post install logic (optional)
    { "assembly": "MyProduct.dll", "typeName": "MyProduct.Installer" },
  ],
  "cleanup": {
    // Paths to clean up on uninstall (optional)
    "registryKeys": [], // Registry keys to delete
    "dataLocations": ["%APPDATA%\\MyProduct"], // Data folders to delete
  },
}
```

## Plugin system

### App-level plugins (`IStorkDropPlugin`)

Extend StorkDrop with custom setup wizard steps, settings, navigation tabs, and install hooks. Drop the DLL in the `plugins/` directory next to StorkDrop - it's loaded automatically.

```csharp
public class MyPlugin : IStorkDropPlugin
{
    public string PluginId => "my-plugin";
    public string DisplayName => "My Plugin";
    public string[] AssociatedFeeds => new[] { "https://nexus.example.com" };

    // Add custom pages to the sidebar (see "Navigation tabs" below)
    public IReadOnlyList<PluginNavTab> GetNavigationTabs() => new[]
    {
        new PluginNavTab { TabId = "status", DisplayName = "Server Status", Icon = "\uE8A5" }
    };

    // Called when the user clicks your tab - render your content here
    public void OnNavigationTabSelected(string tabId)
    {
        // tabId == "status" → show your custom UI
        // StorkDrop passes a content area you can populate
    }

    // Called when a product from your associated feed is installed
    public async Task OnProductInstalledAsync(PluginInstallContext context, CancellationToken ct)
    {
        // e.g. run SQL scripts found in the package, register services, etc.
        foreach (string sqlFile in Directory.GetFiles(context.InstallPath, "*.sql"))
        {
            // Execute against configured database...
        }
    }

    // ... setup steps, settings, uninstall hooks
}
```

#### Navigation tabs

Plugins can add their own pages to the StorkDrop sidebar. This is useful for plugins that manage external state (database connections, service status, etc.) that users need to monitor independently of individual products.

**How it works:**

1. `GetNavigationTabs()` declares one or more tabs with an ID, label, and icon
2. StorkDrop renders them in the sidebar under a "Plugins" section
3. When the user clicks the tab, `OnNavigationTabSelected(tabId)` is called
4. The plugin can then show status information, run diagnostics, or provide a management UI

**Example use cases:**

- A database plugin showing connection status and letting users run health checks
- A service manager plugin showing which Windows services are running
- A monitoring plugin showing server metrics from installed products

#### Custom file type handlers (`IFileTypeHandler`)

Plugins can claim specific file types from product packages. Files matching the registered extensions are **not copied** to the install directory - the plugin handles them entirely.

```csharp
public class MyPlugin : IStorkDropPlugin, IFileTypeHandler
{
    // ... IStorkDropPlugin members ...

    // Claim .sql and .migration files
    public IReadOnlyList<string> HandledExtensions => new[] { ".sql", ".migration" };

    public async Task<FileHandlerResult> HandleFilesAsync(
        IReadOnlyList<string> files,
        PluginContext context,
        CancellationToken ct
    )
    {
        List<FileHandlerFileResult> results = new List<FileHandlerFileResult>();
        string connectionString = context.DatabaseConnections
            .FirstOrDefault(db => db.IsDefault)?.ConnectionString ?? "";

        foreach (string file in files)
        {
            // Execute the SQL file against the configured database
            string sql = await File.ReadAllTextAsync(file, ct);
            // ... execute sql ...
            results.Add(new FileHandlerFileResult
            {
                FilePath = file,
                Success = true,
                Action = $"Executed against database",
            });
        }

        return new FileHandlerResult { Success = true, FileResults = results };
    }
}
```

**How it works:**

1. After extracting the product ZIP, StorkDrop checks all registered `IFileTypeHandler` plugins
2. Files matching `HandledExtensions` are passed to `HandleFilesAsync` (still in the temp directory)
3. Those files are **excluded** from the copy step - they never reach the install directory
4. The plugin decides what to do: run SQL scripts, deploy to a service, register with an API, etc.

**Use cases:**

- SQL migration scripts executed against a database during install
- Configuration templates processed and written to custom locations
- Service registration files deployed to a runtime directory
- Any file type that needs custom handling instead of simple file copy

### Product-level plugins (`IStorkPlugin`)

Products ship a DLL with pre/post install logic and dynamic configuration:

```csharp
public class MyInstaller : IStorkPlugin
{
    public IReadOnlyList<PluginConfigField> GetConfigurationSchema(PluginEnvironment env)
    {
        return new[]
        {
            new PluginConfigField
            {
                Key = "database",
                Label = "Target Database",
                FieldType = PluginFieldType.DatabasePicker,
                Required = true,
            },
        };
    }

    public async Task<PluginPreInstallResult> PreInstallAsync(PluginContext ctx, CancellationToken ct)
    {
        // Validate, prepare - return result instead of throwing
        if (string.IsNullOrEmpty(ctx.ConfigValues["database"]))
            return new PluginPreInstallResult
            {
                Success = false,
                ValidationErrors = new[] { new PluginValidationError("database", "Required") }
            };
        return new PluginPreInstallResult { Success = true };
    }

    public async Task PostInstallAsync(PluginContext ctx, CancellationToken ct)
    {
        // Create DB entries, register components, run migrations
    }

    public async Task<PluginPreInstallResult> PreUninstallAsync(PluginContext ctx, CancellationToken ct)
        => new PluginPreInstallResult { Success = true };

    public async Task PostUninstallAsync(PluginContext ctx, CancellationToken ct)
    {
        // Clean up DB entries
    }
}
```

### Plugin debug support

Test plugins without running StorkDrop:

```csharp
await StorkPluginDebugger.RunAsync<MyInstaller>(
    new PluginContext { ProductId = "test", Version = "1.0.0", InstallPath = @"C:\Test" },
    new Dictionary<string, string> { ["database"] = "MyDb" }
);
```

### Configuration field types

| Type              | Control           | Use case                  |
| ----------------- | ----------------- | ------------------------- |
| `Text`            | TextBox           | Free text                 |
| `Number`          | Validated TextBox | Numeric with min/max      |
| `Dropdown`        | ComboBox          | Single selection          |
| `MultiSelect`     | Checkbox list     | Multiple selections       |
| `Checkbox`        | CheckBox          | Boolean                   |
| `Password`        | PasswordBox       | Sensitive input           |
| `FilePath`        | TextBox + browse  | File selection            |
| `FolderPath`      | TextBox + browse  | Directory selection       |
| `DatabasePicker`  | ComboBox          | From configured databases |
| `StepsPathPicker` | ComboBox          | From configured paths     |

## Environment variables

Products can declare environment variables to set or modify during installation. Changes are tracked and precisely reversed on uninstall.

### Actions

| Action   | Behavior on install                     | Behavior on uninstall             |
| -------- | --------------------------------------- | --------------------------------- |
| `set`    | Creates or overwrites the variable      | Deletes the variable entirely     |
| `append` | Appends a value to the existing variable | Removes only the appended portion |

### Fields

| Field       | Required | Default     | Description                                                  |
| ----------- | -------- | ----------- | ------------------------------------------------------------ |
| `name`      | Yes      |             | Variable name (e.g., `PATH`, `ACME_HOME`)                    |
| `value`     | Yes      |             | Value to set or append. Supports `{InstallPath}` templating  |
| `action`    | No       | `"set"`     | `"set"` or `"append"`                                        |
| `mustExist` | No       | `false`     | For `append`: skip if the variable doesn't already exist     |
| `separator` | No       | `";"`       | Delimiter for `append` (`;` on Windows)                      |
| `target`    | No       | `"machine"` | `"machine"` or `"user"` scope                                |

### Template variables

| Template        | Resolves to                       |
| --------------- | --------------------------------- |
| `{InstallPath}` | The chosen installation directory |

### Example

```jsonc
"environmentVariables": [
  { "name": "ACME_HOME", "value": "{InstallPath}", "action": "set" },
  { "name": "PATH", "value": "{InstallPath}\\bin", "action": "append", "mustExist": true }
]
```

On install to `C:\Program Files\Acme\Dashboard`:
- `ACME_HOME` is set to `C:\Program Files\Acme\Dashboard`
- `C:\Program Files\Acme\Dashboard\bin` is appended to `PATH`

On uninstall:
- `ACME_HOME` is deleted
- Only `C:\Program Files\Acme\Dashboard\bin` is removed from `PATH`, leaving all other entries intact

## Multi-feed support

StorkDrop can connect to multiple Nexus repositories simultaneously. Products from all feeds appear in a unified marketplace with a feed filter dropdown.

### Configuration

Add multiple feeds in Settings or during the setup wizard. Each feed has its own URL, repository name, and optional credentials:

```json
{
  "feeds": [
    { "id": "internal", "name": "Internal Feed", "url": "https://nexus.company.com", "repository": "releases" },
    { "id": "vendor", "name": "Vendor Feed", "url": "https://feed.vendor.com:8443", "repository": "tools" }
  ]
}
```

### How it works

- Each configured feed gets its own dedicated HTTP client with independent authentication
- The marketplace loads products from all feeds in parallel, tagging each product with its source feed
- Installing, updating, and uninstalling always uses the correct feed  - even through UAC elevation
- The feed filter dropdown lets users browse products from a specific feed or all feeds at once
- Installed products remember which feed they came from, so updates are checked against the right source

### Adding a new feed type

The current architecture is designed for extensibility. All feed interactions go through the `IRegistryClient` interface:

```csharp
public interface IRegistryClient
{
    Task<IReadOnlyList<ProductManifest>> GetAllProductsAsync(CancellationToken ct = default);
    Task<ProductManifest?> GetProductManifestAsync(string productId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAvailableVersionsAsync(string productId, CancellationToken ct = default);
    Task<Stream> DownloadProductAsync(string productId, string version, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
```

To add support for a different repository type (Azure Artifacts, GitHub Releases, S3, etc.):

1. Implement `IRegistryClient` for your repository backend
2. Extend `FeedRegistry` to create your client type based on a feed type field in `FeedConfiguration`
3. That's it  - the marketplace, install engine, updates, and all other features work automatically

No changes needed in the UI, plugin system, or installation engine.

## Reliability and safety

StorkDrop is designed for production servers where failed updates are not acceptable.

### Update safety

| Protection | How it works |
|---|---|
| **Backup before update** | Before updating, the current installation is backed up. If the update fails at any step, the backup is automatically restored. |
| **File manifest tracking** | Every installed file is recorded in `{productId}.files.json`. Uninstall removes exactly what was installed  - nothing more, nothing less. |
| **Atomic file writes** | Configuration and product registry files are written to a temp file first, then moved into place. A crash mid-write can't corrupt the data. |
| **Retry with backoff** | File deletions during uninstall/update retry up to 3 times with 500ms delays, handling transient locks from antivirus or indexer. |
| **Environment variable rollback** | Environment variable changes are tracked per-product. Uninstall precisely reverses only what was added  - for PATH-style variables, only the appended segment is removed. |

### File-in-use handling

When an update needs to replace a file that's currently running:

1. The locked file is renamed to `DEL_{guid}_{filename}` in the same directory
2. The new file is copied into place
3. The renamed file is scheduled for deletion on next reboot via the Windows MoveFileEx API
4. The application continues with the new version; the old file is cleaned up on restart

### UAC elevation

StorkDrop runs as a normal user by default. When installing to a protected directory (Program Files, Windows, etc.):

1. The UI detects the protected path and shows a hint
2. On install/update/uninstall, a separate elevated process is spawned via `runas`
3. The elevated process performs only the privileged operation, then exits
4. The main process reloads state from disk to pick up changes made by the elevated process
5. The feed ID is passed to the elevated process so it downloads from the correct source

### File lock detection

Before uninstalling or updating, StorkDrop checks for locked `.exe` and `.dll` files:

- Uses the Windows Restart Manager API to identify which process holds the lock
- Only checks executable files (not configs, logs, etc.) to avoid false positives from the indexer
- Opens files with `FileShare.ReadWrite | FileShare.Delete`  - the minimum check needed for deletion
- Shows a clear error naming the locked file and the process holding it

### Data protection

- Feed passwords are encrypted with DPAPI (Windows Data Protection)  - they're tied to the current user and machine
- Configuration files use atomic writes (temp + move) to prevent corruption
- The product registry validates for duplicate entries on load

## NuGet package

Reference in your plugin projects:

```xml
<PackageReference Include="StorkDrop.Contracts" />
```

## Architecture

```
StorkDrop.sln
dotnet/
  StorkDrop.Contracts/     Models, interfaces, plugin contracts (NuGet, net10.0)
  StorkDrop.Registry/      Nexus client, FeedRegistry (net10.0)
  StorkDrop.Installer/     Install engine (net10.0-windows)
  StorkDrop.App/           WPF application (net10.0-windows, win-x64)
  StorkDrop.Tests/         Unit tests (net10.0-windows)
```

## Building

```bash
dotnet build StorkDrop.sln --configuration Release
dotnet test StorkDrop.sln --configuration Release
```

## Configuration

Stored in `%APPDATA%/StorkDrop/Config/`:

| File                             | Purpose                                           |
| -------------------------------- | ------------------------------------------------- |
| `config.json`                    | Feeds, preferences, language                      |
| `installed-products.json`        | Installed product registry                        |
| `activity-log.json`              | Installation activity log                         |
| `{productId}.files.json`         | Per-product file manifest (for clean uninstall)   |
| `{productId}.envvars.json`     | Per-product environment variable tracking (for clean uninstall) |
| `plugin-config-{productId}.json` | Plugin config values (remembered between updates) |

Logs in `%APPDATA%/StorkDrop/Logs/` (Serilog, rolling daily).

## Roadmap

- **Cross-platform plugin scripting** - support pre/post install scripts in PowerShell, Python, and Bash alongside compiled .NET plugins
- **Differential updates** - download only changed files using binary diff instead of full packages
- **Rollback history** - keep multiple backup versions with a UI to restore any previous state
- **Remote management API** - REST API for triggering installs and checking status across a fleet of machines
- **Dependency resolution** - declare dependencies between products and install them in the correct order
- **Signed manifests** - GPG/Authenticode signing for manifests and packages with verification on install
- **Linux support** - extend beyond Windows with systemd service management and package integration
- **Additional languages** - expand localization beyond English and German

## License

MIT
