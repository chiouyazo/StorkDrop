using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StorkDrop.Contracts;

namespace ExampleAppPlugin;

/// <summary>
/// Example app-level plugin that extends StorkDrop itself with SQL Server tooling.
/// Demonstrates setup steps, settings sections, navigation tabs, and install/uninstall hooks.
/// </summary>
public sealed class SqlToolsPlugin : IStorkDropPlugin
{
    /// <inheritdoc />
    public string PluginId => "sql-tools";

    /// <inheritdoc />
    public string DisplayName => "SQL Tools";

    /// <inheritdoc />
    public string[] AssociatedFeeds =>
        new string[] { "https://nexus.example.com/repository/sql-products" };

    /// <inheritdoc />
    public IReadOnlyList<PluginSetupStep> GetSetupSteps()
    {
        List<PluginSetupStep> steps = new List<PluginSetupStep>
        {
            new PluginSetupStep
            {
                StepId = "database-connections",
                Title = "Database Connections",
                Description =
                    "Configure SQL Server connections that products in this feed can use during installation and at runtime.",
                Fields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = "defaultConnectionString",
                        Label = "Default Connection String",
                        Description =
                            "The default SQL Server connection string for products in this feed.",
                        FieldType = PluginFieldType.Text,
                        Required = true,
                        DefaultValue =
                            "Server=localhost;Database=StorkDrop;Trusted_Connection=true",
                    },
                    new PluginConfigField
                    {
                        Key = "sqlAuthMode",
                        Label = "Authentication Mode",
                        Description = "How to authenticate with SQL Server.",
                        FieldType = PluginFieldType.Dropdown,
                        Required = true,
                        Options = new List<PluginOptionItem>
                        {
                            new PluginOptionItem
                            {
                                Value = "Windows",
                                Label = "Windows Authentication",
                            },
                            new PluginOptionItem
                            {
                                Value = "SqlAuth",
                                Label = "SQL Server Authentication",
                            },
                        },
                    },
                    new PluginConfigField
                    {
                        Key = "sqlPassword",
                        Label = "SQL Password",
                        Description =
                            "Password for SQL Server authentication (only required for SQL Auth mode).",
                        FieldType = PluginFieldType.Password,
                        Required = false,
                    },
                },
            },
        };

        return steps;
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginSettingsSection> GetSettingsSections()
    {
        List<PluginSettingsSection> sections = new List<PluginSettingsSection>
        {
            new PluginSettingsSection
            {
                SectionId = "sql-server-settings",
                Title = "SQL Server",
                Fields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = "commandTimeout",
                        Label = "Command Timeout (seconds)",
                        Description =
                            "Default timeout for SQL commands executed by product plugins.",
                        FieldType = PluginFieldType.Number,
                        Required = false,
                        DefaultValue = "30",
                        Min = 1,
                        Max = 3600,
                    },
                    new PluginConfigField
                    {
                        Key = "logSqlStatements",
                        Label = "Log SQL Statements",
                        Description = "Write all executed SQL statements to the activity log.",
                        FieldType = PluginFieldType.Checkbox,
                        Required = false,
                        DefaultValue = "false",
                    },
                    new PluginConfigField
                    {
                        Key = "backupPath",
                        Label = "Backup Folder",
                        Description = "Folder where database backups are stored before migrations.",
                        FieldType = PluginFieldType.FolderPath,
                        Required = false,
                    },
                },
            },
        };

        return sections;
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginNavTab> GetNavigationTabs()
    {
        List<PluginNavTab> tabs = new List<PluginNavTab>
        {
            new PluginNavTab
            {
                TabId = "sql-status",
                DisplayName = "SQL Status",
                Icon = "\uE968", // Database icon (Segoe MDL2)
            },
        };

        return tabs;
    }

    /// <inheritdoc />
    public Task OnProductInstalledAsync(
        PluginInstallContext context,
        CancellationToken ct = default
    )
    {
        Console.WriteLine($"[SQL Tools] Scanning for .sql files in {context.InstallPath}...");

        if (Directory.Exists(context.InstallPath))
        {
            string[] sqlFiles = Directory.GetFiles(
                context.InstallPath,
                "*.sql",
                SearchOption.AllDirectories
            );
            if (sqlFiles.Length > 0)
            {
                Console.WriteLine($"[SQL Tools] Found {sqlFiles.Length} SQL script(s):");
                foreach (string file in sqlFiles)
                {
                    Console.WriteLine($"[SQL Tools]   - {file}");
                }
            }
            else
            {
                Console.WriteLine("[SQL Tools] No .sql files found in the package.");
            }
        }
        else
        {
            Console.WriteLine($"[SQL Tools] Install path does not exist: {context.InstallPath}");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnProductUninstalledAsync(string productId, CancellationToken ct = default)
    {
        Console.WriteLine($"[SQL Tools] SQL cleanup for {productId}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void OnNavigationTabSelected(string tabId)
    {
        Console.WriteLine($"[SQL Tools] SQL Status tab selected (tabId: {tabId})");
    }
}
