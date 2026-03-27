using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;
using StorkDrop.Contracts;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// Generic ViewModel for a plugin's navigation tab.
/// Renders the plugin's GetSettingsSections() as an editable form.
/// Supports Group fields (repeatable add/remove groups).
/// Persists values to a JSON file per plugin.
/// </summary>
public partial class PluginTabViewModel : ObservableObject
{
    private readonly IStorkDropPlugin _plugin;
    private readonly DialogService _dialogService;
    private readonly string _configPath;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _pluginId = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PluginSettingsSectionViewModel> _sections = [];

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public PluginTabViewModel(IStorkDropPlugin plugin, DialogService dialogService)
    {
        _plugin = plugin;
        _dialogService = dialogService;
        Title = plugin.DisplayName;
        PluginId = plugin.PluginId;

        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StorkDrop",
            "Config",
            $"plugin-settings-{plugin.PluginId}.json"
        );

        LoadSections();
    }

    private void LoadSections()
    {
        Dictionary<string, string> savedValues = LoadSavedValues();
        IReadOnlyList<PluginSettingsSection> pluginSections = _plugin.GetSettingsSections();
        ObservableCollection<PluginSettingsSectionViewModel> sectionVms = [];

        foreach (PluginSettingsSection section in pluginSections)
        {
            PluginSettingsSectionViewModel sectionVm = new()
            {
                Title = section.Title,
                SectionId = section.SectionId,
            };

            foreach (PluginConfigField field in section.Fields)
            {
                if (field.FieldType == PluginFieldType.Group)
                {
                    GroupFieldViewModel groupVm = new()
                    {
                        Key = field.Key,
                        Label = field.Label,
                        Description = field.Description ?? string.Empty,
                        SubFieldTemplates = field.SubFields,
                    };

                    if (savedValues.TryGetValue(field.Key, out string? groupJson))
                    {
                        try
                        {
                            var items = JsonSerializer.Deserialize<
                                List<Dictionary<string, string>>
                            >(groupJson);
                            if (items is not null)
                            {
                                foreach (var item in items)
                                {
                                    GroupInstanceViewModel instance = CreateGroupInstance(
                                        field.SubFields,
                                        item
                                    );
                                    groupVm.Instances.Add(instance);
                                }
                            }
                        }
                        catch { }
                    }

                    sectionVm.GroupFields.Add(groupVm);
                }
                else
                {
                    PluginConfigFieldViewModel fieldVm = new()
                    {
                        Key = field.Key,
                        Label = field.Label,
                        Description = field.Description ?? string.Empty,
                        FieldType = field.FieldType,
                        Required = field.Required,
                        Options = new ObservableCollection<PluginOptionItem>(field.Options),
                        Min = field.Min,
                        Max = field.Max,
                    };

                    if (savedValues.TryGetValue(field.Key, out string? saved))
                        fieldVm.Value = saved;
                    else if (!string.IsNullOrEmpty(field.DefaultValue))
                        fieldVm.Value = field.DefaultValue;

                    sectionVm.Fields.Add(fieldVm);
                }
            }

            sectionVms.Add(sectionVm);
        }

        Sections = sectionVms;
    }

    private static GroupInstanceViewModel CreateGroupInstance(
        List<PluginConfigField> templates,
        Dictionary<string, string>? values = null
    )
    {
        GroupInstanceViewModel instance = new();
        foreach (PluginConfigField tmpl in templates)
        {
            PluginConfigFieldViewModel fieldVm = new()
            {
                Key = tmpl.Key,
                Label = tmpl.Label,
                Description = tmpl.Description ?? string.Empty,
                FieldType = tmpl.FieldType,
                Required = tmpl.Required,
                Options = new ObservableCollection<PluginOptionItem>(tmpl.Options),
                Min = tmpl.Min,
                Max = tmpl.Max,
            };

            if (values is not null && values.TryGetValue(tmpl.Key, out string? val))
                fieldVm.Value = val;
            else if (!string.IsNullOrEmpty(tmpl.DefaultValue))
                fieldVm.Value = tmpl.DefaultValue;

            instance.Fields.Add(fieldVm);
        }
        return instance;
    }

    [RelayCommand]
    private void AddGroupInstance(GroupFieldViewModel group)
    {
        GroupInstanceViewModel instance = CreateGroupInstance(group.SubFieldTemplates);
        group.Instances.Add(instance);
    }

    [RelayCommand]
    private void RemoveGroupInstance(RemoveGroupInstanceRequest request)
    {
        request.Group.Instances.Remove(request.Instance);
    }

    [RelayCommand]
    private void BrowsePath(PluginConfigFieldViewModel field)
    {
        string? folder = _dialogService.ShowFolderPicker(field.Label, field.Value);
        if (folder is not null)
            field.Value = folder;
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            Dictionary<string, string> values = [];

            foreach (PluginSettingsSectionViewModel section in Sections)
            {
                foreach (PluginConfigFieldViewModel field in section.Fields)
                    values[field.Key] = field.Value;

                foreach (GroupFieldViewModel group in section.GroupFields)
                {
                    List<Dictionary<string, string>> items = [];
                    foreach (GroupInstanceViewModel instance in group.Instances)
                    {
                        Dictionary<string, string> item = [];
                        foreach (PluginConfigFieldViewModel field in instance.Fields)
                            item[field.Key] = field.Value;
                        items.Add(item);
                    }
                    values[group.Key] = JsonSerializer.Serialize(items);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            string json = JsonSerializer.Serialize(
                values,
                new JsonSerializerOptions { WriteIndented = true }
            );
            File.WriteAllText(_configPath, json);
            StatusMessage = LocalizationManager.GetString("Status_TestSuccess");
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.GetString("Error_SaveFailed") + ": " + ex.Message;
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadSections();
        StatusMessage = string.Empty;
    }

    private Dictionary<string, string> LoadSavedValues()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                string json = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            }
        }
        catch { }
        return [];
    }
}

public partial class PluginSettingsSectionViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _sectionId = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PluginConfigFieldViewModel> _fields = [];

    [ObservableProperty]
    private ObservableCollection<GroupFieldViewModel> _groupFields = [];
}

public partial class GroupFieldViewModel : ObservableObject
{
    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private ObservableCollection<GroupInstanceViewModel> _instances = [];

    public List<PluginConfigField> SubFieldTemplates { get; set; } = [];
}

public partial class GroupInstanceViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<PluginConfigFieldViewModel> _fields = [];
}

/// <summary>
/// Helper to pass both group and instance to the remove command.
/// </summary>
public sealed class RemoveGroupInstanceRequest
{
    public required GroupFieldViewModel Group { get; init; }
    public required GroupInstanceViewModel Instance { get; init; }
}
