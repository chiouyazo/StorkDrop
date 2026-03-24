using System.Windows;
using System.Windows.Controls;
using StorkDrop.App.ViewModels;

namespace StorkDrop.App.Views.Marketplace;

public partial class MarketplaceView : UserControl
{
    public MarketplaceView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is MarketplaceViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            if (DataContext is MarketplaceViewModel viewModel)
            {
                viewModel.ErrorMessage = $"Fehler beim Laden: {ex.Message}";
            }
        }
    }
}
