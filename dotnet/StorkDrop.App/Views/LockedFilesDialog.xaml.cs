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

        foreach (LockedProcessViewModel item in _items)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LockedProcessViewModel.IsSelected))
                    UpdateKillButtonState();
            };
        }

        CollectionViewSource viewSource = new CollectionViewSource { Source = _items };
        viewSource.GroupDescriptions.Add(
            new PropertyGroupDescription(nameof(LockedProcessViewModel.FileName))
        );
        ProcessList.ItemsSource = viewSource.View;
        UpdateKillButtonState();
    }

    private void UpdateKillButtonState()
    {
        KillButton.IsEnabled = _items.Any(i => i.IsSelected);
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

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Refresh();
    }

    private void KillSelected_Click(object sender, RoutedEventArgs e)
    {
        List<LockedProcessViewModel> selected = _items.Where(i => i.IsSelected).ToList();

        foreach (LockedProcessViewModel item in selected)
        {
            bool killed = _detector.TryKillProcess(item.ProcessId);
            if (!killed)
            {
                item.ErrorMessage = LocalizationManager
                    .GetString("LockedFiles_KillFailed")
                    .Replace("{0}", item.ProcessName);
                item.HasError = true;
            }
        }

        Refresh();
    }

    private void Refresh()
    {
        IReadOnlyList<LockedFileInfo> lockedFiles = _detector.GetLockedFiles(_directory);
        if (lockedFiles.Count == 0)
        {
            DialogResult = true;
            Close();
            return;
        }

        BuildItemList(lockedFiles);
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
