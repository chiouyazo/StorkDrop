using System.Windows.Controls;
using System.Windows.Input;
using StorkDrop.App.ViewModels;

namespace StorkDrop.App.Views.Marketplace;

public partial class ProductCard : UserControl
{
    public ProductCard()
    {
        InitializeComponent();
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        // Don't navigate if the click originated from a Button (install/update)
        if (e.OriginalSource is System.Windows.DependencyObject source
            && FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null)
            return;

        NavigateToDetail();
        e.Handled = true;
    }

    private void NavigateToDetail()
    {
        if (DataContext is ProductCardViewModel card)
        {
            ItemsControl? itemsControl = FindAncestor<ItemsControl>(this);
            if (itemsControl?.DataContext is MarketplaceViewModel marketplace)
            {
                marketplace.NavigateToDetailCommand.Execute(card);
            }
        }
    }

    private static T? FindAncestor<T>(System.Windows.DependencyObject current)
        where T : System.Windows.DependencyObject
    {
        while (current is not null)
        {
            if (current is T ancestor)
                return ancestor;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
