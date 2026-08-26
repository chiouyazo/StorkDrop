using StorkDrop.App.Localization;
using StorkDrop.App.Views;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.Services;

/// <summary>
/// Resolves a manifest's recommended install path when it anchors into another product via a
/// <see cref="ProductPathToken"/> (e.g. <c>{ProductPath:acme.suite}/addons/plugins</c>): the
/// user is asked which installed instance of that product to target before anything else happens.
/// </summary>
public static class InstanceTargetResolver
{
    /// <summary>
    /// If <paramref name="manifest"/>'s recommended install path references another product, prompts
    /// for one of its installed instances and returns the resolved path.
    /// </summary>
    /// <returns>
    /// <c>Proceed=false</c> when the install must be aborted (no instances, or the user cancelled);
    /// <c>Error</c> carries a message to surface in that case. <c>Path</c> is the resolved default path
    /// when a token was present, or null when the manifest has no token (caller keeps its own default).
    /// </returns>
    public static async Task<(bool Proceed, string? Path, string? Error)> ResolveAsync(
        ProductManifest manifest,
        IProductRepository productRepository
    )
    {
        string? referencedId = ProductPathToken.GetReferencedProductId(
            manifest.RecommendedInstallPath
        );
        if (referencedId is null)
            return (true, null, null);

        IReadOnlyList<InstalledProduct> instances = await productRepository.GetInstancesAsync(
            referencedId
        );
        if (instances.Count == 0)
        {
            string error = LocalizationManager
                .GetString("SelectInstance_NoInstances")
                .Replace("{0}", referencedId);
            return (false, null, error);
        }

        InstalledProduct? selected = System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            SelectInstanceDialog dialog = new SelectInstanceDialog(
                manifest.Title,
                referencedId,
                instances
            )
            {
                Owner = System.Windows.Application.Current.MainWindow,
            };
            return dialog.ShowDialog() == true ? dialog.SelectedInstance : null;
        });

        if (selected is null)
            return (false, null, null);

        string resolved = ProductPathToken.Resolve(
            manifest.RecommendedInstallPath!,
            selected.InstalledPath
        );
        return (true, resolved, null);
    }
}
