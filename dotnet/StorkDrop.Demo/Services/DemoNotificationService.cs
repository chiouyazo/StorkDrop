using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Demo.Services;

internal sealed class DemoNotificationService : INotificationService
{
    public void ShowInfo(string title, string message) { }

    public void ShowSuccess(string title, string message) { }

    public void ShowWarning(string title, string message) { }

    public void ShowError(string title, string message) { }

    public void ShowUpdateAvailable(string productTitle, string version) { }
}
