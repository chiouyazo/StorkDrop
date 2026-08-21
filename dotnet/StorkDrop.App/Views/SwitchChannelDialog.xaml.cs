using System.Windows;
using System.Windows.Controls;
using StorkDrop.App.Localization;

namespace StorkDrop.App.Views;

public partial class SwitchChannelDialog : Window
{
    /// <summary>The feed id of the channel the user chose, or null if cancelled.</summary>
    public string? SelectedFeedId { get; private set; }

    public SwitchChannelDialog(string title, IReadOnlyList<ChannelRow> channels)
    {
        InitializeComponent();

        MessageText.Text = LocalizationManager
            .GetString("SwitchChannel_Message")
            .Replace("{0}", title);

        ChannelList.ItemsSource = channels;
        ChannelList.SelectedItem = channels.FirstOrDefault(c => c.IsCurrent);
        UpdateApplyState();
    }

    private void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateApplyState();

    private void UpdateApplyState()
    {
        ApplyButton.IsEnabled = ChannelList.SelectedItem is ChannelRow { IsCurrent: false };
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelList.SelectedItem is ChannelRow { IsCurrent: false } row)
        {
            SelectedFeedId = row.FeedId;
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public sealed record ChannelRow(string FeedId, string FeedName, string Version, bool IsCurrent);
}
