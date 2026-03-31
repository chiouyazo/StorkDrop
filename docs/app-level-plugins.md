# App-Level Plugins

App-level plugins extend StorkDrop itself. They are loaded once at startup and stay active for the entire session. They can add UI tabs, handle custom file types during installation, resolve install path templates, and react to product lifecycle events.

## When to use

Use an app-level plugin when you need to:

- Handle a custom file type across multiple products (e.g., deploy `.sql` files, `.config` files)
- Add a settings/configuration page to StorkDrop's sidebar
- Resolve custom path templates like `{MyAppPath}` in product install paths
- Run logic when any product from a specific feed is installed or uninstalled

## Getting started

### 1. Create a class library project

```bash
dotnet new classlib -n MyCompany.StorkPlugin -f net10.0-windows
cd MyCompany.StorkPlugin
dotnet add package StorkDrop.Contracts
```

### 2. Implement `IStorkDropPlugin`

This is the minimum interface every app-level plugin must implement:

```csharp
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;

namespace MyCompany.StorkPlugin;

public class MyPlugin : IStorkDropPlugin
{
    public string PluginId => "mycompany-plugin";
    public string DisplayName => "My Plugin";
    public string[]? AssociatedFeeds => null;

    public IReadOnlyList<PluginSetupStep> GetSetupSteps() => [];
    public IReadOnlyList<PluginSettingsSection> GetSettingsSections() => [];
    public IReadOnlyList<PluginNavTab> GetNavigationTabs() => [];

    public Task OnProductInstalledAsync(PluginInstallContext context, CancellationToken ct)
        => Task.CompletedTask;

    public Task OnProductUninstalledAsync(string productId, CancellationToken ct)
        => Task.CompletedTask;

    public void OnNavigationTabSelected(string tabId) { }
}
```

### 3. Deploy

Build the project and copy the output DLL to StorkDrop's `plugins/` directory (next to `StorkDrop.App.exe`). StorkDrop discovers it on next startup.

For development, use the `--plugin-dir` launch argument:

```bash
StorkDrop.App.exe --plugin-dir "C:\dev\MyCompany.StorkPlugin\bin\Debug\net10.0-windows"
```

## Optional interfaces

### `IFileTypeHandler` - Handle custom file types

Implement this alongside `IStorkDropPlugin` to intercept specific file extensions during product installation. Files claimed by your handler are not copied to the install directory.

```csharp
public class MyPlugin : IStorkDropPlugin, IFileTypeHandler
{
    public IReadOnlyList<string> HandledExtensions => [".sql"];

    public IReadOnlyList<PluginConfigField> GetFileHandlerConfig(
        IReadOnlyList<string> files, PluginContext context)
    {
        // Return fields shown to the user before processing.
        // Return an empty list if no user input is needed.
        return
        [
            new PluginConfigField
            {
                Key = "target-db",
                Label = "Target Database",
                FieldType = PluginFieldType.Dropdown,
                Required = true,
                Options = LoadDatabaseOptions(),
            }
        ];
    }

    public async Task<FileHandlerResult> HandleFilesAsync(
        IReadOnlyList<string> files, PluginContext context, CancellationToken ct)
    {
        string db = context.ConfigValues["target-db"];
        List<FileHandlerFileResult> results = [];

        foreach (string file in files)
        {
            try
            {
                await DeployFile(file, db, ct);
                results.Add(new FileHandlerFileResult
                {
                    FilePath = file,
                    Success = true,
                    Action = $"Deployed to {db}",
                });
            }
            catch (Exception ex)
            {
                results.Add(new FileHandlerFileResult
                {
                    FilePath = file,
                    Success = false,
                    ErrorMessage = ex.Message,
                });
            }
        }

        bool allSucceeded = results.All(r => r.Success);
        return new FileHandlerResult
        {
            Success = allSucceeded,
            FileResults = results,
            ErrorMessage = allSucceeded ? null : "One or more files failed to deploy.",
        };
    }
}
```

If `HandleFilesAsync` returns `Success = false`, the entire product installation is aborted.

### `IInstallPathResolver` - Resolve path templates

Implement this to resolve custom template variables in product install paths.

```csharp
public class MyPlugin : IStorkDropPlugin, IInstallPathResolver
{
    public string? ResolveInstallPath(string targetPath, PluginContext? context)
    {
        if (!targetPath.Contains("{MyAppPath}"))
            return null;

        string configuredPath = LoadConfiguredPath();
        return targetPath.Replace("{MyAppPath}", configuredPath);
    }
}
```

Products can then use `{MyAppPath}/subfolder` as their `recommendedInstallPath`. Return `null` if the template is not yours.

`{StorkPath}` is resolved by StorkDrop itself and does not need a plugin.

## Settings and navigation tabs

### Adding a settings section

Return sections from `GetSettingsSections()`. StorkDrop renders them as editable forms in the plugin's sidebar tab. Values are persisted automatically to `%APPDATA%/StorkDrop/Config/plugin-settings-{pluginId}.json`.

```csharp
public IReadOnlyList<PluginSettingsSection> GetSettingsSections()
{
    return
    [
        new PluginSettingsSection
        {
            SectionId = "general",
            Title = "General Settings",
            Fields =
            [
                new PluginConfigField
                {
                    Key = "server",
                    Label = "Server Address",
                    FieldType = PluginFieldType.Text,
                    Required = true,
                },
                new PluginConfigField
                {
                    Key = "connections",
                    Label = "Database Connections",
                    FieldType = PluginFieldType.Group,
                    SubFields =
                    [
                        new PluginConfigField { Key = "name", Label = "Name", FieldType = PluginFieldType.Text, Required = true },
                        new PluginConfigField { Key = "host", Label = "Host", FieldType = PluginFieldType.Text, Required = true },
                        new PluginConfigField { Key = "password", Label = "Password", FieldType = PluginFieldType.Password },
                    ],
                },
            ],
        },
    ];
}
```

Group fields render as repeatable cards with add/remove buttons. Values are stored as JSON arrays.

### Adding a navigation tab

```csharp
public IReadOnlyList<PluginNavTab> GetNavigationTabs()
{
    return
    [
        new PluginNavTab
        {
            TabId = "my-status",
            DisplayName = "My Status",
            Icon = "\uE770", // Segoe MDL2 Assets icon code
        },
    ];
}
```

When the user clicks the tab, `OnNavigationTabSelected("my-status")` is called and StorkDrop shows the plugin's settings sections.

## Associated feeds

Set `AssociatedFeeds` to an array of feed URLs. When a user adds a feed matching one of these URLs, StorkDrop shows your plugin as a recommended addition in the Settings page.

```csharp
public string[]? AssociatedFeeds => ["https://nexus.mycompany.com"];
```

## Plugin loading

StorkDrop scans the `plugins/` directory at startup. Each DLL is loaded into its own isolated `AssemblyLoadContext`. The host's `StorkDrop.Contracts` assembly is shared so plugin types implement the correct interfaces. Other dependencies are resolved from the plugin's own directory.

This means:
- Multiple plugins can use different versions of the same library without conflict
- Plugins don't need to bundle `StorkDrop.Contracts.dll` (it comes from the host)
- A plugin built against Contracts v1.0.7 works with StorkDrop running Contracts v1.0.8
