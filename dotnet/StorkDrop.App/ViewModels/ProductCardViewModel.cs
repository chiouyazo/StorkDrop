using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using StorkDrop.Core.Models;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model representing a single product card in the marketplace.
/// </summary>
public partial class ProductCardViewModel : ObservableObject
{
    [ObservableProperty]
    private string _productId = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _version = string.Empty;

    [ObservableProperty]
    private InstallType _installType;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private int _installPercentage;

    [ObservableProperty]
    private string _installStatusMessage = string.Empty;

    [ObservableProperty]
    private string _installedVersion = string.Empty;

    [ObservableProperty]
    private string? _imageUrl;

    [ObservableProperty]
    private BitmapImage? _productImage;

    [ObservableProperty]
    private bool _isImageLoading;

    [ObservableProperty]
    private string? _publisher;

    [ObservableProperty]
    private string? _feedName;

    /// <summary>
    /// Loads the product image from the specified URL asynchronously.
    /// </summary>
    public async Task LoadImageAsync()
    {
        if (string.IsNullOrEmpty(ImageUrl))
            return;

        try
        {
            IsImageLoading = true;
            using System.Net.Http.HttpClient client = new System.Net.Http.HttpClient();
            byte[] imageData = await client.GetByteArrayAsync(ImageUrl);

            BitmapImage bitmap = new BitmapImage();
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(imageData))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
            }

            ProductImage = bitmap;
        }
        catch
        {
            // Image load failed, fallback to icon
            ProductImage = null;
        }
        finally
        {
            IsImageLoading = false;
        }
    }
}
