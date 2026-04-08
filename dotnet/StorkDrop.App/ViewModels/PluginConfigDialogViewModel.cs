using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StorkDrop.App.Localization;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.ViewModels;

public partial class PluginConfigDialogViewModel : ObservableObject
{
    private readonly Func<PluginContext, IReadOnlyList<PluginValidationError>>? _validateCallback;

    [ObservableProperty]
    private ObservableCollection<PluginConfigFieldViewModel> _fields =
        new ObservableCollection<PluginConfigFieldViewModel>();

    [ObservableProperty]
    private ObservableCollection<ActionGroupViewModel> _actionGroups =
        new ObservableCollection<ActionGroupViewModel>();

    [ObservableProperty]
    private string _globalErrorMessage = string.Empty;

    public bool HasActionGroups => ActionGroups.Count > 0;

    public IInteractiveStorkPlugin? InteractivePlugin { get; set; }

    public PluginConfigDialogViewModel(
        IReadOnlyList<PluginConfigField> schema,
        Dictionary<string, string> previousValues,
        PluginEnvironment? environment = null,
        Func<PluginContext, IReadOnlyList<PluginValidationError>>? validateCallback = null
    )
    {
        _validateCallback = validateCallback;
        BuildFields(schema, previousValues);
    }

    public PluginConfigDialogViewModel(
        IReadOnlyList<PluginActionGroup> groups,
        Dictionary<string, string> previousValues
    )
    {
        _validateCallback = null;
        BuildActionGroups(groups, previousValues);
    }

    public void HandleButtonClick(PluginConfigFieldViewModel field)
    {
        if (InteractivePlugin is null)
            return;

        try
        {
            PluginButtonResult result = InteractivePlugin.OnButtonClicked(field.Key, GetValues());

            if (result.StatusText is not null)
            {
                field.StatusText = result.StatusText;
                field.IsStatusError = result.IsError;
            }

            if (result.UpdatedSchema is not null)
            {
                Dictionary<string, string> currentValues = GetValues();
                if (HasActionGroups)
                    RebuildGroupFields(result.UpdatedSchema, currentValues);
                else
                    BuildFields(result.UpdatedSchema, currentValues);
            }
        }
        catch (Exception ex)
        {
            field.StatusText = ex.Message;
            field.IsStatusError = true;
        }
    }

    public void BuildFields(
        IReadOnlyList<PluginConfigField> schema,
        Dictionary<string, string> previousValues
    )
    {
        Fields.Clear();
        foreach (PluginConfigField field in schema)
        {
            PluginConfigFieldViewModel fieldVm = CreateFieldViewModel(field, previousValues);
            Fields.Add(fieldVm);
        }
    }

    private void BuildActionGroups(
        IReadOnlyList<PluginActionGroup> groups,
        Dictionary<string, string> previousValues
    )
    {
        ActionGroups.Clear();
        Fields.Clear();

        foreach (PluginActionGroup group in groups)
        {
            ActionGroupViewModel groupVm = new ActionGroupViewModel
            {
                GroupId = group.GroupId,
                Title = group.Title,
                Phase = group.Phase,
                IsEnabled = group.IsEnabled,
                Descriptions = new ObservableCollection<PluginActionDescription>(
                    group.Descriptions
                ),
            };

            foreach (PluginConfigField field in group.Fields)
            {
                PluginConfigFieldViewModel fieldVm = CreateFieldViewModel(field, previousValues);
                fieldVm.IsEnabled = group.IsEnabled;
                groupVm.Fields.Add(fieldVm);
                Fields.Add(fieldVm);
            }

            ActionGroups.Add(groupVm);
        }

        OnPropertyChanged(nameof(HasActionGroups));
    }

    private void RebuildGroupFields(
        IReadOnlyList<PluginConfigField> updatedSchema,
        Dictionary<string, string> currentValues
    )
    {
        Dictionary<string, PluginConfigField> updatedByKey = updatedSchema.ToDictionary(f => f.Key);

        foreach (ActionGroupViewModel group in ActionGroups)
        {
            foreach (PluginConfigFieldViewModel fieldVm in group.Fields)
            {
                if (updatedByKey.TryGetValue(fieldVm.Key, out PluginConfigField? updated))
                {
                    fieldVm.Options = new ObservableCollection<PluginOptionItem>(updated.Options);
                    if (updated.DefaultValue is not null && !currentValues.ContainsKey(fieldVm.Key))
                        fieldVm.Value = updated.DefaultValue;
                    fieldVm.Description = updated.Description ?? string.Empty;
                    fieldVm.IsEnabled = updated.IsEnabled;
                    fieldVm.IsReadOnly = updated.IsReadOnly;
                }
            }
        }
    }

