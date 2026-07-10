using CommunityToolkit.Mvvm.ComponentModel;

namespace StorkDrop.App.ViewModels;

/// <summary>One problem file shown in the repair dialog, with a checkbox for whether to repair it.</summary>
public partial class IntegrityRepairItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    public string Path { get; init; } = string.Empty;

    public string StatusText { get; init; } = string.Empty;
}
