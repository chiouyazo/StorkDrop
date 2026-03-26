# ExampleAppPlugin - SQL Tools

This example demonstrates how to implement an `IStorkDropPlugin` that extends StorkDrop itself (as opposed to a product plugin that runs during product install/uninstall).

## What it demonstrates

- **`GetSetupSteps`** - Contributes a "Database Connections" step to the StorkDrop setup wizard, with fields for connection string, authentication mode, and password.
- **`GetSettingsSections`** with **Group fields** - Adds a "SQL Server Settings" section that includes:
  - A `Group` field for managing multiple database connections (each with name, connection string, and password). Users can add and remove connections dynamically.
  - Regular fields for command timeout, SQL logging, and backup folder.
- **`GetNavigationTabs`** - Adds a "SQL Status" tab to the StorkDrop sidebar with a database icon. Clicking it shows the settings form with all configured groups and fields.
- **`OnProductInstalledAsync`** - Called when a product from an associated feed is installed. Scans the install path for `.sql` files and lists them.
- **`OnProductUninstalledAsync`** - Called when a product is uninstalled. Logs cleanup activity.
- **`OnNavigationTabSelected`** - Called when the user clicks the plugin's navigation tab.

## Group fields

The `PluginFieldType.Group` field type lets plugins declare repeatable groups of sub-fields. In this example, the "Database Connections" group has three sub-fields (name, connection string, password). StorkDrop renders each group with an "Add" button and each instance with a "Remove" button.

Group values are persisted as JSON arrays in the plugin's settings file:

```json
{
  "connections": "[{\"name\":\"Production\",\"connectionString\":\"Server=...\",\"password\":\"...\"}]",
  "commandTimeout": "30",
  "logSqlStatements": "false"
}
```

## How to test

1. Open the solution in Visual Studio or Rider.
2. Set `ExampleAppPlugin` as the startup project.
3. `dotnet run`

The `Program.cs` entry point exercises every `IStorkDropPlugin` method in sequence, including displaying Group field sub-field details. It creates a temporary directory with a sample `.sql` file to demonstrate the install-time scanning behavior.

## Debug with StorkDrop

To test the plugin inside StorkDrop, build the project and run StorkDrop with:

```
StorkDrop.App.exe --plugin-dir C:\path\to\ExampleAppPlugin\bin\Debug\net10.0
```

The plugin will appear in the sidebar as "SQL Status" and its settings will be available in the plugin tab.

For Rider/VS, configure a launch profile that starts StorkDrop with the `--plugin-dir` argument pointing to this project's build output.
