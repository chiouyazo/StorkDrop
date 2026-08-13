using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;

namespace StorkDrop.App.Views;

public partial class InstallLogWindow : Window
{
    private readonly TrackedInstallation _installation;

    public InstallLogWindow(TrackedInstallation installation)
    {
        InitializeComponent();
        DataContext = installation;
        _installation = installation;

        // Populate with existing log entries
        LogTextBox.Text = string.Join(Environment.NewLine, installation.LogEntries);
        LogTextBox.Focus();

        if (installation.LogEntries is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += (_, args) =>
            {
                if (args.NewItems is null)
                    return;
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    () =>
                    {
                        foreach (string entry in args.NewItems)
                        {
                            if (LogTextBox.Text.Length > 0)
                                LogTextBox.AppendText(Environment.NewLine);
                            LogTextBox.AppendText(entry);
                        }

                        LogTextBox.ScrollToEnd();
                    }
                );
            };
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_installation.IsCompleted)
            return;

        MessageBoxResult result = MessageBox.Show(
            LocalizationManager.GetString("Cancel_Install_Confirm_Message"),
            LocalizationManager.GetString("Cancel_Install_Confirm_Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );
        if (result != MessageBoxResult.Yes)
            return;

        _installation.Cancel();
    }
}
