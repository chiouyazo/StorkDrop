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
  <em>The StorkDrop marketplace: browse, install, and update products from multiple feeds</em>
</p>

## What is StorkDrop?

StorkDrop is a self-contained deployment client that installs, updates, and manages software from Nexus OSS registries. Products declare their install behavior in a JSON manifest. So no custom installer is needed.

It's built for **server software and business tools** that:

- Need regular updates on production servers
- Require custom pre/post-install logic (database migrations, config writes, service restarts)
- Are deployed to multiple machines with slightly different configurations
- Don't justify building a full MSI/NSIS installer for each release

## When to use StorkDrop

| Scenario                          | StorkDrop                        | Traditional installer | Fleet management | Package manager  |
| --------------------------------- | -------------------------------- | --------------------- | ---------------- | ---------------- |
| **Server-side business apps**     | Built for this                   | Manual per-machine    | Overkill         | No custom logic  |
| **Custom pre/post install logic** | Plugin system with dynamic UI    | Baked into installer  | Scripts only     | Scripts only     |
| **Multiple feeds/repositories**   | Multi-feed with per-feed plugins | N/A                   | Multiple sources | Multiple sources |
| **Self-service marketplace**      | Browse, search, filter           | No discovery          | Company portal   | CLI search       |
| **Version rollback**              | Backup + restore                 | Manual                | Complex          | Limited          |
| **Plugin extensibility**          | Dynamic config UI, custom tabs   | None                  | None             | None             |

## How it works

```
Nexus OSS          StorkDrop Client          Target Machine
(raw repo)    -->  Marketplace UI     -->    C:\Program Files\...
manifest.json      Plugin hooks              Shortcuts
product.zip        Install tracking          Environment variables
```

1. **Publish** your product to a Nexus raw repository with a `manifest.json`
2. **Users** open StorkDrop, browse the marketplace, click Install
3. **StorkDrop** downloads, extracts, runs plugin hooks, copies files, creates shortcuts
4. **Updates** are detected automatically and applied with backup/rollback
5. **Everything is tracked** - what was installed, where, and what changed - for clean uninstall

## Documentation

| Topic                                                  | Description                                                                                                                               |
| ------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------- |
| [Features](docs/features.md)                           | Marketplace, installation tracking, isolation, backup/rollback, file-in-use handling, UAC elevation, file lock detection, data protection |
| [Product Manifest](docs/manifest.md)                   | Repository structure, ZIP packaging, manifest reference, environment variables, uninstall behavior                                        |
| [Multi-Feed Support](docs/multi-feed.md)               | Connecting to multiple Nexus repositories, adding custom feed backends                                                                    |
| [App-Level Plugins](docs/app-level-plugins.md)         | Extend StorkDrop with custom file handlers, settings pages, and path resolvers                                                            |
| [Product-Level Plugins](docs/product-level-plugins.md) | Ship pre/post install logic with your product (database setup, validation, config generation)                                             |
| [CLI](docs/cli.md)                                     | Command-line interface for headless install, uninstall, update, list, and version queries                                                 |
| [UAC Elevation](docs/elevation.md)                     | How StorkDrop handles admin privileges by re-launching itself as an elevated process                                                      |

## Plugin system

StorkDrop has two plugin types. See the full guides linked above for details.

**App-level plugins** (`IStorkDropPlugin`) - Drop a DLL in the `plugins/` directory. Plugins can add sidebar tabs, claim file types (e.g., `.sql`), resolve custom path templates, and show setup wizard steps.

**Product-level plugins** (`IStorkPlugin`) - Ship a DLL with your product that provides pre/post install logic and a dynamic configuration UI. StorkDrop renders the config form automatically from the plugin's schema.

### Configuration field types

