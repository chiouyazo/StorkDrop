# ExampleProduct - Acme Dashboard

This example demonstrates how to implement an `IStorkPlugin` (and `IValidatingStorkPlugin`) for a product that ships custom install logic through StorkDrop.

## What it demonstrates

- **`GetConfigurationSchema`** - Declares five configuration fields using Dropdown, Text, Number, Checkbox, and MultiSelect field types. StorkDrop renders these as a dynamic form before installation begins.
- **`ValidateConfiguration`** - Validates that a database is selected, the API URL is a well-formed HTTP(S) URL, and the port is in the 1-65535 range. Errors are shown inline next to the corresponding field.
- **`PreInstallAsync`** - Runs before files are copied. Logs simulated checks and demonstrates returning a structured error when the configuration is inconsistent (e.g., localhost API with a Production database).
- **`PostInstallAsync`** - Runs after files are copied. Creates an `appsettings.json` file in the install path with the user's configured values.
- **`PreUninstallAsync`** - Runs before files are removed. Logs a simulated database config backup.
- **`PostUninstallAsync`** - Runs after files are removed. Logs cleanup completion.

## How to test

1. Open the solution in Visual Studio or Rider.
2. Set `ExampleProduct` as the startup project.
3. `dotnet run`

The `Program.cs` entry point uses `StorkPluginDebugger.RunAsync` to exercise every lifecycle method with sample configuration values. Console output shows exactly what StorkDrop would call and in what order.

You can also reference `StorkPluginDebugger` from a unit test to automate plugin verification.

## manifest.json

The included `manifest.json` shows every supported manifest field for a product package, including shortcuts, environment variables, cleanup rules, plugin declarations, and requirements.

The `environmentVariables` section demonstrates both actions:
- `ACME_HOME` is **set** to the install path (created on install, deleted on uninstall)
- `PATH` is **appended** with the `bin` subdirectory (only the added value is removed on uninstall)
