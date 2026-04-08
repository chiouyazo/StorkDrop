using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Demo.Plugins;

internal sealed class DemoInteractivePlugin
    : IStorkPlugin,
        IInteractiveStorkPlugin,
        IDescribableStorkPlugin
{
    public IReadOnlyList<PluginActionDescription> GetActionDescriptions(
        PluginEnvironment environment
    ) =>
        [
            new PluginActionDescription
            {
                Phase = PluginActionPhase.PreInstall,
                Title = "Validate database connection",
                Description =
                    "Checks that the selected database is reachable and responds to queries.",
            },
            new PluginActionDescription
            {
                Phase = PluginActionPhase.PostInstall,
                Title = "Create reporting tables",
                Description = "Creates the schema and tables for report storage.",
                Fields =
                [
                    new PluginConfigField
                    {
                        Key = "timeout",
                        Label = "Query Timeout (seconds)",
                        FieldType = PluginFieldType.Number,
                        DefaultValue = "300",
                        Min = 30,
                        Max = 3600,
                    },
                ],
            },
            new PluginActionDescription
            {
                Phase = PluginActionPhase.PostInstall,
                Title = "Insert default configuration",
                Description = "Populates initial settings and templates.",
                Fields =
                [
                    new PluginConfigField
                    {
                        Key = "service-password",
                        Label = "Service Account Password",
                        FieldType = PluginFieldType.Password,
                        Description = "Password for the reporting service account.",
                    },
                    new PluginConfigField
                    {
                        Key = "output-path",
                        Label = "Report Output Directory",
                        FieldType = PluginFieldType.FolderPath,
                        DefaultValue = @"C:\Reports\Output",
                    },
                ],
            },
            new PluginActionDescription
            {
                Phase = PluginActionPhase.PostInstall,
                Title = "Register scheduled tasks",
                Description = "Sets up automated report generation jobs.",
                Fields =
                [
                    new PluginConfigField
                    {
                        Key = "verbose-logging",
                        Label = "Enable Verbose Logging",
                        FieldType = PluginFieldType.Checkbox,
                        DefaultValue = "false",
                    },
                ],
            },
        ];

    public IReadOnlyList<PluginConfigField> GetConfigurationSchema(PluginEnvironment environment) =>
        [
            new PluginConfigField
            {
                Key = "target-database",
                Label = "Target Database",
                FieldType = PluginFieldType.Dropdown,
                Required = true,
                Options =
                [
                    new PluginOptionItem { Value = "prod", Label = "Production (db-prod-01)" },
                    new PluginOptionItem { Value = "staging", Label = "Staging (db-staging-01)" },
                    new PluginOptionItem { Value = "dev", Label = "Development (localhost)" },
                ],
                DefaultValue = "dev",
            },
            new PluginConfigField
            {
                Key = "schema-name",
                Label = "Schema Name",
                FieldType = PluginFieldType.Text,
                Required = true,
                DefaultValue = "dbo",
                Description = "The database schema to use for table creation.",
            },
            new PluginConfigField
            {
                Key = "test-connection",
                Label = "Test Connection",
                FieldType = PluginFieldType.Button,
            },
        ];

    public PluginButtonResult OnButtonClicked(
        string fieldKey,
        Dictionary<string, string> currentValues
    )
    {
        if (fieldKey != "test-connection")
            return new PluginButtonResult { StatusText = "Unknown button." };

        string db = currentValues.GetValueOrDefault("target-database", "");
        if (string.IsNullOrEmpty(db))
            return new PluginButtonResult
            {
                StatusText = "Select a database first.",
                IsError = true,
            };

        return new PluginButtonResult
        {
            StatusText = $"Connected to {db} - 3 schemas found.",
            UpdatedSchema = GetConfigurationSchema(new PluginEnvironment())
                .Select(f =>
                    f.Key == "schema-name"
                        ? new PluginConfigField
                        {
                            Key = "schema-name",
                            Label = "Schema Name",
                            FieldType = PluginFieldType.Dropdown,
                            Required = true,
                            Options =
                            [
                                new PluginOptionItem { Value = "dbo", Label = "dbo" },
                                new PluginOptionItem { Value = "reporting", Label = "reporting" },
                                new PluginOptionItem { Value = "archive", Label = "archive" },
                            ],
                            DefaultValue = "dbo",
                        }
                        : f
                )
                .ToList(),
        };
    }

    public Task<PluginPreInstallResult> PreInstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new PluginPreInstallResult { Success = true });

    public Task PostInstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task<PluginPreInstallResult> PreUninstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new PluginPreInstallResult { Success = true });

    public Task PostUninstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;
}
