using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Installer;

public sealed class ProductRepository : IProductRepository, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<ProductRepository> _logger;
    private List<InstalledProduct> _products = [];
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ProductRepository(ILogger<ProductRepository> logger)
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StorkDrop",
                "Stork",
                "Config",
                "installed-products.json"
            ),
            logger
        ) { }

    public ProductRepository(string filePath, ILogger<ProductRepository> logger)
    {
        _filePath = filePath;
        _logger = logger;
        string? directory = Path.GetDirectoryName(_filePath);
        if (directory is not null)
            Directory.CreateDirectory(directory);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadFromDiskAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Reloads products from disk, discarding in-memory state.
    /// Call this after an elevated process may have modified the file.
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _initialized = false;
            await LoadFromDiskAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;
        await LoadFromDiskAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadFromDiskAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Loading product repository from {FilePath}", _filePath);
        if (File.Exists(_filePath))
        {
            string json = await File.ReadAllTextAsync(_filePath, cancellationToken)
                .ConfigureAwait(false);
            List<InstalledProduct>? deserialized = JsonSerializer.Deserialize<
                List<InstalledProduct>
            >(json, JsonOptions);
            _products = deserialized ?? [];

            ValidateNoDuplicateProductIds(_products);
            _logger.LogInformation(
                "Loaded {Count} installed products from repository",
                _products.Count
            );
        }
        else
        {
            _products = [];
            _logger.LogDebug(
                "Product repository file not found at {FilePath}, starting empty",
                _filePath
            );
        }

        _initialized = true;
    }

    public async Task<IReadOnlyList<InstalledProduct>> GetAllAsync(
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            return _products.ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<InstalledProduct?> GetByIdAsync(
        string productId,
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            return _products.FirstOrDefault(p => p.ProductId == productId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddAsync(
        InstalledProduct product,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation(
            "Adding product {ProductId} v{Version} to repository",
            product.ProductId,
            product.Version
        );
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            _products.RemoveAll(p => p.ProductId == product.ProductId);
            _products.Add(product);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAsync(
        InstalledProduct product,
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            int index = _products.FindIndex(p => p.ProductId == product.ProductId);
            if (index >= 0)
            {
                _products[index] = product;
                await SaveAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAsync(string productId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing product {ProductId} from repository", productId);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            int removed = _products.RemoveAll(p => p.ProductId == productId);
            if (removed > 0)
            {
                await SaveAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Saving product repository ({Count} products)", _products.Count);
        string json = JsonSerializer.Serialize(_products, JsonOptions);
        string tempPath = _filePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best effort cleanup of temp file
            }
        }
    }

    private static void ValidateNoDuplicateProductIds(List<InstalledProduct> products)
    {
        HashSet<string> seen = [];
        foreach (InstalledProduct product in products)
        {
            if (!seen.Add(product.ProductId))
            {
                throw new InvalidOperationException(
                    $"Doppelte ProductId in installierter Produktliste: {product.ProductId}"
                );
            }
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
