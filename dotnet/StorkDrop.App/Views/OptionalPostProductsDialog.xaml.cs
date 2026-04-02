using System.Windows;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;

namespace StorkDrop.App.Views;

public partial class OptionalPostProductsDialog : Window
{
    public IReadOnlyList<ResolvedPostProduct> SelectedProducts { get; private set; } = [];

    public OptionalPostProductsDialog(
        string parentProductTitle,
        IReadOnlyList<ResolvedPostProduct> availableProducts,
        IReadOnlyList<ResolvedPostProduct> alreadyInstalled
    )
    {
        InitializeComponent();
        MessageText.Text = LocalizationManager
            .GetString("OptionalProducts_Message")
            .Replace("{0}", parentProductTitle);

        List<PostProductItem> items = availableProducts
            .Select(p => new PostProductItem(p, isInstalled: false))
            .Concat(alreadyInstalled.Select(p => new PostProductItem(p, isInstalled: true)))
            .ToList();

        ProductList.ItemsSource = items;
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
        DialogResult = false;
        Close();
    }
}
