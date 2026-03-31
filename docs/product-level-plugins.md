# Product-Level Plugins

Product-level plugins ship inside a product's ZIP package. They run during installation and uninstallation to perform custom logic like database setup, service registration, or configuration file generation. Unlike app-level plugins, they are not permanently loaded. They exist only for the duration of the install/uninstall operation.

## When to use

Use a product-level plugin when your product needs to:

- Show a configuration form before installation (database selection, path configuration)
- Validate prerequisites before files are copied
- Run database migrations or SQL scripts after installation
- Register the product in an external system
- Clean up resources on uninstall

## Getting started

### 1. Create a class library project

```bash
dotnet new classlib -n MyProduct.Installer -f net10.0
cd MyProduct.Installer
dotnet add package StorkDrop.Contracts
```

Use `net10.0` (not `net10.0-windows`) unless you need Windows-specific APIs.

### 2. Implement `IStorkPlugin`

```csharp
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;

namespace MyProduct.Installer;

public class MyProductInstaller : IStorkPlugin
{
    public IReadOnlyList<PluginConfigField> GetConfigurationSchema(PluginEnvironment environment)
    {
        return
        [
            new PluginConfigField
            {
                Key = "database",
                Label = "Target Database",
                FieldType = PluginFieldType.Dropdown,
                Required = true,
                Options =
                [
                    new PluginOptionItem { Value = "production", Label = "Production" },
                    new PluginOptionItem { Value = "staging", Label = "Staging" },
                ],
                DefaultValue = environment.PreviousConfigValues
                    .GetValueOrDefault("database", "staging"),
            },
            new PluginConfigField
            {
                Key = "run-migrations",
                Label = "Run database migrations",
                FieldType = PluginFieldType.Checkbox,
                DefaultValue = "true",
            },
        ];
    }

    public async Task<PluginPreInstallResult> PreInstallAsync(
        PluginContext context, CancellationToken ct)
    {
        string db = context.ConfigValues.GetValueOrDefault("database", "");
        if (string.IsNullOrEmpty(db))
        {
            return new PluginPreInstallResult
            {
                Success = false,
                Message = "No database selected.",
            };
        }

        // Validate connectivity, check prerequisites, etc.
        return new PluginPreInstallResult { Success = true };
    }

    public async Task PostInstallAsync(PluginContext context, CancellationToken ct)
    {
        string db = context.ConfigValues.GetValueOrDefault("database", "");
        bool runMigrations = context.ConfigValues.GetValueOrDefault("run-migrations", "true") == "true";

        if (runMigrations)
        {
            // Run migrations against the selected database
        }
    }

    public Task<PluginPreInstallResult> PreUninstallAsync(
        PluginContext context, CancellationToken ct)
    {
        return Task.FromResult(new PluginPreInstallResult { Success = true });
    }

    public Task PostUninstallAsync(PluginContext context, CancellationToken ct)
    {
        // Clean up database entries, external registrations, etc.
        return Task.CompletedTask;
    }
}
```

### 3. Reference the plugin in the product manifest

```json
{
    "productId": "my-product",
    "title": "My Product",
    "version": "1.0.0",
    "installType": "Suite",
    "recommendedInstallPath": "C:\\MyCompany\\MyProduct",
    "publisher": "My Company",
    "plugins": [
        {
            "assembly": "MyProduct.Installer.dll",
            "typeName": "MyProduct.Installer.MyProductInstaller"
        }
    ]
}
```

## Lifecycle

StorkDrop calls plugin methods in this order during installation:

```
GetConfigurationSchema()     User sees the config form
         |
         v
PreInstallAsync()            Validate, check prerequisites
         |                   Return Success = false to abort
         v
   [Files copied]            StorkDrop copies product files to install path
         |
         v
PostInstallAsync()           Migrations, registrations, config writes
```

During uninstallation:

```
PreUninstallAsync()          Backup data, stop services
         |                   Return Success = false to abort
         v
   [Files deleted]           StorkDrop removes tracked files
         |
         v
PostUninstallAsync()         Remove database entries, clean up
```

