using System.Windows;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;

namespace StorkDrop.App.Views;

public partial class RequiredProductsDialog : Window
{
    public IReadOnlyList<ResolvedPostProduct> SelectedProducts { get; private set; } = [];

    public RequiredProductsDialog(
        string parentProductTitle,
        IReadOnlyList<ResolvedPostProduct> availableProducts,
        IReadOnlyList<ResolvedPostProduct> alreadyInstalled,
        IReadOnlyList<string> unavailableIds
    )
    {
        InitializeComponent();
        MessageText.Text = LocalizationManager
            .GetString("RequiredComponents_Message")
            .Replace("{0}", parentProductTitle);

        List<PostProductItem> items = availableProducts
            .Select(p => new PostProductItem(p, isInstalled: false))
            .Concat(alreadyInstalled.Select(p => new PostProductItem(p, isInstalled: true)))
            .ToList();

        ProductList.ItemsSource = items;
        UnavailableList.ItemsSource = unavailableIds;
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        if (ProductList.ItemsSource is IEnumerable<PostProductItem> items)
        {
            SelectedProducts = items
                .Where(i => i.IsSelected && !i.IsInstalled)
                .Select(i => i.Resolved)
                .ToList();
        }
        DialogResult = true;
        Close();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        SelectedProducts = [];
        DialogResult = true;
        Close();
    }
}
