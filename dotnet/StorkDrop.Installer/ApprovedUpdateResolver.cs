using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Installer;

public static class ApprovedUpdateResolver
{
    public static async Task<ApprovedUpdateResolution> ResolveAsync(
        IRegistryClient client,
        string productId,
        string approvedVersion,
        CancellationToken cancellationToken
    )
    {
        ProductManifest? exact = await client.GetProductManifestAsync(
            productId,
            approvedVersion,
            cancellationToken
        );
        if (
            exact is not null
            && string.Equals(exact.Version, approvedVersion, StringComparison.Ordinal)
        )
            return new ApprovedUpdateResolution(exact, null);

        ProductManifest? latest = await client.GetProductManifestAsync(
            productId,
            cancellationToken
        );
        return new ApprovedUpdateResolution(null, latest);
    }
}
