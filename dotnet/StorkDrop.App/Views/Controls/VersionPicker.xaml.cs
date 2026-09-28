using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StorkDrop.App.Localization;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.Views.Controls;

/// <summary>
/// Reusable version selector: renders a channel's versions either as a flat list or, when they form a
/// regular compound structure, as a dependent breadcrumb cascade (grouped by default, with a toggle back to
/// the flat list). Backward compatible - not groupable versions simply show the flat list. Used both inline
/// in the marketplace and inside the combined channel/version dialog, so the grouping logic lives in one
/// place.
/// </summary>
public partial class VersionPicker : UserControl
{
    private VersionTree? _tree;
    private bool _grouped;
    private readonly List<VersionNode> _path = [];
    private bool _suppress;

    public VersionPicker()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IReadOnlyList<string>),
        typeof(VersionPicker),
        new PropertyMetadata(null, OnSourceChanged)
    );

    public IReadOnlyList<string>? ItemsSource
    {
        get => (IReadOnlyList<string>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SchemaProperty = DependencyProperty.Register(
        nameof(Schema),
        typeof(VersionSchema),
        typeof(VersionPicker),
        new PropertyMetadata(null, OnSourceChanged)
    );

    public VersionSchema? Schema
    {
        get => (VersionSchema?)GetValue(SchemaProperty);
        set => SetValue(SchemaProperty, value);
    }

    public static readonly DependencyProperty CurrentVersionProperty = DependencyProperty.Register(
        nameof(CurrentVersion),
        typeof(string),
        typeof(VersionPicker),
        new PropertyMetadata(null, OnSourceChanged)
    );

    public string? CurrentVersion
    {
        get => (string?)GetValue(CurrentVersionProperty);
        set => SetValue(CurrentVersionProperty, value);
    }

    public static readonly DependencyProperty SelectedVersionProperty = DependencyProperty.Register(
        nameof(SelectedVersion),
        typeof(string),
        typeof(VersionPicker),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            static (d, _) =>
                ((VersionPicker)d).SelectedVersionChanged?.Invoke(d, System.EventArgs.Empty)
        )
    );

    public string? SelectedVersion
    {
        get => (string?)GetValue(SelectedVersionProperty);
        set => SetValue(SelectedVersionProperty, value);
    }

    /// <summary>Raised whenever <see cref="SelectedVersion"/> changes (user drill/selection or a rebuild).</summary>
    public event System.EventHandler? SelectedVersionChanged;

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((VersionPicker)d).Rebuild();

    private void Rebuild()
    {
        IReadOnlyList<string> versions = ItemsSource ?? [];

        List<VersionRow> flat = versions
            .OrderByDescending(v => v, VersionComparer.Instance)
            .Select((v, index) => new VersionRow(v, index == 0, IsCurrent(v)))
            .ToList();

        _suppress = true;
        FlatList.ItemsSource = flat;
        FlatList.SelectedItem = flat.FirstOrDefault();
        _suppress = false;

        _tree = VersionGrouping.Build(versions, Schema);
        _grouped = _tree is not null;

        if (flat.Count == 0)
        {
            EmptyText.Visibility = Visibility.Visible;
            FlatScroller.Visibility = Visibility.Collapsed;
            GroupedScroller.Visibility = Visibility.Collapsed;
            BreadcrumbPanel.Visibility = Visibility.Collapsed;
            ViewToggleButton.Visibility = Visibility.Collapsed;
            SetCurrentValue(SelectedVersionProperty, null);
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        RenderMode();

        // Default the outward selection to the newest version so the host (install button, release notes)
        // has a value; in grouped mode the cascade still starts at the root for the user to drill in.
        SetCurrentValue(SelectedVersionProperty, flat[0].Version);
    }

    private void RenderMode()
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
        }
    }

    private void ShowLevel(IReadOnlyList<VersionNode> nodes, bool autoAdvance)
    {
        if (autoAdvance)
            nodes = DescendAutoSingle(nodes);

        List<GroupedItem> items = nodes
            .Select(n => new GroupedItem(
                n,
                n.Display,
                n.FullVersion is null,
                n.FullVersion is not null && IsCurrent(n.FullVersion)
            ))
            .ToList();

        _suppress = true;
        GroupedList.ItemsSource = items;
        GroupedList.SelectedItem = null;
        _suppress = false;

        BuildBreadcrumb();
        PlayFadeIn();
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
        Style linkStyle = (Style)FindResource("VP_LinkButton");

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
                    Foreground = (Brush)FindResource("OnSurfaceVariantBrush"),
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
            Foreground = (Brush)FindResource("OnSurfaceVariantBrush"),
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
        if (_suppress || GroupedList.SelectedItem is not GroupedItem item)
            return;

        if (item.HasChildren)
        {
            _path.Add(item.Node);
            ShowLevel(item.Node.Children, autoAdvance: true);
        }
        else
        {
            SetCurrentValue(SelectedVersionProperty, item.Node.FullVersion);
        }
    }

    private void FlatList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress)
            return;
        if (FlatList.SelectedItem is VersionRow row)
            SetCurrentValue(SelectedVersionProperty, row.Version);
    }

    private void ViewToggle_Click(object sender, RoutedEventArgs e)
    {
        _grouped = !_grouped;
        RenderMode();
        if (!_grouped)
            SetCurrentValue(
                SelectedVersionProperty,
                (FlatList.SelectedItem as VersionRow)?.Version
            );
    }

    private void PlayFadeIn()
    {
        DoubleAnimation fade = new DoubleAnimation(
            0.0,
            1.0,
            new Duration(System.TimeSpan.FromMilliseconds(160))
        )
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Root.BeginAnimation(OpacityProperty, fade);
    }

    private bool IsCurrent(string version) =>
        CurrentVersion is { Length: > 0 }
        && string.Equals(version, CurrentVersion, System.StringComparison.OrdinalIgnoreCase);

    private sealed record VersionRow(string Version, bool IsNewest, bool IsCurrent);

    private sealed record GroupedItem(
        VersionNode Node,
        string Display,
        bool HasChildren,
        bool IsCurrent
    );
}
