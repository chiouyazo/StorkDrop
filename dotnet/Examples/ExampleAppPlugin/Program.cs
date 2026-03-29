using ExampleAppPlugin;
using StorkDrop.Contracts;

SqlToolsPlugin plugin = new SqlToolsPlugin();

Console.WriteLine($"[Debug] Plugin: {plugin.DisplayName} ({plugin.PluginId})");
Console.WriteLine($"[Debug] Associated feeds: {string.Join(", ", plugin.AssociatedFeeds)}");
Console.WriteLine();

// --- Setup Steps ---
Console.WriteLine("[Debug] === GetSetupSteps ===");
IReadOnlyList<PluginSetupStep> steps = plugin.GetSetupSteps();
Console.WriteLine($"[Debug] Steps: {steps.Count}");
foreach (PluginSetupStep step in steps)
{
    Console.WriteLine($"[Debug]   {step.StepId}: {step.Title}");
    Console.WriteLine($"[Debug]     {step.Description}");
    foreach (PluginConfigField field in step.Fields)
    {
        string requiredMarker = field.Required ? " *" : "";
        Console.WriteLine($"[Debug]     Field: {field.Key} ({field.FieldType}){requiredMarker}");
    }
}
Console.WriteLine();

// --- Settings Sections ---
Console.WriteLine("[Debug] === GetSettingsSections ===");
IReadOnlyList<PluginSettingsSection> sections = plugin.GetSettingsSections();
Console.WriteLine($"[Debug] Sections: {sections.Count}");
foreach (PluginSettingsSection section in sections)
{
    Console.WriteLine($"[Debug]   {section.SectionId}: {section.Title}");
    foreach (PluginConfigField field in section.Fields)
    {
        string requiredMarker = field.Required ? " *" : "";
        Console.WriteLine($"[Debug]     Field: {field.Key} ({field.FieldType}){requiredMarker}");
        if (field.FieldType == PluginFieldType.Group && field.SubFields.Count > 0)
        {
            Console.WriteLine($"[Debug]       SubFields ({field.SubFields.Count}):");
            foreach (PluginConfigField sub in field.SubFields)
            {
                Console.WriteLine(
                    $"[Debug]         - {sub.Key} ({sub.FieldType}){(sub.Required ? " *" : "")}"
                );
            }
        }
    }
}
Console.WriteLine();

// --- Navigation Tabs ---
Console.WriteLine("[Debug] === GetNavigationTabs ===");
IReadOnlyList<PluginNavTab> tabs = plugin.GetNavigationTabs();
Console.WriteLine($"[Debug] Tabs: {tabs.Count}");
foreach (PluginNavTab tab in tabs)
{
    Console.WriteLine($"[Debug]   {tab.TabId}: {tab.DisplayName} (icon: {tab.Icon})");
}
Console.WriteLine();

// --- OnProductInstalledAsync ---
Console.WriteLine("[Debug] === OnProductInstalledAsync ===");

// Create a temp directory with a sample .sql file to demonstrate scanning
string tempInstallPath = Path.Combine(Path.GetTempPath(), "StorkDropSqlExample");
Directory.CreateDirectory(tempInstallPath);
File.WriteAllText(
    Path.Combine(tempInstallPath, "init.sql"),
    "CREATE TABLE Example (Id INT PRIMARY KEY);"
);

PluginInstallContext installContext = new PluginInstallContext
{
    ProductId = "sql-reporting",
    Version = "1.0.0",
    InstallPath = tempInstallPath,
};
await plugin.OnProductInstalledAsync(installContext);
Console.WriteLine();

// --- OnProductUninstalledAsync ---
Console.WriteLine("[Debug] === OnProductUninstalledAsync ===");
await plugin.OnProductUninstalledAsync("sql-reporting");
Console.WriteLine();

// --- OnNavigationTabSelected ---
Console.WriteLine("[Debug] === OnNavigationTabSelected ===");
plugin.OnNavigationTabSelected("sql-status");
Console.WriteLine();

// Cleanup
if (Directory.Exists(tempInstallPath))
{
    Directory.Delete(tempInstallPath, true);
}

Console.WriteLine("[Debug] === Done ===");
