using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StorkDrop.App.Localization;
using StorkDrop.Contracts;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model for the plugin configuration dialog, managing dynamic configuration fields.
/// </summary>
public partial class PluginConfigDialogViewModel : ObservableObject
{
    private readonly Func<PluginContext, IReadOnlyList<PluginValidationError>>? _validateCallback;

    [ObservableProperty]
    private ObservableCollection<PluginConfigFieldViewModel> _fields =
        new ObservableCollection<PluginConfigFieldViewModel>();

    [ObservableProperty]
    private string _globalErrorMessage = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfigDialogViewModel"/> class.
    /// </summary>
    /// <param name="schema">The configuration field schema from the plugin.</param>
    /// <param name="previousValues">Previously saved configuration values.</param>
    /// <param name="environment">The plugin environment, or null if not available.</param>
    /// <param name="validateCallback">An optional validation callback from the plugin.</param>
    public PluginConfigDialogViewModel(
        IReadOnlyList<PluginConfigField> schema,
        Dictionary<string, string> previousValues,
        PluginEnvironment? environment = null,
        Func<PluginContext, IReadOnlyList<PluginValidationError>>? validateCallback = null
    )
    {
        _validateCallback = validateCallback;

        foreach (PluginConfigField field in schema)
        {
            PluginConfigFieldViewModel fieldVm = new PluginConfigFieldViewModel()
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

            if (previousValues.TryGetValue(field.Key, out string? previousValue))
            {
                fieldVm.Value = previousValue;
            }
            else if (!string.IsNullOrEmpty(field.DefaultValue))
            {
                fieldVm.Value = field.DefaultValue;
            }

            Fields.Add(fieldVm);
        }
    }

    /// <summary>
    /// Validates all configuration fields and returns whether validation passed.
    /// </summary>
    /// <returns>True if all fields are valid; otherwise false.</returns>
    public bool Validate()
    {
        bool isValid = true;
        GlobalErrorMessage = string.Empty;

        foreach (PluginConfigFieldViewModel field in Fields)
        {
            field.ErrorMessage = string.Empty;

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
                        LocalizationManager.GetString("Validation_Minimum") + $": {field.Min.Value}";
                    isValid = false;
                    continue;
                }

                if (field.Max.HasValue && numValue > field.Max.Value)
                {
                    field.ErrorMessage =
                        LocalizationManager.GetString("Validation_Maximum") + $": {field.Max.Value}";
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
                PluginContext context = new PluginContext() { ConfigValues = GetValues() };
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
                GlobalErrorMessage = LocalizationManager.GetString("Error_ValidationFailed") + ": " + ex.Message;
                isValid = false;
            }
        }

        return isValid;
    }

    /// <summary>
    /// Gets the current configuration values as a dictionary.
    /// </summary>
    /// <returns>A dictionary of field keys to their current values.</returns>
    public Dictionary<string, string> GetValues()
    {
        Dictionary<string, string> values = new Dictionary<string, string>();
        foreach (PluginConfigFieldViewModel field in Fields)
        {
            values[field.Key] = field.Value;
        }
        return values;
    }
}