If `PreInstallAsync` or `PreUninstallAsync` returns `Success = false`, the operation is aborted. `PostInstallAsync` and `PostUninstallAsync` failures are logged but do not abort the operation.

## PluginContext

The `PluginContext` passed to pre/post methods contains:

| Property | Type | Description |
|----------|------|-------------|
| `ProductId` | `string` | The product being installed |
| `Version` | `string` | Version being installed |
| `InstallPath` | `string` | Target installation directory |
| `StorkConfigDirectory` | `string` | StorkDrop's config directory |
| `ConfigValues` | `Dictionary<string, string>` | User's choices from the config form |
| `PluginData` | `Dictionary<string, object>` | Extra data from app-level plugins |

## PluginEnvironment

The `PluginEnvironment` passed to `GetConfigurationSchema` contains:

| Property | Type | Description |
|----------|------|-------------|
| `StorkConfigDirectory` | `string` | StorkDrop's config directory |
| `PreviousVersion` | `string?` | Version currently installed (null for fresh install) |
| `PreviousConfigValues` | `Dictionary<string, string>` | Config values from the previous install |
| `PluginData` | `Dictionary<string, object>` | Extra data from app-level plugins |

Use `PreviousConfigValues` to pre-fill the form with the user's previous choices during upgrades.

## Validation

Optionally implement `IValidatingStorkPlugin` alongside `IStorkPlugin` to validate user input before `PreInstallAsync` runs:

```csharp
public class MyProductInstaller : IStorkPlugin, IValidatingStorkPlugin
{
    public IReadOnlyList<PluginValidationError> ValidateConfiguration(PluginContext context)
    {
        List<PluginValidationError> errors = [];

        string db = context.ConfigValues.GetValueOrDefault("database", "");
        if (string.IsNullOrEmpty(db))
            errors.Add(new PluginValidationError { FieldKey = "database", Message = "Required." });

        return errors;
    }
}
```

Validation errors are shown next to the corresponding form fields.

## Packaging

Product plugins must be published (not just built) so all runtime dependencies are included:

```bash
dotnet publish MyProduct.Installer/ --configuration Release --output publish/plugin/
```

The published output (all DLLs) goes into the outer ZIP as loose files alongside `contents.zip`:

```
my-product-1.0.0.zip                    (outer ZIP)
  contents.zip                           (inner ZIP - product binaries)
    MyProduct.exe
    MyProduct.dll
  MyProduct.Installer.dll                (plugin assembly)
  Microsoft.Data.SqlClient.dll           (plugin dependency)
  other-dependency.dll                   (plugin dependency)
```

StorkDrop loads the plugin in an isolated `AssemblyLoadContext` that resolves dependencies from the plugin's directory first, then falls back to the host. This means:

- Your plugin can use any NuGet package without conflicting with StorkDrop
- `StorkDrop.Contracts` is always resolved from the host, even if the published output contains it. This prevents type identity issues between the plugin and host.
- All other dependencies must be in the same directory as the plugin DLL

### Pipeline example (Bitbucket)

```yaml
- step: &build
    name: Build & Pack
    script:
      - cry-version
      - VERSION=$(cat version.txt)
      - dotnet build --configuration Release -p:Version=$VERSION
      - dotnet publish MyProduct/ --configuration Release --no-build --output artifacts/product/
      - dotnet publish MyProduct.Installer/ --configuration Release --no-build --output artifacts/plugin/

      # Inner ZIP: product files
      - cd artifacts/product && zip -qr ../contents.zip . && cd ../..

      # Outer ZIP: inner ZIP + plugin DLLs
      - stork-package --contents artifacts/contents.zip --loose "artifacts/plugin/*.dll"
      - stork-manifest-update --version "$VERSION" --size artifacts/*.zip --date
```

## Reading app-level plugin configuration

Product plugins often need to read configuration from an app-level plugin (for example, reading database connections configured in a settings tab). The config is stored as JSON at a well-known path:

```csharp
string configPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "StorkDrop", "Config", "plugin-settings-{pluginId}.json"
);

string json = File.ReadAllText(configPath);
var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
```

Group field values are stored as JSON arrays within the string values:

```csharp
string dbJson = values["databases"];
var databases = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(dbJson);
```
