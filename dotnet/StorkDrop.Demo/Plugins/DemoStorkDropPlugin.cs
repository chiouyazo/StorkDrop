using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Demo.Plugins;

internal sealed class DemoStorkDropPlugin
    : IStorkDropPlugin,
        IFileTypeHandler,
        IInstallPathResolver,
        IInteractiveStorkPlugin
{
    public string PluginId => "demo-sql-tools";
    public string DisplayName => "SQL Deploy Tools";
    public string[] AssociatedFeeds => ["https://demo.internal"];

    public IReadOnlyList<PluginSetupStep> GetSetupSteps() => [];

    public IReadOnlyList<PluginSettingsSection> GetSettingsSections() =>
        [
            new PluginSettingsSection
            {
                SectionId = "demo-databases",
                Title = "Database Connections",
                Fields =
                [
                    new PluginConfigField
                    {
                        Key = "databases",
                        Label = "Databases",
                        FieldType = PluginFieldType.Group,
                        SubFields =
                        [
                            new PluginConfigField
                            {
                                Key = "name",
                                Label = "Name",
                                FieldType = PluginFieldType.Text,
                                Required = true,
                            },
                            new PluginConfigField
                            {
                                Key = "server",
                                Label = "Server",
                                FieldType = PluginFieldType.Text,
                                Required = true,
                            },
                            new PluginConfigField
                            {
                                Key = "database",
                                Label = "Database",
                                FieldType = PluginFieldType.Dropdown,
                                Required = true,
                                Description =
                                    "Click \"Load databases\" to populate from the server.",
                            },
                            new PluginConfigField
                            {
                                Key = "load-databases",
                                Label = "Load databases",
                                FieldType = PluginFieldType.Button,
                            },
                            new PluginConfigField
                            {
                                Key = "password",
                                Label = "Password",
                                FieldType = PluginFieldType.Password,
                            },
                        ],
                    },
                ],
            },
        ];

    public IReadOnlyList<PluginNavTab> GetNavigationTabs() =>
        [
            new PluginNavTab
            {
                TabId = "sql-status",
                DisplayName = "SQL Status",
                Icon = "\uE964",
            },
        ];

    public void OnNavigationTabSelected(string tabId) { }

    public PluginButtonResult OnButtonClicked(
        string fieldKey,
        Dictionary<string, string> currentValues
    )
    {
        if (fieldKey != "load-databases")
            return new PluginButtonResult { StatusText = "Unknown button.", IsError = true };

        string server = currentValues.GetValueOrDefault("server", string.Empty);
        if (string.IsNullOrWhiteSpace(server))
            return new PluginButtonResult { StatusText = "Enter a server first.", IsError = true };

        List<PluginOptionItem> databases =
        [
            new PluginOptionItem { Value = "master", Label = "master" },
            new PluginOptionItem { Value = "Steps_Basis_2025_05", Label = "Steps_Basis_2025_05" },
            new PluginOptionItem { Value = "Steps_Test", Label = "Steps_Test" },
        ];

        return new PluginButtonResult
        {
            StatusText = $"Loaded {databases.Count} databases from {server}.",
            UpdatedSchema =
            [
                new PluginConfigField
                {
                    Key = "database",
                    Label = "Database",
                    FieldType = PluginFieldType.Dropdown,
                    Options = databases,
                    DefaultValue = databases[0].Value,
                },
            ],
        };
    }

    public Task OnProductInstalledAsync(
        PluginInstallContext context,
        CancellationToken ct = default
    ) => Task.CompletedTask;

    public Task OnProductUninstalledAsync(string productId, CancellationToken ct = default) =>
        Task.CompletedTask;

    public IReadOnlyList<string> HandledExtensions => [".sql"];

    public IReadOnlyList<PluginConfigField> GetFileHandlerConfig(
        IReadOnlyList<string> files,
        PluginContext context
    ) =>
        [
            new PluginConfigField
            {
                Key = "target-db",
                Label = "Target Database",
                FieldType = PluginFieldType.Dropdown,
                Required = true,
                Options =
                [
                    new PluginOptionItem
                    {
                        Value = "production",
                        Label = "Production (db-prod-01)",
                    },
                    new PluginOptionItem { Value = "staging", Label = "Staging (db-staging-01)" },
                ],
            },
        ];

    public Task<FileHandlerResult> HandleFilesAsync(
        IReadOnlyList<string> files,
        PluginContext context,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new FileHandlerResult { Success = true });

    public string? ResolveInstallPath(string targetPath, PluginContext? context)
    {
        if (!targetPath.Contains("{DemoPath}"))
            return null;
        return targetPath.Replace("{DemoPath}", StorkDrop.Demo.Sandbox.DemoSandbox.AppsRoot);
    }
}
