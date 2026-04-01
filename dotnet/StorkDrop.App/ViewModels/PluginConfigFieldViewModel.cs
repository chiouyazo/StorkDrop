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

    /// <summary>
    /// String representation of FieldType for XAML DataTrigger binding.
    /// Avoids type identity issues when plugins are loaded from separate AssemblyLoadContexts.
    /// </summary>
    public string FieldTypeName => FieldType.ToString();

    partial void OnFieldTypeChanged(PluginFieldType value) =>
        OnPropertyChanged(nameof(FieldTypeName));

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

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isStatusError;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasStatusText => !string.IsNullOrEmpty(StatusText);

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatusText));
}
