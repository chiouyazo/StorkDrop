using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;

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
    private readonly IPluginSettingsStore _settingsStore;
    private readonly ILogger<PluginTabViewModel> _logger;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _pluginId = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PluginSettingsSectionViewModel> _sections = [];

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public PluginTabViewModel(
        IStorkDropPlugin plugin,
        DialogService dialogService,
        IPluginSettingsStore settingsStore,
        ILogger<PluginTabViewModel> logger
    )
    {
        _plugin = plugin;
        _dialogService = dialogService;
        _settingsStore = settingsStore;
        _logger = logger;
        Title = plugin.DisplayName;
        PluginId = plugin.PluginId;

        _ = LoadSectionsAsync();
    }

    private async Task LoadSectionsAsync()
    {
        Dictionary<string, string> savedValues = await LoadSavedValuesAsync();
        IReadOnlyList<PluginSettingsSection> pluginSections = _plugin.GetSettingsSections();
        ObservableCollection<PluginSettingsSectionViewModel> sectionVms = [];

        foreach (PluginSettingsSection section in pluginSections)
        {
            PluginSettingsSectionViewModel sectionVm = new PluginSettingsSectionViewModel
            {
                Title = section.Title,
                SectionId = section.SectionId,
            };

            foreach (PluginConfigField field in section.Fields)
            {
                if (field.FieldType == PluginFieldType.Group)
                {
                    GroupFieldViewModel groupVm = new GroupFieldViewModel
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
                            List<Dictionary<string, string>>? items = JsonSerializer.Deserialize<
                                List<Dictionary<string, string>>
                            >(groupJson);
                            if (items is not null)
                            {
                                foreach (Dictionary<string, string> item in items)
                                {
                                    GroupInstanceViewModel instance = CreateGroupInstance(
                                        field.SubFields,
                                        item
                                    );
                                    groupVm.Instances.Add(instance);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Failed to deserialize group field {Key} for plugin {PluginId}",
                                field.Key,
                                _plugin.PluginId
                            );
                        }
                    }

                    sectionVm.GroupFields.Add(groupVm);
                }
                else
                {
                    PluginConfigFieldViewModel fieldVm = new PluginConfigFieldViewModel
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

                    EnsureSelectedValueIsAnOption(fieldVm);
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
        GroupInstanceViewModel instance = new GroupInstanceViewModel();
        foreach (PluginConfigField tmpl in templates)
        {
            PluginConfigFieldViewModel fieldVm = new PluginConfigFieldViewModel
            {
                Key = tmpl.Key,
                Label = tmpl.Label,
                Description = tmpl.Description ?? string.Empty,
                FieldType = tmpl.FieldType,
                Required = tmpl.Required,
                Options = new ObservableCollection<PluginOptionItem>(tmpl.Options),
                Min = tmpl.Min,
                Max = tmpl.Max,
                IsEnabled = tmpl.IsEnabled,
                EnabledWhen = tmpl.EnabledWhen,
            };

            if (values is not null && values.TryGetValue(tmpl.Key, out string? val))
                fieldVm.Value = val;
            else if (!string.IsNullOrEmpty(tmpl.DefaultValue))
                fieldVm.Value = tmpl.DefaultValue;

            EnsureSelectedValueIsAnOption(fieldVm);
            instance.Fields.Add(fieldVm);
        }

        WireEnabledConditions(instance);
        return instance;
    }

    private static void WireEnabledConditions(GroupInstanceViewModel instance)
    {
        void Evaluate()
        {
            Dictionary<string, string> values = instance.Fields.ToDictionary(
                f => f.Key,
                f => f.Value
            );
            foreach (PluginConfigFieldViewModel field in instance.Fields)
            {
                if (field.EnabledWhen is not null)
                    field.IsEnabled = field.EnabledWhen(values);
            }
        }

        foreach (PluginConfigFieldViewModel field in instance.Fields)
        {
            field.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PluginConfigFieldViewModel.Value))
                    Evaluate();
            };
        }

        Evaluate();
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

    /// <summary>
    /// Runs a Button field's action via <see cref="IInteractiveStorkPlugin.OnButtonClicked"/> and applies
    /// the result: status text plus any <see cref="PluginButtonResult.UpdatedSchema"/> (used e.g. to fill a
    /// dropdown's options). When the button sits inside a group instance, only that instance's values are
    /// passed and only its fields are updated, so the action is scoped to the row it was clicked in.
    /// </summary>
    public async Task HandleButtonClickAsync(
        PluginConfigFieldViewModel field,
        GroupInstanceViewModel? instance
    )
    {
        if (_plugin is not IInteractiveStorkPlugin interactive)
        {
            field.StatusText = LocalizationManager.GetString("Plugin_Settings_ButtonUnsupported");
            field.IsStatusError = true;
            return;
        }

        try
        {
            Dictionary<string, string> values = instance is not null
                ? instance.Fields.ToDictionary(f => f.Key, f => f.Value)
                : CollectValues();

            PluginButtonResult result = await Task.Run(() =>
                interactive.OnButtonClicked(field.Key, values)
            );

            field.StatusText = result.StatusText ?? string.Empty;
            field.IsStatusError = result.IsError;

            if (result.UpdatedSchema is not null)
                ApplyUpdatedSchema(result.UpdatedSchema, instance);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Plugin button {Key} failed for {PluginId}",
                field.Key,
                PluginId
            );
            field.StatusText = ex.Message;
            field.IsStatusError = true;
        }
    }

    /// <summary>
    /// Merges an updated schema into the live fields by key: refreshes options (and the value, when the
    /// schema provides a default). Structural changes are not applied - the settings tab keeps its section
    /// layout; this covers the common "populate a dropdown" case.
    /// </summary>
    private void ApplyUpdatedSchema(
        IReadOnlyList<PluginConfigField> schema,
        GroupInstanceViewModel? instance
    )
    {
        IEnumerable<PluginConfigFieldViewModel> targets = instance is not null
            ? instance.Fields
            : Sections
                .SelectMany(s => s.Fields)
                .Concat(
                    Sections
                        .SelectMany(s => s.GroupFields)
                        .SelectMany(g => g.Instances)
                        .SelectMany(i => i.Fields)
                );
        List<PluginConfigFieldViewModel> targetList = targets.ToList();

        foreach (PluginConfigField updated in schema)
        {
            PluginConfigFieldViewModel? vm = targetList.FirstOrDefault(f => f.Key == updated.Key);
            if (vm is null)
                continue;

            string previousValue = vm.Value;
            vm.Options = new ObservableCollection<PluginOptionItem>(updated.Options);

            // Keep the previous selection when it still exists in the refreshed list; otherwise fall back to
            // the schema default.
            bool previousStillValid =
                !string.IsNullOrEmpty(previousValue)
                && vm.Options.Any(o =>
                    string.Equals(o.Value, previousValue, StringComparison.Ordinal)
                );
            string desired = previousStillValid
                ? previousValue
                : updated.DefaultValue ?? previousValue;

            vm.Value = desired;

            PluginConfigFieldViewModel targetVm = vm;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    if (!string.Equals(targetVm.Value, desired, StringComparison.Ordinal))
                        targetVm.Value = desired;
                })
            );
        }
    }

    /// <summary>
    /// Makes a dropdown's currently-selected value a selectable option when it is not already one - e.g. a
    /// database chosen in an earlier session whose option list has not been (re)loaded yet. Without this the
    /// ComboBox shows blank and clears the value on save, silently dropping the existing configuration.
    /// </summary>
    private static void EnsureSelectedValueIsAnOption(PluginConfigFieldViewModel field)
    {
        if (
            field.FieldType != PluginFieldType.Dropdown
            || string.IsNullOrEmpty(field.Value)
            || field.Options.Any(o => string.Equals(o.Value, field.Value, StringComparison.Ordinal))
        )
            return;

        field.Options.Insert(0, new PluginOptionItem { Value = field.Value, Label = field.Value });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        List<string> missing = CollectMissingRequired();
        if (missing.Count > 0)
        {
            StatusMessage = LocalizationManager.GetString(
                "Plugin_Settings_RequiredMissing",
                string.Join(", ", missing)
            );
            return;
        }

        try
        {
            Dictionary<string, string> values = CollectValues();
            await Task.Run(() => _settingsStore.SaveAsync(_plugin.PluginId, values));
            StatusMessage = LocalizationManager.GetString("Status_TestSuccess");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to save plugin settings for {PluginId}",
                _plugin.PluginId
            );
            StatusMessage = LocalizationManager.GetString("Error_SaveFailed") + ": " + ex.Message;
        }
    }

    private List<string> CollectMissingRequired()
    {
        List<string> missing = [];
        foreach (PluginSettingsSectionViewModel section in Sections)
        {
            foreach (PluginConfigFieldViewModel field in section.Fields)
            {
                if (field.Required && field.IsEnabled && string.IsNullOrWhiteSpace(field.Value))
                    missing.Add(field.Label);
            }

            foreach (GroupFieldViewModel group in section.GroupFields)
            {
                foreach (GroupInstanceViewModel instance in group.Instances)
                {
                    foreach (PluginConfigFieldViewModel field in instance.Fields)
                    {
                        if (
                            field.Required
                            && field.IsEnabled
                            && string.IsNullOrWhiteSpace(field.Value)
                        )
                            missing.Add($"{group.Label} → {field.Label}");
                    }
                }
            }
        }
        return missing.Distinct().ToList();
    }

    private Dictionary<string, string> CollectValues()
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
        return values;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadSectionsAsync();
        StatusMessage = string.Empty;
    }

    private async Task<Dictionary<string, string>> LoadSavedValuesAsync()
    {
        try
        {
            return await Task.Run(() => _settingsStore.LoadAsync(_plugin.PluginId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to load saved config values for plugin {PluginId}",
                _plugin.PluginId
            );
        }
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
