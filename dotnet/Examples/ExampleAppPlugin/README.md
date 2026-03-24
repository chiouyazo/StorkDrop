# ExampleAppPlugin - SQL Tools

This example demonstrates how to implement an `IStorkDropPlugin` that extends StorkDrop itself (as opposed to a product plugin that runs during product install/uninstall).

## What it demonstrates

- **`GetSetupSteps`** - Contributes a "Database Connections" step to the StorkDrop setup wizard, with fields for connection string, authentication mode, and password.
- **`GetSettingsSections`** - Adds a "SQL Server" section to the StorkDrop settings UI, with fields for command timeout, SQL logging, and backup folder.
- **`GetNavigationTabs`** - Adds a "SQL Status" tab to the StorkDrop sidebar with a database icon.
- **`OnProductInstalledAsync`** - Called when a product from an associated feed is installed. Scans the install path for `.sql` files and lists them.
- **`OnProductUninstalledAsync`** - Called when a product is uninstalled. Logs cleanup activity.
- **`OnNavigationTabSelected`** - Called when the user clicks the plugin's navigation tab.

## How to test

1. Open the solution in Visual Studio or Rider.
2. Set `ExampleAppPlugin` as the startup project.
3. `dotnet run`

The `Program.cs` entry point exercises every `IStorkDropPlugin` method in sequence. It creates a temporary directory with a sample `.sql` file to demonstrate the install-time scanning behavior.

You can also reference `StorkPluginDebugger` for product-level plugin testing.
