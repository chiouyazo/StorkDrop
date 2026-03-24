using System.IO;
using System.Windows;
using Microsoft.Win32;
using StorkDrop.App.Localization;
using StorkDrop.Core.Models;
using StorkDrop.Installer;

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
        ProductManifest? manifest = null
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
    }

    private void UpdateAdminHint()
    {
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
        // Download size
        if (_manifest?.DownloadSizeBytes is not null and > 0)
        {
            DownloadSizeText.Text = FormatBytes(_manifest.DownloadSizeBytes.Value);
        }
        else
        {
            DownloadSizeText.Text = " - ";
        }

        // Shortcut count
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
            string path = PathBox.Text;
            if (string.IsNullOrWhiteSpace(path))
            {
                DiskSpaceText.Text = " - ";
                return;
            }

            string? root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
            {
                DiskSpaceText.Text = " - ";
                return;
            }

            DriveInfo driveInfo = new DriveInfo(root);
            if (driveInfo.IsReady)
            {
                DiskSpaceText.Text = FormatBytes(driveInfo.AvailableFreeSpace);
            }
            else
            {
                DiskSpaceText.Text = " - ";
            }
        }
        catch
        {
            DiskSpaceText.Text = " - ";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = new string[] { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int suffixIndex = 0;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:0.##} {suffixes[suffixIndex]}";
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
