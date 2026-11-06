using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Demo.Scenarios;
using StorkDrop.Installer;

namespace StorkDrop.Demo.Services;

internal sealed class DemoProductRepository : IProductRepository, IInstalledProductsSnapshot
{
    private readonly object _gate = new object();
    private readonly Dictionary<string, InstalledProduct> _products = new Dictionary<
        string,
        InstalledProduct
    >(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceProvider _services;
    private readonly DemoControlState _control;

    public DemoProductRepository(IServiceProvider services, DemoControlState control)
    {
        _services = services;
        _control = control;
    }

    public Task<IReadOnlyList<InstalledProduct>> GetAllAsync(
        CancellationToken cancellationToken = default
    )
    {
        ThrowIfFailing();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<InstalledProduct>>(_products.Values.ToList());
    }

    public Task<InstalledProduct?> GetByIdAsync(
        string productId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        CancellationToken cancellationToken = default
    )
    {
        lock (_gate)
            return Task.FromResult(_products.GetValueOrDefault(Key(productId, instanceId)));
    }

    public Task<IReadOnlyList<InstalledProduct>> GetInstancesAsync(
        string productId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(GetInstances(productId));

    public IReadOnlyList<InstalledProduct> GetInstances(string productId)
    {
        lock (_gate)
            return _products.Values.Where(p => p.ProductId == productId).ToList();
    }

    public Task AddAsync(InstalledProduct product, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _products[Key(product.ProductId, product.InstanceId)] = product;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(
        InstalledProduct product,
        CancellationToken cancellationToken = default
    ) => AddAsync(product, cancellationToken);

    public Task RemoveAsync(
        string productId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        CancellationToken cancellationToken = default
    )
    {
        lock (_gate)
            _products.Remove(Key(productId, instanceId));

        if (DemoBehaviors.For(productId).RebootOnUninstall)
            SimulateRebootHint();

        return Task.CompletedTask;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private void ThrowIfFailing()
    {
        if (_control.RepositoryFails)
            throw new IOException(
                "The installed products file is locked by another process (demo)."
            );
    }

    private void SimulateRebootHint()
    {
        PropertyInfo? property = typeof(UninstallService).GetProperty(
            nameof(UninstallService.RequiresReboot)
        );
        property
            ?.GetSetMethod(nonPublic: true)
            ?.Invoke(_services.GetRequiredService<UninstallService>(), [true]);
    }

    private static string Key(string productId, string instanceId) => $"{productId}\n{instanceId}";
}
