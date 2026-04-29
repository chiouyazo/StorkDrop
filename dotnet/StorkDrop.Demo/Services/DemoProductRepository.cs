using System.Collections.Concurrent;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Demo.Data;

namespace StorkDrop.Demo.Services;

internal sealed class DemoProductRepository : IProductRepository
{
    private readonly ConcurrentDictionary<string, InstalledProduct> _products =
        new ConcurrentDictionary<string, InstalledProduct>();

    public DemoProductRepository()
    {
        InstalledProduct cli = DemoProducts.PreInstalledCliTools;
        _products[cli.ProductId] = cli;
    }

    public Task<IReadOnlyList<InstalledProduct>> GetAllAsync(
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<InstalledProduct>>(_products.Values.ToList());

    public Task<InstalledProduct?> GetByIdAsync(
        string productId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(_products.GetValueOrDefault(productId));

    public Task<IReadOnlyList<InstalledProduct>> GetInstancesAsync(
        string productId,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult<IReadOnlyList<InstalledProduct>>(
            _products.Values.Where(p => p.ProductId == productId).ToList()
        );

    public Task AddAsync(InstalledProduct product, CancellationToken cancellationToken = default)
    {
        _products[product.ProductId] = product;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(InstalledProduct product, CancellationToken cancellationToken = default)
    {
        _products[product.ProductId] = product;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(
        string productId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        CancellationToken cancellationToken = default
    )
    {
        _products.TryRemove(productId, out _);
        return Task.CompletedTask;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
