using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StorkDrop.Contracts;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model representing a single configuration field in the plugin config dialog.
/// </summary>
public partial class PluginConfigFieldViewModel : ObservableObject
{
    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private PluginFieldType _fieldType;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _required;

    [ObservableProperty]
    private ObservableCollection<PluginOptionItem> _options =
        new ObservableCollection<PluginOptionItem>();

    [ObservableProperty]
    private double? _min;

    [ObservableProperty]
    private double? _max;

    /// <summary>
    /// Gets a value indicating whether this field has a validation error.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
}