| Type          | Control                         | Use case                           |
| ------------- | ------------------------------- | ---------------------------------- |
| `Text`        | TextBox                         | Free text input                    |
| `Number`      | Validated TextBox               | Numeric with optional min/max      |
| `Dropdown`    | ComboBox                        | Single selection from options      |
| `MultiSelect` | Checkbox list                   | Multiple selections                |
| `Checkbox`    | CheckBox                        | Boolean toggle                     |
| `Password`    | PasswordBox                     | Sensitive input (masked)           |
| `FilePath`    | TextBox + browse                | File selection                     |
| `FolderPath`  | TextBox + browse                | Directory selection                |
| `Button`      | Button + status text            | Interactive actions (test, reload) |
| `Group`       | Repeatable card with add/remove | Dynamic lists of structured items  |

## Logging

StorkDrop logs every operation with structured logging via Serilog:

- **Installation steps** - download, extract, plugin processing, file copy, shortcuts, env vars, registration
- **Feed operations** - which feeds are loaded, how many products found, connection test results
- **Plugin loading** - which DLLs were discovered, which types were found, which failed and why
- **Update checking** - which feeds were queried, what updates were found
- **Configuration changes** - settings saves, imports, exports

Log files are stored in `%APPDATA%/StorkDrop/Logs/` with daily rolling and 30-day retention.

The Activity Log page has a "View Application Logs" button that opens the log directory. The installation panel's log viewer shows real-time per-installation logs.

## NuGet package

```xml
<PackageReference Include="StorkDrop.Contracts" />
```

## Architecture

```
StorkDrop.sln
dotnet/
  StorkDrop.Contracts/     Models, interfaces, plugin contracts (NuGet, net10.0)
  StorkDrop.Registry/      Nexus client, FeedRegistry (net10.0)
  StorkDrop.Installer/     Install engine, coordinator (net10.0-windows)
  StorkDrop.App/           WPF application (net10.0-windows, win-x64)
  StorkDrop.Tests/         Unit tests (net10.0-windows)
```

## Building

```bash
dotnet build StorkDrop.sln --configuration Release
```

## Configuration

Stored in `%APPDATA%/StorkDrop/Config/`:

| File                             | Purpose                                           |
| -------------------------------- | ------------------------------------------------- |
| `config.json`                    | Feeds, preferences, language                      |
| `installed-products.json`        | Installed product registry                        |
| `activity-log.json`              | Installation activity log                         |
| `{productId}.files.json`         | Per-product file manifest (for clean uninstall)   |
| `{productId}.envvars.json`       | Per-product environment variable tracking         |
| `plugin-config-{productId}.json` | Plugin config values (remembered between updates) |

Logs in `%APPDATA%/StorkDrop/Logs/` (Serilog, rolling daily, 30-day retention).

## Known limitations

- **Re-running plugin phases**: Currently, Pre/PostInstall steps can only run during installation. If you need to run the same plugin logic against a second database or with different configuration, you must reinstall the product. A "Re-run configuration" button per installed product is planned.

## Roadmap

- **Re-run plugin configuration** - allow re-executing Pre/PostInstall plugin phases on already-installed products with new configuration values (e.g., targeting a second database)
- **Remote deployment via WinRM** - deploy to remote servers without installing a persistent agent. StorkDrop connects via PowerShell Remoting (WinRM, enabled by default on Windows Server), installs itself on the target machine if needed, verifies plugin compatibility, and runs the installation locally on the remote machine. The local StorkDrop streams config dialogs and progress back to the user. Deployment targets are modular (`IDeploymentTarget`) so additional transports (SSH, custom) can be added alongside the built-in local and WinRM targets.
- **Cross-platform plugin scripting** - support pre/post install scripts in PowerShell, Python, and Bash alongside compiled .NET plugins
- **Differential updates** - download only changed files using binary diff instead of full packages
- **Rollback history** - keep multiple backup versions with a UI to restore any previous state
- **Dependency resolution** - declare dependencies between products and install them in the correct order
- **Signed manifests** - GPG/Authenticode signing for manifests and packages with verification on install
- **Plugin logging** - pass an `ILogger` to product plugins via `PluginContext` so plugins can write to the install log window
- **Reduce file copy log spam** - report copy progress as percentage instead of per-file "Copying files..." entries
- **Linux support** - extend beyond Windows with systemd service management and package integration
- **Additional languages** - expand localization beyond English and German

## License

Apache 2.0
