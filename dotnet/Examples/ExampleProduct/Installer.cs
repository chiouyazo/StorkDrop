using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StorkDrop.Contracts;

namespace ExampleProduct;

/// <summary>
/// Example product plugin for "Acme Dashboard" - a web dashboard that needs
/// database configuration, API endpoint setup, and feature selection during install.
/// Implements <see cref="IValidatingStorkPlugin"/> to demonstrate both install hooks
/// and configuration validation.
/// </summary>
public sealed class Installer : IValidatingStorkPlugin
{
    /// <inheritdoc />
    public IReadOnlyList<PluginConfigField> GetConfigurationSchema(PluginEnvironment environment)
    {
        List<PluginConfigField> fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "database",
                Label = "Database Connection",
                Description = "Select which database the dashboard should connect to.",
                FieldType = PluginFieldType.Dropdown,
                Required = true,
                Options = new List<PluginOptionItem>
                {
                    new PluginOptionItem
                    {
                        Value = "Production",
                        Label = "Production (SQL Server)",
                    },
                    new PluginOptionItem { Value = "Staging", Label = "Staging (SQL Server)" },
                    new PluginOptionItem { Value = "Development", Label = "Development (LocalDB)" },
                },
            },
            new PluginConfigField
            {
                Key = "apiUrl",
                Label = "API Endpoint URL",
                Description = "The base URL for the Acme Dashboard API.",
                FieldType = PluginFieldType.Text,
                Required = true,
                DefaultValue = "https://api.acme.local",
            },
            new PluginConfigField
            {
                Key = "port",
                Label = "Port Number",
                Description = "The port the dashboard web server will listen on.",
                FieldType = PluginFieldType.Number,
                Required = true,
                DefaultValue = "8443",
                Min = 1,
                Max = 65535,
            },
            new PluginConfigField
            {
                Key = "enableSsl",
                Label = "Enable SSL",
                Description = "Whether to enable SSL/TLS for the dashboard web server.",
                FieldType = PluginFieldType.Checkbox,
                Required = false,
                DefaultValue = "true",
            },
            new PluginConfigField
            {
                Key = "features",
                Label = "Features to Install",
                Description = "Select which optional features to include in the installation.",
                FieldType = PluginFieldType.MultiSelect,
                Required = false,
                Options = new List<PluginOptionItem>
                {
                    new PluginOptionItem { Value = "Reporting", Label = "Reporting" },
                    new PluginOptionItem { Value = "API", Label = "API" },
                    new PluginOptionItem { Value = "AdminPanel", Label = "Admin Panel" },
                },
            },
        };

        return fields;
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginValidationError> ValidateConfiguration(PluginContext context)
    {
        List<PluginValidationError> errors = new List<PluginValidationError>();

        if (
            !context.ConfigValues.TryGetValue("database", out string? database)
            || string.IsNullOrWhiteSpace(database)
        )
        {
            errors.Add(
                new PluginValidationError("database", "A database connection must be selected.")
            );
        }

        if (context.ConfigValues.TryGetValue("apiUrl", out string? apiUrl))
        {
            if (
                string.IsNullOrWhiteSpace(apiUrl)
                || !Uri.TryCreate(apiUrl, UriKind.Absolute, out Uri? parsedUri)
                || (parsedUri.Scheme != "http" && parsedUri.Scheme != "https")
            )
            {
                errors.Add(
                    new PluginValidationError(
                        "apiUrl",
                        "API URL must be a valid HTTP or HTTPS URL."
                    )
                );
            }
        }
        else
        {
            errors.Add(new PluginValidationError("apiUrl", "API URL is required."));
        }

        if (context.ConfigValues.TryGetValue("port", out string? portString))
        {
            if (!int.TryParse(portString, out int port) || port < 1 || port > 65535)
            {
                errors.Add(
                    new PluginValidationError("port", "Port must be a number between 1 and 65535.")
                );
            }
        }
        else
        {
            errors.Add(new PluginValidationError("port", "Port number is required."));
        }

        return errors;
    }

    /// <inheritdoc />
    public Task<PluginPreInstallResult> PreInstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    )
    {
        string database = context.ConfigValues.GetValueOrDefault("database", "");
        string apiUrl = context.ConfigValues.GetValueOrDefault("apiUrl", "");

        Console.WriteLine($"[Acme Dashboard] Checking database connection to '{database}'...");
        Console.WriteLine($"[Acme Dashboard] Verifying API endpoint at '{apiUrl}'...");

        // Demonstrate returning a validation error if something is wrong
        if (database == "Production" && apiUrl.Contains("localhost"))
        {
            PluginPreInstallResult failResult = new PluginPreInstallResult
            {
                Success = false,
                Message = "Cannot use localhost API URL with Production database.",
                ValidationErrors = new List<PluginValidationError>
                {
                    new PluginValidationError(
                        "apiUrl",
                        "Production installs must use a non-localhost API endpoint."
                    ),
                },
            };
            return Task.FromResult(failResult);
        }

        Console.WriteLine("[Acme Dashboard] Pre-install checks passed.");

        PluginPreInstallResult result = new PluginPreInstallResult
        {
            Success = true,
            Message = "Database and API endpoint verified successfully.",
        };
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task PostInstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    )
    {
        string installPath = context.InstallPath;
        string database = context.ConfigValues.GetValueOrDefault("database", "");
        string apiUrl = context.ConfigValues.GetValueOrDefault("apiUrl", "");
        string port = context.ConfigValues.GetValueOrDefault("port", "8443");
        string enableSsl = context.ConfigValues.GetValueOrDefault("enableSsl", "false");
        string features = context.ConfigValues.GetValueOrDefault("features", "");

        Console.WriteLine("[Acme Dashboard] Creating database tables...");

        string configFilePath = Path.Combine(installPath, "appsettings.json");
        Console.WriteLine($"[Acme Dashboard] Writing config file to {configFilePath}");

        Directory.CreateDirectory(installPath);

        string json =
            "{\n"
            + $"  \"Database\": \"{database}\",\n"
            + $"  \"ApiUrl\": \"{apiUrl}\",\n"
            + $"  \"Port\": {port},\n"
            + $"  \"EnableSsl\": {enableSsl.ToLowerInvariant()},\n"
            + $"  \"Features\": \"{features}\"\n"
            + "}";

        File.WriteAllText(configFilePath, json);

        Console.WriteLine("[Acme Dashboard] Registering API endpoint...");
        Console.WriteLine("[Acme Dashboard] Post-install complete.");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PluginPreInstallResult> PreUninstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    )
    {
        Console.WriteLine("[Acme Dashboard] Backing up database config...");

        PluginPreInstallResult result = new PluginPreInstallResult
        {
            Success = true,
            Message = "Database config backed up.",
        };
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task PostUninstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    )
    {
        Console.WriteLine("[Acme Dashboard] Cleanup complete.");
        return Task.CompletedTask;
    }
}