    private static PluginConfigFieldViewModel CreateFieldViewModel(
        PluginConfigField field,
        Dictionary<string, string> previousValues
    )
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
            IsEnabled = field.IsEnabled,
            IsReadOnly = field.IsReadOnly,
        };

        if (previousValues.TryGetValue(field.Key, out string? previousValue))
            fieldVm.Value = previousValue;
        else if (!string.IsNullOrEmpty(field.DefaultValue))
            fieldVm.Value = field.DefaultValue;

        return fieldVm;
    }

    public bool Validate()
    {
        bool isValid = true;
        GlobalErrorMessage = string.Empty;

        HashSet<string> disabledGroupFields = new HashSet<string>();
        foreach (ActionGroupViewModel group in ActionGroups)
        {
            if (!group.IsEnabled)
            {
                foreach (PluginConfigFieldViewModel field in group.Fields)
                    disabledGroupFields.Add(field.Key);
            }
        }

        foreach (PluginConfigFieldViewModel field in Fields)
        {
            field.ErrorMessage = string.Empty;

            if (disabledGroupFields.Contains(field.Key))
                continue;

            if (!field.IsEnabled)
                continue;

            if (field.Required && string.IsNullOrWhiteSpace(field.Value))
            {
                field.ErrorMessage = LocalizationManager.GetString("Validation_FieldRequired");
                isValid = false;
                continue;
            }

            if (field.FieldType is PluginFieldType.Number && !string.IsNullOrEmpty(field.Value))
            {
                if (!double.TryParse(field.Value, out double numValue))
                {
                    field.ErrorMessage = LocalizationManager.GetString("Validation_InvalidNumber");
                    isValid = false;
                    continue;
                }

                if (field.Min.HasValue && numValue < field.Min.Value)
                {
                    field.ErrorMessage =
                        LocalizationManager.GetString("Validation_Minimum")
                        + $": {field.Min.Value}";
                    isValid = false;
                    continue;
                }

                if (field.Max.HasValue && numValue > field.Max.Value)
                {
                    field.ErrorMessage =
                        LocalizationManager.GetString("Validation_Maximum")
                        + $": {field.Max.Value}";
                    isValid = false;
                    continue;
                }
            }

            if (
                field.FieldType is PluginFieldType.Checkbox
                && !string.IsNullOrEmpty(field.Value)
                && !bool.TryParse(field.Value, out _)
            )
            {
                field.ErrorMessage = LocalizationManager.GetString("Validation_InvalidBoolean");
                isValid = false;
            }
        }

        if (isValid && _validateCallback is not null)
        {
            try
            {
                PluginContext context = new PluginContext { ConfigValues = GetValues() };
                IReadOnlyList<PluginValidationError> errors = _validateCallback(context);
                foreach (PluginValidationError error in errors)
                {
                    PluginConfigFieldViewModel? field = Fields.FirstOrDefault(f =>
                        f.Key == error.FieldKey
                    );
                    if (field is not null)
                    {
                        field.ErrorMessage = error.Message;
                        isValid = false;
                    }
                    else
                    {
                        GlobalErrorMessage = error.Message;
                        isValid = false;
                    }
                }
            }
            catch (Exception ex)
            {
                GlobalErrorMessage =
                    LocalizationManager.GetString("Error_ValidationFailed") + ": " + ex.Message;
                isValid = false;
            }
        }

        return isValid;
    }

    public Dictionary<string, string> GetValues()
    {
        Dictionary<string, string> values = new Dictionary<string, string>();
        foreach (PluginConfigFieldViewModel field in Fields)
        {
            values[field.Key] = field.Value;
        }

        foreach (ActionGroupViewModel group in ActionGroups)
        {
            values[$"__group_enabled_{group.GroupId}"] = group
                .IsEnabled.ToString()
                .ToLowerInvariant();
        }

        return values;
    }
}
