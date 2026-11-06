using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

public sealed class ProductRepository : IProductRepository, IInstalledProductsSnapshot, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private readonly ILogger<ProductRepository> _logger;
    private List<InstalledProduct> _products = [];
    private volatile IReadOnlyList<InstalledProduct>? _snapshot;
    private FileStamp _loadedStamp;
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ProductRepository(ILogger<ProductRepository> logger)
        : this(StorkPaths.InstalledProductsFile, logger) { }

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
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await LoadFromDiskAsync(holdsFileLock: false, cancellationToken);
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
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _initialized = false;
            await LoadFromDiskAsync(holdsFileLock: false, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureCurrentAsync(CancellationToken cancellationToken)
    {
        if (_initialized && _loadedStamp == FileStamp.Of(_filePath))
            return;
        await LoadFromDiskAsync(holdsFileLock: false, cancellationToken);
    }

    private async Task LoadFromDiskAsync(bool holdsFileLock, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Loading product repository from {FilePath}", _filePath);
        FileStamp stampBeforeRead = FileStamp.Of(_filePath);
        string? json = await AtomicFile.ReadAllTextAsync(
            _filePath,
            cancellationToken: cancellationToken
        );

        if (json is null)
        {
            _products = [];
            _snapshot = _products.ToList();
            _logger.LogDebug(
                "Product repository file not found at {FilePath}, starting empty",
                _filePath
            );
            _loadedStamp = stampBeforeRead;
            _initialized = true;
            return;
        }

        List<InstalledProduct> loaded =
            JsonSerializer.Deserialize<List<InstalledProduct>>(json, JsonOptions) ?? [];
        bool migrated = MigrateInstanceIds(loaded);
        ValidateNoDuplicateProducts(loaded);

        if (migrated && !holdsFileLock)
        {
            using IDisposable fileLock = await CrossProcessFileLock.AcquireAsync(
                _filePath,
                cancellationToken: cancellationToken
            );
            await LoadFromDiskAsync(holdsFileLock: true, cancellationToken);
            return;
        }

        _products = loaded;
        if (migrated)
        {
            _logger.LogInformation("Migrated legacy products to include InstanceId, saving");
            await SaveAsync(cancellationToken);
        }
        else
        {
            _loadedStamp = stampBeforeRead;
        }

        _snapshot = _products.ToList();
        _logger.LogInformation(
            "Loaded {Count} installed products from repository",
            _products.Count
        );
        _initialized = true;
    }

    public IReadOnlyList<InstalledProduct> GetInstances(string productId)
    {
        IReadOnlyList<InstalledProduct> products = _snapshot ?? ReadFromDisk();
        return products.Where(p => p.ProductId == productId).ToList();
    }

    private IReadOnlyList<InstalledProduct> ReadFromDisk()
    {
        if (!File.Exists(_filePath))
            return [];

        string json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<List<InstalledProduct>>(json, JsonOptions) ?? [];
    }

    public async Task<IReadOnlyList<InstalledProduct>> GetAllAsync(
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureCurrentAsync(cancellationToken);
            return _products.ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<InstalledProduct?> GetByIdAsync(
        string productId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureCurrentAsync(cancellationToken);
            return _products.FirstOrDefault(p =>
                p.ProductId == productId && p.InstanceId == instanceId
            );
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<InstalledProduct>> GetInstancesAsync(
        string productId,
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureCurrentAsync(cancellationToken);
            return _products.Where(p => p.ProductId == productId).ToList();
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
            "Adding product {ProductId}/{InstanceId} v{Version} to repository",
            product.ProductId,
            product.InstanceId,
            product.Version
        );
        await MutateAsync(
            () =>
            {
                _products.RemoveAll(p =>
                    p.ProductId == product.ProductId && p.InstanceId == product.InstanceId
                );
                _products.Add(product);
                return true;
            },
            cancellationToken
        );
    }

    public async Task UpdateAsync(
        InstalledProduct product,
        CancellationToken cancellationToken = default
    )
    {
        await MutateAsync(
            () =>
            {
                int index = _products.FindIndex(p =>
                    p.ProductId == product.ProductId && p.InstanceId == product.InstanceId
                );
                if (index < 0)
                    return false;

                _products[index] = product;
                return true;
            },
            cancellationToken
        );
    }

    public async Task RemoveAsync(
        string productId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation(
            "Removing product {ProductId}/{InstanceId} from repository",
            productId,
            instanceId
        );
        await MutateAsync(
            () =>
                _products.RemoveAll(p => p.ProductId == productId && p.InstanceId == instanceId)
                > 0,
            cancellationToken
        );
    }

    private async Task MutateAsync(Func<bool> mutation, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            using IDisposable fileLock = await CrossProcessFileLock.AcquireAsync(
                _filePath,
                cancellationToken: cancellationToken
            );
            await LoadFromDiskAsync(holdsFileLock: true, cancellationToken);

            if (mutation())
                await SaveAsync(cancellationToken);
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
        await SafeFileWriter.WriteAtomicWithBackupAsync(_filePath, json, cancellationToken);
        _loadedStamp = FileStamp.Of(_filePath);
        _snapshot = _products.ToList();
    }

    private bool MigrateInstanceIds(List<InstalledProduct> products)
    {
        bool migrated = false;
        for (int i = 0; i < products.Count; i++)
        {
            if (string.IsNullOrEmpty(products[i].InstanceId))
            {
                _logger.LogWarning(
                    "Product {ProductId} has no InstanceId, migrating to 'default'",
                    products[i].ProductId
                );
                products[i] = products[i] with { InstanceId = InstanceIdHelper.DefaultInstanceId };
                migrated = true;
            }
        }

        // Deduplicate: if multiple entries have the same (ProductId, InstanceId) after migration,
        // keep the most recently installed one
        List<InstalledProduct> deduped = products
            .GroupBy(p => (p.ProductId, p.InstanceId))
            .Select(g =>
            {
                if (g.Count() > 1)
                {
                    _logger.LogWarning(
                        "Duplicate entries for {ProductId}/{InstanceId}, keeping most recent",
                        g.Key.ProductId,
                        g.Key.InstanceId
                    );
                    migrated = true;
                    return g.OrderByDescending(p => p.InstalledDate).First();
                }
                return g.First();
            })
            .ToList();

        if (migrated)
        {
            products.Clear();
            products.AddRange(deduped);
        }

        return migrated;
    }

    private static void ValidateNoDuplicateProducts(List<InstalledProduct> products)
    {
        HashSet<(string, string)> seen = [];
        foreach (InstalledProduct product in products)
        {
            if (!seen.Add((product.ProductId, product.InstanceId)))
            {
                throw new InvalidOperationException(
                    $"Duplicate ProductId/InstanceId in installed product list: {product.ProductId} ({product.InstanceId})"
                );
            }
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
