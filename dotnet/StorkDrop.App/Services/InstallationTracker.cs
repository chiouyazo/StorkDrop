using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StorkDrop.App.Services;

/// <summary>
/// Global singleton that tracks all active and completed installations.
/// The status bar and popup panel bind to this.
/// </summary>
public sealed partial class InstallationTracker : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<TrackedInstallation> _installations = [];

    public bool HasActiveInstallations => Installations.Any(i => !i.IsCompleted);

    public int ActiveCount => Installations.Count(i => !i.IsCompleted);

    [ObservableProperty]
    private bool _showPanel;

    public TrackedInstallation StartInstallation(string productId, string title)
    {
        ShowPanel = true;
        TrackedInstallation install = new TrackedInstallation
        {
            ProductId = productId,
            Title = title,
            StartedAt = DateTime.Now,
        };
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Installations.Insert(0, install);
            OnPropertyChanged(nameof(HasActiveInstallations));
            OnPropertyChanged(nameof(ActiveCount));
        });
        return install;
    }

    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(HasActiveInstallations));
        OnPropertyChanged(nameof(ActiveCount));
    }
}

public sealed partial class TrackedInstallation : ObservableObject
{
    [ObservableProperty]
    private string _productId = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private int _percentage;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private DateTime _startedAt;

    [ObservableProperty]
    private ObservableCollection<string> _logEntries = [];

    public CancellationTokenSource Cts { get; } = new CancellationTokenSource();

    public void Cancel()
    {
        Cts.Cancel();
        Complete(false, "Cancelled by user");
    }

    public void AddLog(string message)
    {
        string entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => LogEntries.Add(entry));
    }

    public void Complete(bool success, string? error = null)
    {
        IsCompleted = true;
        IsSuccess = success;
        ErrorMessage = error ?? string.Empty;
        StatusMessage = success ? "Completed" : $"Failed: {error}";
    }
}
