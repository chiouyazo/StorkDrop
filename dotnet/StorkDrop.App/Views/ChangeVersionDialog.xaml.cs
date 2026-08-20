using System.Windows;
using System.Windows.Controls;
using StorkDrop.App.Localization;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.Views;

public partial class ChangeVersionDialog : Window
{
    /// <summary>The version the user chose to apply, or null if cancelled.</summary>
    public string? SelectedVersion { get; private set; }

    public ChangeVersionDialog(string title, string currentVersion, IReadOnlyList<string> versions)
    {
        InitializeComponent();

        MessageText.Text = LocalizationManager
            .GetString("ChangeVersion_Message")
            .Replace("{0}", title);

        List<VersionRow> rows = versions
            .OrderByDescending(v => v, VersionComparer.Instance)
            .Select(v => new VersionRow(
                v,
                string.Equals(v, currentVersion, StringComparison.OrdinalIgnoreCase)
            ))
            .ToList();

        VersionList.ItemsSource = rows;
        VersionList.SelectedItem = rows.FirstOrDefault(r => r.IsCurrent);
        UpdateApplyState();
    }

    private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateApplyState();

    private void UpdateApplyState()
    {
        ApplyButton.IsEnabled = VersionList.SelectedItem is VersionRow { IsCurrent: false };
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (VersionList.SelectedItem is VersionRow { IsCurrent: false } row)
        {
            SelectedVersion = row.Version;
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public sealed record VersionRow(string Version, bool IsCurrent);
}
