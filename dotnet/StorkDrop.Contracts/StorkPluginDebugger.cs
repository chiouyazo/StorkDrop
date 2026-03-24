using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StorkDrop.Contracts;

/// <summary>
/// Utility for plugin developers to test their plugins outside of StorkDrop.
/// Call <see cref="RunAsync{TPlugin}"/> from a console app or test to exercise
/// the full plugin lifecycle: schema, validate, pre-install, post-install,
/// pre-uninstall, and post-uninstall.
/// </summary>
public static class StorkPluginDebugger
{
    /// <summary>
    /// Exercises the full plugin lifecycle for the specified plugin type.
    /// Runs: GetConfigurationSchema, ValidateConfiguration, PreInstallAsync,
    /// PostInstallAsync, PreUninstallAsync, and PostUninstallAsync.
    /// </summary>
    /// <typeparam name="TPlugin">The plugin type to test. Must implement <see cref="IStorkPlugin"/> and have a parameterless constructor.</typeparam>
    /// <param name="context">The plugin context to pass to each lifecycle method.</param>
    /// <param name="configValues">The configuration values to simulate user input.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task RunAsync<TPlugin>(
        PluginContext context,
        Dictionary<string, string> configValues
    )
        where TPlugin : IStorkPlugin, new()
    {
        context.ConfigValues = configValues;

        TPlugin plugin = new TPlugin();

        System.Console.WriteLine($"[StorkPluginDebugger] Plugin: {typeof(TPlugin).FullName}");
        System.Console.WriteLine(
            $"[StorkPluginDebugger] Product: {context.ProductId} v{context.Version}"
        );
        System.Console.WriteLine($"[StorkPluginDebugger] InstallPath: {context.InstallPath}");
        System.Console.WriteLine();

        PluginEnvironment environment = new PluginEnvironment()
        {
            StorkConfigDirectory = context.StorkConfigDirectory,
            PreviousConfigValues = configValues,
        };

        System.Console.WriteLine("[StorkPluginDebugger] === GetConfigurationSchema ===");
        IReadOnlyList<PluginConfigField> schema = plugin.GetConfigurationSchema(environment);
        System.Console.WriteLine($"[StorkPluginDebugger] Fields: {schema.Count}");
        foreach (PluginConfigField field in schema)
        {
            string requiredMarker = field.Required ? " *" : "";
            System.Console.WriteLine(
                $"[StorkPluginDebugger]   {field.Key} ({field.FieldType}){requiredMarker}: {field.Label}"
            );
        }
        System.Console.WriteLine();

        System.Console.WriteLine("[StorkPluginDebugger] === ValidateConfiguration ===");
        IReadOnlyList<PluginValidationError> errors = plugin.TryValidateConfiguration(context);
        if (errors.Count > 0)
        {
            foreach (PluginValidationError error in errors)
            {
                System.Console.WriteLine(
                    $"[StorkPluginDebugger]   ERROR {error.FieldKey}: {error.Message}"
                );
            }
            System.Console.WriteLine("[StorkPluginDebugger] Validation failed. Aborting.");
            return;
        }
        System.Console.WriteLine("[StorkPluginDebugger] Validation passed.");
        System.Console.WriteLine();

        System.Console.WriteLine("[StorkPluginDebugger] === PreInstallAsync ===");
        PluginPreInstallResult preInstallResult = await plugin.PreInstallAsync(context);
        if (!preInstallResult.Success)
        {
            System.Console.WriteLine(
                $"[StorkPluginDebugger] PreInstall failed: {preInstallResult.Message}"
            );
            foreach (PluginValidationError error in preInstallResult.ValidationErrors)
            {
                System.Console.WriteLine(
                    $"[StorkPluginDebugger]   ERROR {error.FieldKey}: {error.Message}"
                );
            }
            return;
        }
        System.Console.WriteLine("[StorkPluginDebugger] PreInstall completed.");
        System.Console.WriteLine();

        System.Console.WriteLine("[StorkPluginDebugger] === PostInstallAsync ===");
        await plugin.PostInstallAsync(context);
        System.Console.WriteLine("[StorkPluginDebugger] PostInstall completed.");
        System.Console.WriteLine();

        System.Console.WriteLine("[StorkPluginDebugger] === PreUninstallAsync ===");
        PluginPreInstallResult preUninstallResult = await plugin.PreUninstallAsync(context);
        if (!preUninstallResult.Success)
        {
            System.Console.WriteLine(
                $"[StorkPluginDebugger] PreUninstall failed: {preUninstallResult.Message}"
            );
            foreach (PluginValidationError error in preUninstallResult.ValidationErrors)
            {
                System.Console.WriteLine(
                    $"[StorkPluginDebugger]   ERROR {error.FieldKey}: {error.Message}"
                );
            }
            return;
        }
        System.Console.WriteLine("[StorkPluginDebugger] PreUninstall completed.");
        System.Console.WriteLine();

        System.Console.WriteLine("[StorkPluginDebugger] === PostUninstallAsync ===");
        await plugin.PostUninstallAsync(context);
        System.Console.WriteLine("[StorkPluginDebugger] PostUninstall completed.");
        System.Console.WriteLine();

        System.Console.WriteLine("[StorkPluginDebugger] === Done ===");
    }
}
