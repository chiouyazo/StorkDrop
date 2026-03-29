using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StorkDrop.App.Localization;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model for the activity log view, displaying installation and plugin activity history.
/// </summary>
public partial class ActivityLogViewModel : ObservableObject
{
    private readonly IActivityLog _activityLog;
    private readonly ILogger<ActivityLogViewModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityLogViewModel"/> class.
    /// </summary>
    /// <param name="activityLog">The activity log service.</param>
    /// <param name="logger">The logger instance.</param>
    public ActivityLogViewModel(IActivityLog activityLog, ILogger<ActivityLogViewModel> logger)
    {
        _activityLog = activityLog;
        _logger = logger;
    }

    [ObservableProperty]
    private ObservableCollection<ActivityLogEntry> _entries =
        new ObservableCollection<ActivityLogEntry>();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Loads the activity log entries.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            IReadOnlyList<ActivityLogEntry> logEntries = await _activityLog.GetEntriesAsync(500);
            Entries = new ObservableCollection<ActivityLogEntry>(logEntries);
        }
        catch (Exception ex)
        {
            ErrorMessage = LocalizationManager.GetString("Error_LoadLogFailed") + ": " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Clears all activity log entries.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task ClearLogAsync()
    {
        await _activityLog.ClearAsync();
        Entries.Clear();
    }

    /// <summary>
    /// Refreshes the activity log.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    /// <summary>
    /// Opens the log directory in the system file explorer.
    /// </summary>
    [RelayCommand]
    private void OpenLogDirectory()
    {
        string logDir = StorkPaths.LogDir;
        if (Directory.Exists(logDir))
        {
            Process.Start(new ProcessStartInfo(logDir) { UseShellExecute = true });
        }
    }
}
