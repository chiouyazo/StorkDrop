using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;
using StorkDrop.Contracts;

namespace StorkDrop.App.ViewModels;

public partial class PluginTabViewModel : ObservableObject
{
    private readonly IStorkDropPlugin _plugin;
    private readonly DialogService _dialogService;
    private readonly string _stepsConfigPath;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _pluginId = string.Empty;

    [ObservableProperty]
    private ObservableCollection<StepsDatabaseViewModel> _databases = [];

    [ObservableProperty]
    private ObservableCollection<StepsPathViewModel> _stepsPaths = [];

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PluginSettingsSectionViewModel> _sections = [];

    public bool IsStepsPlugin => PluginId == "creativity-steps";

    public PluginTabViewModel(IStorkDropPlugin plugin, DialogService dialogService)
    {
        _plugin = plugin;
        _dialogService = dialogService;
        Title = plugin.DisplayName;
        PluginId = plugin.PluginId;

        _stepsConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StorkDrop",
            "Config",
            "creativity-steps-config.json"
        );

        if (IsStepsPlugin)
            LoadStepsConfig();
        else
            LoadGenericSections();
    }

    private void LoadStepsConfig()
    {
        try
        {
            if (File.Exists(_stepsConfigPath))
            {
                string json = File.ReadAllText(_stepsConfigPath);
                using JsonDocument doc = JsonDocument.Parse(json);

                Databases.Clear();
                if (doc.RootElement.TryGetProperty("Databases", out JsonElement dbArray))
                {
                    foreach (JsonElement el in dbArray.EnumerateArray())
                    {
                        Databases.Add(
                            new StepsDatabaseViewModel
                            {
                                Name = el.TryGetProperty("Name", out var n)
                                    ? n.GetString() ?? ""
                                    : "",
                                Server = el.TryGetProperty("Server", out var s)
                                    ? s.GetString() ?? ""
                                    : "",
                                DatabaseName = el.TryGetProperty("DatabaseName", out var d)
                                    ? d.GetString() ?? ""
                                    : "",
                                UseWindowsAuth =
                                    el.TryGetProperty("UseWindowsAuth", out var w)
                                    && w.GetBoolean(),
                                Username = el.TryGetProperty("Username", out var u)
                                    ? u.GetString() ?? "sao"
                                    : "sao",
                                Password = el.TryGetProperty("Password", out var p)
                                    ? p.GetString() ?? "sao"
                                    : "sao",
                                TrustCertificate =
                                    !el.TryGetProperty("TrustCertificate", out var t)
                                    || t.GetBoolean(),
                            }
                        );
                    }
                }

                StepsPaths.Clear();
                if (doc.RootElement.TryGetProperty("StepsPaths", out JsonElement pathArray))
                {
                    foreach (JsonElement el in pathArray.EnumerateArray())
                    {
                        StepsPaths.Add(
                            new StepsPathViewModel
                            {
                                Name = el.TryGetProperty("Name", out var n)
                                    ? n.GetString() ?? ""
                                    : "",
                                Path = el.TryGetProperty("Path", out var p)
                                    ? p.GetString() ?? ""
                                    : "",
                                IsRemote =
                                    el.TryGetProperty("IsRemote", out var r) && r.GetBoolean(),
                                RdpHost = el.TryGetProperty("RdpHost", out var h)
                                    ? h.GetString() ?? ""
                                    : "",
                                RdpUsername = el.TryGetProperty("RdpUsername", out var u)
                                    ? u.GetString() ?? ""
                                    : "",
                                RdpPassword = el.TryGetProperty("RdpPassword", out var pw)
                                    ? pw.GetString() ?? ""
                                    : "",
                            }
                        );
                    }
                }
            }
        }
        catch { }
    }

    private void LoadGenericSections()
    {
        IReadOnlyList<PluginSettingsSection> pluginSections = _plugin.GetSettingsSections();
        foreach (PluginSettingsSection section in pluginSections)
        {
            PluginSettingsSectionViewModel sectionVm = new()
            {
                Title = section.Title,
                SectionId = section.SectionId,
            };
            foreach (PluginConfigField field in section.Fields)
            {
                sectionVm.Fields.Add(
                    new PluginConfigFieldViewModel
                    {
                        Key = field.Key,
                        Label = field.Label,
                        Description = field.Description ?? string.Empty,
                        FieldType = field.FieldType,
                        Required = field.Required,
                        Options = new ObservableCollection<PluginOptionItem>(field.Options),
                        Value = field.DefaultValue ?? string.Empty,
                    }
                );
            }
            Sections.Add(sectionVm);
        }
    }

    [RelayCommand]
    private void AddDatabase()
    {
        Databases.Add(
            new StepsDatabaseViewModel
            {
                Name = $"Database {Databases.Count + 1}",
                DatabaseName = "STEPS_Basis_2025_05_00",
                Username = "sao",
                Password = "sao",
                TrustCertificate = true,
            }
        );
    }

    [RelayCommand]
    private void RemoveDatabase(StepsDatabaseViewModel db) => Databases.Remove(db);

    [RelayCommand]
    private void AddStepsPath()
    {
        StepsPaths.Add(
            new StepsPathViewModel
            {
                Name = $"STEPS {StepsPaths.Count + 1}",
                Path = @"C:\Program Files (x86)\STAPS\Application",
            }
        );
    }

    [RelayCommand]
    private void RemoveStepsPath(StepsPathViewModel path) => StepsPaths.Remove(path);

    [RelayCommand]
    private void BrowseStepsPath(StepsPathViewModel path)
    {
        string? folder = _dialogService.ShowFolderPicker("Select STEPS Application Directory");
        if (folder is not null)
            path.Path = folder;
    }

    [RelayCommand]
    private async Task TestDatabaseAsync(StepsDatabaseViewModel db)
    {
        db.ConnectionTestMessage = LocalizationManager.GetString("Status_Connecting");
        db.ConnectionTestSuccess = null;

        try
        {
            string cs = $"Server={db.Server};Database={db.DatabaseName}";
            if (db.UseWindowsAuth)
                cs += ";Integrated Security=true";
            else
                cs += $";User Id={db.Username};Password={db.Password}";
            if (db.TrustCertificate)
                cs += ";TrustServerCertificate=true";
            cs += ";Connect Timeout=5";

            await Task.Run(() =>
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
                conn.Open();
            });

            db.ConnectionTestMessage = LocalizationManager.GetString("Status_TestSuccess");
            db.ConnectionTestSuccess = true;
        }
        catch (Exception ex)
        {
            db.ConnectionTestMessage = ex.Message;
            db.ConnectionTestSuccess = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            var config = new
            {
                Databases = Databases.Select(db => new
                {
                    db.Name,
                    db.Server,
                    db.DatabaseName,
                    db.UseWindowsAuth,
                    db.Username,
                    db.Password,
                    db.TrustCertificate,
                }),
                StepsPaths = StepsPaths.Select(p => new
                {
                    p.Name,
                    p.Path,
                    p.IsRemote,
                    p.RdpHost,
                    p.RdpUsername,
                    p.RdpPassword,
                }),
            };

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_stepsConfigPath)!);
            string json = JsonSerializer.Serialize(
                config,
                new JsonSerializerOptions { WriteIndented = true }
            );
            File.WriteAllText(_stepsConfigPath, json);
            StatusMessage = "Saved successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = "Save failed: " + ex.Message;
        }
    }
}

public partial class StepsDatabaseViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _server = string.Empty;

    [ObservableProperty]
    private string _databaseName = string.Empty;

    [ObservableProperty]
    private bool _useWindowsAuth;

    [ObservableProperty]
    private string _username = "sao";

    [ObservableProperty]
    private string _password = "sao";

    [ObservableProperty]
    private bool _trustCertificate = true;

    [ObservableProperty]
    private string _connectionTestMessage = string.Empty;

    [ObservableProperty]
    private bool? _connectionTestSuccess;
}

public partial class StepsPathViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private bool _isRemote;

    [ObservableProperty]
    private string _rdpHost = string.Empty;

    [ObservableProperty]
    private string _rdpUsername = string.Empty;

    [ObservableProperty]
    private string _rdpPassword = string.Empty;
}

public partial class PluginSettingsSectionViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _sectionId = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PluginConfigFieldViewModel> _fields = [];
}
