using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.ViewModels;

public partial class ActionGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private string _groupId = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private PluginActionPhase _phase;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private ObservableCollection<PluginConfigFieldViewModel> _fields =
        new ObservableCollection<PluginConfigFieldViewModel>();

    [ObservableProperty]
    private ObservableCollection<PluginActionDescription> _descriptions =
        new ObservableCollection<PluginActionDescription>();

    public bool HasFields => Fields.Count > 0;
    public bool HasDescriptions => Descriptions.Count > 0;

    partial void OnIsEnabledChanged(bool value)
    {
        foreach (PluginConfigFieldViewModel field in Fields)
            field.IsEnabled = value;
    }
}
