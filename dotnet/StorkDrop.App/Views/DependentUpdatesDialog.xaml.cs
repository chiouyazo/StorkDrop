using System.Windows;
using StorkDrop.App.Localization;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.Views;

public partial class DependentUpdatesDialog : Window
{
    /// <summary>The dependents the user chose to update, or empty if skipped.</summary>
    public IReadOnlyList<DependentUpdate> Selected { get; private set; } = [];

    public DependentUpdatesDialog(
        string updatedProductTitle,
        IReadOnlyList<DependentUpdate> candidates
    )
    {
        InitializeComponent();
        MessageText.Text = LocalizationManager
            .GetString("DependentUpdates_Message")
            .Replace("{0}", updatedProductTitle);

        ProductList.ItemsSource = candidates.Select(c => new DependentUpdateItem(c)).ToList();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (ProductList.ItemsSource is IEnumerable<DependentUpdateItem> items)
        {
            Selected = items.Where(i => i.IsSelected).Select(i => i.Update).ToList();
        }
        DialogResult = true;
        Close();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Selected = [];
        DialogResult = true;
        Close();
    }
}
