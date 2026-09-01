using System.ComponentModel;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.Views;

/// <summary>Checkbox row for <see cref="DependentUpdatesDialog"/>; pre-selected by default.</summary>
public sealed class DependentUpdateItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public DependentUpdateItem(DependentUpdate update)
    {
        Update = update;
        Title = update.Title;
        VersionChange = $"v{update.CurrentVersion} → v{update.TargetVersion}";
    }

    public DependentUpdate Update { get; }
    public string Title { get; }
    public string VersionChange { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
