using CommunityToolkit.Mvvm.ComponentModel;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model representing a single installed product in the installed products list.
/// </summary>
public partial class InstalledProductViewModel : ObservableObject
{
    [ObservableProperty]
    private string _productId = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _version = string.Empty;

    [ObservableProperty]
    private string _installedPath = string.Empty;

    [ObservableProperty]
    private DateTime _installedDate;

    [ObservableProperty]
    private bool _hasPlugins;

    [ObservableProperty]
    private InstallType _installType;

    public bool IsExecutable => InstallType == InstallType.Executable;
}
