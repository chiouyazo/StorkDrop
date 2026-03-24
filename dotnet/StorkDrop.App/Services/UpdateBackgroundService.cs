using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StorkDrop.Core.Interfaces;
using StorkDrop.Core.Models;
using StorkDrop.Core.Services;

namespace StorkDrop.App.Services;

/// <summary>
/// Background service that periodically checks for product updates and shows notifications.
/// </summary>
public sealed class UpdateBackgroundService : BackgroundService
{
    private readonly IRegistryClient _registryClient;
    private readonly IProductRepository _productRepository;
    private readonly IConfigurationService _configurationService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<UpdateBackgroundService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateBackgroundService"/> class.
    /// </summary>
    public UpdateBackgroundService(
        IRegistryClient registryClient,
        IProductRepository productRepository,
        IConfigurationService configurationService,
        INotificationService notificationService,
        ILogger<UpdateBackgroundService> logger
    )
    {
        _registryClient = registryClient;
        _productRepository = productRepository;
        _configurationService = configurationService;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before first check to let the app finish loading
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                AppConfiguration? config = await _configurationService
                    .LoadAsync(stoppingToken)
                    .ConfigureAwait(false);

                if (config is not null && config.AutoCheckForUpdates)
                {
                    IReadOnlyList<InstalledProduct> installed = await _productRepository
                        .GetAllAsync(stoppingToken)
                        .ConfigureAwait(false);
                    IReadOnlyList<ProductManifest> available = await _registryClient
                        .GetAllProductsAsync(stoppingToken)
                        .ConfigureAwait(false);

                    foreach (InstalledProduct product in installed)
                    {
                        ProductManifest? latest = available.FirstOrDefault(m =>
                            m.ProductId == product.ProductId
                        );
                        if (
                            latest is not null
                            && !string.IsNullOrEmpty(latest.Version)
                            && VersionComparer.IsNewer(latest.Version, product.Version)
                        )
                        {
                            try
                            {
                                _notificationService.ShowUpdateAvailable(
                                    product.Title,
                                    latest.Version
                                );
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(
                                    ex,
                                    "Failed to show update notification for {ProductTitle}",
                                    product.Title
                                );
                            }
                        }
                    }

                    TimeSpan interval =
                        config.CheckInterval > TimeSpan.Zero
                            ? config.CheckInterval
                            : TimeSpan.FromHours(4);

                    await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
                when (ex is OperationCanceledException && stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for updates");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
