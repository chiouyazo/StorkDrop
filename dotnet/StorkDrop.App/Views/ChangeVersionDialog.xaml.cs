using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using StorkDrop.App.Localization;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.Views;

/// <summary>
/// Combined channel + version picker: the left rail lists every channel that serves the product (with its
/// badge colour), the right pane lists the versions of the selected channel. When a channel's versions
/// form a regular compound structure they are shown as a dependent breadcrumb cascade (grouped by default,
/// with a toggle back to the flat list); otherwise the flat list is used. Confirming returns the chosen
/// channel feed id and full version string, so a same-channel version change and a cross-channel switch go
/// through one flow.
/// </summary>
public partial class ChangeVersionDialog : Window
{
    private readonly string _currentFeedId;
    private readonly string _currentVersion;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _fetchVersions;
    private CancellationTokenSource? _loadCts;

    private string _activeFeedId = string.Empty;
    private VersionSchema? _activeSchema;
    private VersionTree? _tree;
    private bool _grouped;
    private readonly List<VersionNode> _path = [];
    private bool _suppressSelection;

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
        {
            _activeFeedId = channel.FeedId;
            _activeSchema = channel.Schema;
            await LoadVersionsAsync(channel.FeedId);
        }
    }

    private async Task LoadVersionsAsync(string feedId)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _loadCts.Token;

        _suppressSelection = true;
        VersionList.ItemsSource = null;
        GroupedList.ItemsSource = null;
        _suppressSelection = false;
        BreadcrumbPanel.Children.Clear();
        FlatScroller.Visibility = Visibility.Collapsed;
        GroupedScroller.Visibility = Visibility.Collapsed;
        BreadcrumbPanel.Visibility = Visibility.Collapsed;
        ViewToggleButton.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Collapsed;
        LoadingText.Visibility = Visibility.Visible;
        UpdateApplyState();

        IReadOnlyList<string> versions;
        try
        {
            versions = await _fetchVersions(feedId, cancellationToken);
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

        List<VersionRow> flat = versions
            .OrderByDescending(v => v, VersionComparer.Instance)
            .Select((v, index) => new VersionRow(v, index == 0, IsCurrentVersion(feedId, v)))
            .ToList();

        _suppressSelection = true;
        VersionList.ItemsSource = flat;
        VersionList.SelectedItem = flat.FirstOrDefault(r => r.IsCurrent) ?? flat.FirstOrDefault();
        _suppressSelection = false;

        _tree = VersionGrouping.Build(versions, _activeSchema);
        _grouped = _tree is not null;

        LoadingText.Visibility = Visibility.Collapsed;
        if (flat.Count == 0)
        {
            EmptyText.Visibility = Visibility.Visible;
            UpdateApplyState();
            return;
        }

        RenderCurrentChannel();
    }

    private void RenderCurrentChannel()
    {
        bool canGroup = _tree is not null;
        ViewToggleButton.Visibility = canGroup ? Visibility.Visible : Visibility.Collapsed;
        ViewToggleButton.Content = LocalizationManager.GetString(
            _grouped ? "ChangeVersion_ViewList" : "ChangeVersion_ViewGrouped"
        );

        if (_grouped && _tree is not null)
        {
            FlatScroller.Visibility = Visibility.Collapsed;
            GroupedScroller.Visibility = Visibility.Visible;
            BreadcrumbPanel.Visibility = Visibility.Visible;
            _path.Clear();
            ShowLevel(_tree.Roots, autoAdvance: true);
        }
        else
        {
            BreadcrumbPanel.Visibility = Visibility.Collapsed;
            GroupedScroller.Visibility = Visibility.Collapsed;
            FlatScroller.Visibility = Visibility.Visible;
            PlayFadeIn();
            UpdateApplyState();
        }
    }

    private void ShowLevel(IReadOnlyList<VersionNode> nodes, bool autoAdvance)
    {
        if (autoAdvance)
            nodes = DescendAutoSingle(nodes);

        bool feedIsCurrent = string.Equals(
            _activeFeedId,
            _currentFeedId,
            StringComparison.OrdinalIgnoreCase
        );
        List<GroupedItem> items = nodes
            .Select(n => new GroupedItem(
                n,
                n.Display,
                n.FullVersion is null,
                n.FullVersion is not null
                    && feedIsCurrent
                    && string.Equals(
                        n.FullVersion,
                        _currentVersion,
                        StringComparison.OrdinalIgnoreCase
                    )
            ))
            .ToList();

        _suppressSelection = true;
        GroupedList.ItemsSource = items;
        GroupedList.SelectedItem = items.FirstOrDefault(i => i.IsCurrent);
        _suppressSelection = false;

        BuildBreadcrumb();
        PlayFadeIn();
        UpdateApplyState();
    }

    private IReadOnlyList<VersionNode> DescendAutoSingle(IReadOnlyList<VersionNode> nodes)
    {
        while (nodes.Count == 1 && nodes[0].FullVersion is null)
        {
            _path.Add(nodes[0]);
            nodes = nodes[0].Children;
        }
        return nodes;
    }

    private void BuildBreadcrumb()
    {
        BreadcrumbPanel.Children.Clear();
        Style linkStyle = (Style)FindResource("LinkButton");

        for (int i = 0; i < _path.Count; i++)
        {
            int index = i;
            Button crumb = new Button { Content = _path[i].Display, Style = linkStyle };
            crumb.Click += (_, _) => NavigateTo(index);
            BreadcrumbPanel.Children.Add(crumb);
            BreadcrumbPanel.Children.Add(NewSeparator());
        }

        if (
            _tree?.Levels is { } levels
            && _path.Count < levels.Count
            && !string.IsNullOrEmpty(levels[_path.Count])
        )
        {
            BreadcrumbPanel.Children.Add(
                new TextBlock
                {
                    Text = levels[_path.Count],
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (System.Windows.Media.Brush)FindResource("OnSurfaceVariantBrush"),
                }
            );
        }
    }

    private TextBlock NewSeparator() =>
        new TextBlock
        {
            Text = " › ",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (System.Windows.Media.Brush)FindResource("OnSurfaceVariantBrush"),
        };

    private void NavigateTo(int depth)
    {
        while (_path.Count > depth)
            _path.RemoveAt(_path.Count - 1);

        IReadOnlyList<VersionNode> nodes = depth == 0 ? _tree!.Roots : _path[depth - 1].Children;
        ShowLevel(nodes, autoAdvance: false);
    }

    private void GroupedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || GroupedList.SelectedItem is not GroupedItem item)
            return;

        if (item.HasChildren)
        {
            _path.Add(item.Node);
            ShowLevel(item.Node.Children, autoAdvance: true);
        }
        else
        {
            UpdateApplyState();
        }
    }

    private void ViewToggle_Click(object sender, RoutedEventArgs e)
    {
        _grouped = !_grouped;
        RenderCurrentChannel();
    }

    private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelection)
            UpdateApplyState();
    }

    private void PlayFadeIn()
    {
        DoubleAnimation fade = new DoubleAnimation(
            0.0,
            1.0,
            new Duration(TimeSpan.FromMilliseconds(160))
        )
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        VersionPane.BeginAnimation(OpacityProperty, fade);
    }

    private bool IsCurrentVersion(string feedId, string version) =>
        string.Equals(feedId, _currentFeedId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(version, _currentVersion, StringComparison.OrdinalIgnoreCase);

    private void UpdateApplyState()
    {
        bool ok = ChannelList.SelectedItem is ChannelItem;
        if (ok)
        {
            if (_grouped && _tree is not null)
                ok =
                    GroupedList.SelectedItem is GroupedItem { HasChildren: false } grouped
                    && grouped.Node.FullVersion is { } full
                    && !IsCurrentVersion(_activeFeedId, full);
            else
                ok =
                    VersionList.SelectedItem is VersionRow version
                    && !IsCurrentVersion(_activeFeedId, version.Version);
        }
        ApplyButton.IsEnabled = ok;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelList.SelectedItem is not ChannelItem channel)
            return;

        string? version =
            _grouped && _tree is not null
                ? (GroupedList.SelectedItem as GroupedItem)?.Node.FullVersion
                : (VersionList.SelectedItem as VersionRow)?.Version;

        if (version is null)
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

    private sealed record VersionRow(string Version, bool IsNewest, bool IsCurrent);

    private sealed record GroupedItem(
        VersionNode Node,
        string Display,
        bool HasChildren,
        bool IsCurrent
    );
}
