using System.ComponentModel;
using StorkDrop.App.Services;

namespace StorkDrop.App.Views;

public sealed class PostProductItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public PostProductItem(ResolvedPostProduct resolved, bool isInstalled)
    {
        Resolved = resolved;
        IsInstalled = isInstalled;
        Title = $"{resolved.Manifest.Title} v{resolved.Manifest.Version}";
        Description = resolved.Manifest.Description ?? string.Empty;
        _isSelected = !isInstalled;
    }

    public ResolvedPostProduct Resolved { get; }
    public bool IsInstalled { get; }
    public string Title { get; }
    public string Description { get; }
    public bool HasDescription => !string.IsNullOrEmpty(Description);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (IsInstalled)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
