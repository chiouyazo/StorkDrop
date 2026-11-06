using StorkDrop.Contracts.Models;

namespace StorkDrop.Installer;

/// <summary>
/// Synchronous read access to the installed products for callers that cannot await, such as the
/// synchronous plugin callbacks.
/// </summary>
public interface IInstalledProductsSnapshot
{
    IReadOnlyList<InstalledProduct> GetInstances(string productId);
}
