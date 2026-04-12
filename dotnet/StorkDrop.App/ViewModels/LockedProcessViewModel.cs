using CommunityToolkit.Mvvm.ComponentModel;

namespace StorkDrop.App.ViewModels;

public partial class LockedProcessViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _processName = string.Empty;

    [ObservableProperty]
    private int _processId;

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _startTimeDisplay = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;
}
