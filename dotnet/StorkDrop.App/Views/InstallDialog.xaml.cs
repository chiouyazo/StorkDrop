using System.Windows;
using Microsoft.Win32;
using StorkDrop.App.Localization;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.Views;

public partial class InstallDialog : Window
{
    public string SelectedPath => PathBox.Text;
    public bool Confirmed { get; private set; }

    private readonly ProductManifest? _manifest;

    public InstallDialog(
        string productTitle,
        string version,
        string defaultPath,
        ProductManifest? manifest = null,
        bool hasFileTypeHandlers = false
    )
    {
        InitializeComponent();
        _manifest = manifest;
        TitleText.Text = productTitle;
        VersionText.Text = $"Version {version}";
        PathBox.Text = defaultPath;
        PathBox.TextChanged += (_, _) =>
        {
            UpdateAdminHint();
            UpdateDiskSpace();
        };
        UpdateAdminHint();
        UpdateInstallDetails();
        UpdateDiskSpace();

        if (manifest?.Plugins is { Length: > 0 } || hasFileTypeHandlers)
            PluginHintBorder.Visibility = Visibility.Visible;
    }

    private void UpdateAdminHint()
    {
        // Skip admin check if path contains unresolved templates like {ACMEPath}
        if (PathBox.Text.Contains('{'))
        {
            AdminHint.Visibility = Visibility.Collapsed;
            InstallButton.Content = LocalizationManager.GetString("Install_Button");
            return;
        }

        bool needsAdmin = ElevationHelper.PathRequiresAdmin(PathBox.Text);
        bool isAdmin = ElevationHelper.IsRunningAsAdmin();

        if (needsAdmin && !isAdmin)
        {
            AdminHint.Text = LocalizationManager.GetString("Install_AdminHint");
            AdminHint.Visibility = Visibility.Visible;
            InstallButton.Content = LocalizationManager.GetString("Install_AsAdmin");
        }
        else
        {
            AdminHint.Visibility = Visibility.Collapsed;
            InstallButton.Content = LocalizationManager.GetString("Install_Button");
        }
    }

    private void UpdateInstallDetails()
    {
        if (_manifest?.DownloadSizeBytes is not null and > 0)
        {
            DownloadSizeText.Text = FormatHelper.FormatBytes(_manifest.DownloadSizeBytes.Value);
        }
        else
        {
            DownloadSizeText.Text = " - ";
        }

        int shortcutCount = _manifest?.Shortcuts?.Length ?? 0;
        if (shortcutCount > 0)
        {
            ShortcutCountText.Text = LocalizationManager
                .GetString("InstallDialog_Shortcuts")
                .Replace("{0}", shortcutCount.ToString());
        }
        else
        {
            ShortcutCountText.Text = LocalizationManager.GetString("InstallDialog_NoShortcuts");
        }
    }

    private void UpdateDiskSpace()
    {
        try
        {
            DiskSpaceText.Text = FormatHelper.GetFormattedDiskSpace(PathBox.Text) ?? " - ";
        }
        catch
        {
            DiskSpaceText.Text = " - ";
        }
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new OpenFolderDialog
        {
            Title = LocalizationManager.GetString("Install_Directory"),
            InitialDirectory = PathBox.Text,
        };

        if (dialog.ShowDialog() == true)
        {
            PathBox.Text = dialog.FolderName;
        }
    }

    private void OnInstallClick(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
