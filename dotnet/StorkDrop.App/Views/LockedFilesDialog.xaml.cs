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
        SetBusy(true);

        int[] pids = _items.Select(i => i.ProcessId).Distinct().ToArray();
        string directory = _directory;

        // TryKillProcess already waits for each process to exit; poll the directory afterwards so we
        // only continue once every handle is actually released, as requested.
        int remaining = await Task.Run(() =>
        {
            foreach (int pid in pids)
                _detector.TryKillProcess(pid);

            for (int attempt = 0; attempt < 20; attempt++)
            {
                int locked = _detector.GetLockedFiles(directory).Count;
                if (locked == 0)
                    return 0;
                Thread.Sleep(250);
            }

            return _detector.GetLockedFiles(directory).Count;
        });

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

    private void RenameContinue_Click(object sender, RoutedEventArgs e)
    {
        Action = LockedFilesAction.RenameAndContinue;
        DialogResult = true;
        Close();
    }

    private void SetBusy(bool busy)
    {
        KillAllButton.IsEnabled = !busy;
        RenameContinueButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;

        if (busy)
        {
            StatusText.ClearValue(ForegroundProperty);
            StatusText.Text = LocalizationManager.GetString("LockedFiles_Killing");
            StatusText.Visibility = Visibility.Visible;
        }
    }
}
