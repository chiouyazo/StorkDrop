using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using StorkDrop.App.Localization;
using StorkDrop.App.ViewModels;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.Views;

public partial class LockedFilesDialog : Window
{
    private readonly IFileLockDetector _detector;
    private readonly string _directory;
    private List<LockedProcessViewModel> _items = [];
    private CancellationTokenSource? _killCts;
    private bool _decided;

    /// <summary>How the operation should proceed. Read by the caller after the dialog closes.</summary>
    public LockedFilesAction Action { get; private set; } = LockedFilesAction.Skip;

    public LockedFilesDialog(
        IReadOnlyList<LockedFileInfo> lockedFiles,
        IFileLockDetector detector,
        string directory
    )
    {
        InitializeComponent();
        _detector = detector;
        _directory = directory;
        BuildItemList(lockedFiles);
    }

    private void BuildItemList(IReadOnlyList<LockedFileInfo> lockedFiles)
    {
        _items = [];

        foreach (LockedFileInfo fileInfo in lockedFiles)
        {
            foreach (LockingProcessInfo proc in fileInfo.Processes)
            {
                _items.Add(
                    new LockedProcessViewModel
                    {
                        ProcessName = proc.ProcessName,
                        ProcessId = proc.ProcessId,
                        UserName = string.IsNullOrWhiteSpace(proc.UserName) ? "-" : proc.UserName,
                        StartTimeDisplay = FormatStartTime(proc.StartTime),
                        FileName = fileInfo.FileName,
                    }
                );
            }
        }

        CollectionViewSource viewSource = new CollectionViewSource { Source = _items };
        viewSource.GroupDescriptions.Add(
            new PropertyGroupDescription(nameof(LockedProcessViewModel.FileName))
        );
        ProcessList.ItemsSource = viewSource.View;
    }

    private static string FormatStartTime(DateTime? startTime)
    {
        if (startTime is null)
            return "-";

        TimeSpan elapsed = DateTime.Now - startTime.Value;

        if (elapsed.TotalMinutes < 1)
            return "< 1 min";
        if (elapsed.TotalHours < 1)
            return $"{(int)elapsed.TotalMinutes} min";
        if (elapsed.TotalDays < 1)
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";

        return $"{(int)elapsed.TotalDays}d {elapsed.Hours}h";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        IReadOnlyList<LockedFileInfo> lockedFiles = _detector.GetLockedFiles(_directory);
        if (lockedFiles.Count == 0)
        {
            Action = LockedFilesAction.Retry;
            DialogResult = true;
            Close();
            return;
        }

        BuildItemList(lockedFiles);
    }

    private async void KillAll_Click(object sender, RoutedEventArgs e)
    {
        _killCts = new CancellationTokenSource();
        CancellationToken ct = _killCts.Token;
        SetBusy(true);

        int[] pids = _items.Select(i => i.ProcessId).Distinct().ToArray();
        string directory = _directory;

        int remaining = await Task.Run(() =>
        {
            foreach (int pid in pids)
            {
                if (ct.IsCancellationRequested)
                    return -1;
                _detector.TryKillProcess(pid);
            }

            // Wait for the killed processes to actually exit (cheap PID check) rather than rescanning
            // the whole directory every tick. Once a process is gone its mapped DLLs are released.
            for (int attempt = 0; attempt < 50 && pids.Any(IsProcessAlive); attempt++)
            {
                if (ct.IsCancellationRequested)
                    return -1;
                Thread.Sleep(200);
            }

            // Confirm the files are free (handle release can lag a moment behind process exit).
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (ct.IsCancellationRequested)
                    return -1;
                if (_detector.GetLockedFiles(directory).Count == 0)
                    return 0;
                Thread.Sleep(250);
            }

            return _detector.GetLockedFiles(directory).Count;
        });

        // The user chose "rename and continue" while the kill was running; that path already closed
        // the dialog, so don't touch the UI.
        if (_decided)
            return;

        if (remaining == 0)
        {
            Action = LockedFilesAction.Retry;
            DialogResult = true;
            Close();
            return;
        }

        SetBusy(false);
        StatusText.SetResourceReference(ForegroundProperty, "ErrorBrush");
        StatusText.Text = LocalizationManager
            .GetString("LockedFiles_StillLocked")
            .Replace("{0}", remaining.ToString());
        StatusText.Visibility = Visibility.Visible;
        BuildItemList(_detector.GetLockedFiles(directory));
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private void RenameContinue_Click(object sender, RoutedEventArgs e)
    {
        _decided = true;
        _killCts?.Cancel();
        Action = LockedFilesAction.RenameAndContinue;
        DialogResult = true;
        Close();
    }

    private void SetBusy(bool busy)
    {
        KillAllButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        // "Rename and continue" stays enabled during the kill so it can always be used as an escape.
        RenameContinueButton.IsEnabled = true;

        if (busy)
        {
            StatusText.ClearValue(ForegroundProperty);
            StatusText.Text = LocalizationManager.GetString("LockedFiles_Killing");
            StatusText.Visibility = Visibility.Visible;
        }
    }
}
