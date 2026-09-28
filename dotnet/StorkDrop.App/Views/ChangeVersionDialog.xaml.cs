using System.Windows;
using System.Windows.Controls;
using StorkDrop.App.Localization;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.Views;

/// <summary>
/// Combined channel + version picker: the left rail lists every channel that serves the product (with its
/// badge colour); the right pane hosts the shared <see cref="Controls.VersionPicker"/>, which shows the
/// selected channel's versions as a grouped cascade or a flat list. Confirming returns the chosen channel
/// feed id and full version string, so a same-channel version change and a cross-channel switch go through
/// one flow.
/// </summary>
public partial class ChangeVersionDialog : Window
{
    private readonly string _currentFeedId;
    private readonly string _currentVersion;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _fetchVersions;
    private CancellationTokenSource? _loadCts;
    private string _activeFeedId = string.Empty;

    /// <summary>The channel feed id the user chose to apply, or null if cancelled.</summary>
    public string? SelectedFeedId { get; private set; }

    /// <summary>The version the user chose to apply, or null if cancelled.</summary>
    public string? SelectedVersion { get; private set; }

    public ChangeVersionDialog(
        string title,
        string currentFeedId,
        string currentVersion,
        IReadOnlyList<ChannelRow> channels,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> fetchVersions
    )
    {
        InitializeComponent();

        _currentFeedId = currentFeedId;
        _currentVersion = currentVersion;
        _fetchVersions = fetchVersions;

        MessageText.Text = LocalizationManager
            .GetString("ChangeVersion_Message")
            .Replace("{0}", title);

        List<ChannelItem> items = channels
            .Select(c => new ChannelItem(
                c.FeedId,
                c.FeedName,
                c.BadgeColor,
                c.LatestVersion,
                string.Equals(c.FeedId, currentFeedId, StringComparison.OrdinalIgnoreCase),
                c.Schema
            ))
            .OrderByDescending(c => c.IsCurrent)
            .ToList();

        ChannelList.ItemsSource = items;
        ChannelList.SelectedItem = items.FirstOrDefault(c => c.IsCurrent) ?? items.FirstOrDefault();
    }

    private async void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChannelList.SelectedItem is ChannelItem channel)
            await LoadVersionsAsync(channel);
    }

    private async Task LoadVersionsAsync(ChannelItem channel)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _loadCts.Token;
        _activeFeedId = channel.FeedId;

        VersionPickerControl.Visibility = Visibility.Collapsed;
        LoadingText.Visibility = Visibility.Visible;
        UpdateApplyState();

        IReadOnlyList<string> versions;
        try
        {
            versions = await _fetchVersions(channel.FeedId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            versions = [];
        }

        if (cancellationToken.IsCancellationRequested)
            return;

        LoadingText.Visibility = Visibility.Collapsed;
        VersionPickerControl.Visibility = Visibility.Visible;

        VersionPickerControl.Schema = channel.Schema;
        VersionPickerControl.CurrentVersion = channel.IsCurrent ? _currentVersion : null;
        VersionPickerControl.ItemsSource = versions;
        UpdateApplyState();
    }

    private void VersionPicker_SelectedVersionChanged(object? sender, EventArgs e) =>
        UpdateApplyState();

    private void UpdateApplyState()
    {
        string? version = VersionPickerControl.SelectedVersion;
        ApplyButton.IsEnabled =
            ChannelList.SelectedItem is ChannelItem
            && !string.IsNullOrEmpty(version)
            && !(
                string.Equals(_activeFeedId, _currentFeedId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(version, _currentVersion, StringComparison.OrdinalIgnoreCase)
            );
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelList.SelectedItem is not ChannelItem channel)
            return;

        string? version = VersionPickerControl.SelectedVersion;
        if (string.IsNullOrEmpty(version))
            return;

        SelectedFeedId = channel.FeedId;
        SelectedVersion = version;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>A channel offered in the left rail. Latest version is shown as a hint under the name.</summary>
    public sealed record ChannelRow(
        string FeedId,
        string FeedName,
        string? BadgeColor,
        string LatestVersion,
        VersionSchema? Schema
    );

    private sealed record ChannelItem(
        string FeedId,
        string FeedName,
        string? BadgeColor,
        string LatestVersion,
        bool IsCurrent,
        VersionSchema? Schema
    );
}
