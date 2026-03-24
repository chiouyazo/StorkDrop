using System.Windows.Controls;

namespace StorkDrop.App.Services;

public sealed class NavigationService
{
    private ContentControl? _contentRegion;

    public event EventHandler<string>? NavigationChanged;

    public string CurrentView { get; private set; } = "Marketplace";

    public void RegisterContentRegion(ContentControl contentControl)
    {
        _contentRegion = contentControl;
    }

    public void NavigateTo(string viewName, object? content = null)
    {
        CurrentView = viewName;

        if (_contentRegion is not null && content is not null)
        {
            _contentRegion.Content = content;
        }

        NavigationChanged?.Invoke(this, viewName);
    }
}
