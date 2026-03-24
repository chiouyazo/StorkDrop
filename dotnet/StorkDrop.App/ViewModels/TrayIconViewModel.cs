using CommunityToolkit.Mvvm.ComponentModel;

namespace StorkDrop.App.ViewModels;

public partial class TrayIconViewModel : ObservableObject
{
    [ObservableProperty]
    private string _toolTip = "StorkDrop";

    [ObservableProperty]
    private bool _hasUpdates;
}
