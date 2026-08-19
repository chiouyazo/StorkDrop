using System.Text.Json;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Registry.Local;

/// <summary>
/// <see cref="IRegistryClient"/> over a local folder, for developer sideloading. Each product is a
/// subfolder (or the root itself) holding a manifest.json plus one package .zip. Everything downstream
/// (requirements, install, elevation) then runs exactly as for a Nexus or S3 feed.
/// </summary>
public sealed class LocalRegistryClient : IRegistryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly string _root;
    private readonly ILogger<LocalRegistryClient> _logger;

    public LocalRegistryClient(string root, ILogger<LocalRegistryClient> logger)
    {
        _root = root;
        _logger = logger;
    }

    public Task<IReadOnlyList<ProductManifest>> GetAllProductsAsync(
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult<IReadOnlyList<ProductManifest>>(
            ReadManifests().Select(entry => entry.Manifest).ToList()
        );

    public Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(FindManifest(productId, version: null));

    public Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(FindManifest(productId, version));

    public Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
        string productId,
        CancellationToken cancellationToken = default
    )
    {
        List<string> versions = ReadManifests()
            .Where(entry =>
                string.Equals(
                    entry.Manifest.ProductId,
                    productId,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Select(entry => entry.Manifest.Version)
            .Where(version => !string.IsNullOrEmpty(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(versions);
    }

    public Task<Stream> DownloadProductAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    )
    {
        foreach ((string dir, ProductManifest manifest) in ReadManifests())
        {
            if (!string.Equals(manifest.ProductId, productId, StringComparison.OrdinalIgnoreCase))
                continue;

            string? package = Directory
                .EnumerateFiles(dir, "*.zip")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (package is null)
            {
                throw new FileNotFoundException(
                    $"No package .zip found next to the manifest of '{productId}' in '{dir}'."
                );
            }

            return Task.FromResult<Stream>(File.OpenRead(package));
        }

        throw new FileNotFoundException(
            $"Product '{productId}' was not found in local feed '{_root}'."
        );
    }

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Directory.Exists(_root));

    private ProductManifest? FindManifest(string productId, string? version)
    {
        foreach ((_, ProductManifest manifest) in ReadManifests())
        {
            if (!string.Equals(manifest.ProductId, productId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (
                version is not null
                && !string.Equals(manifest.Version, version, StringComparison.OrdinalIgnoreCase)
            )
                continue;
            return manifest;
        }
        return null;
    }

    private IEnumerable<(string Dir, ProductManifest Manifest)> ReadManifests()
    {
        if (!Directory.Exists(_root))
            yield break;

        IEnumerable<string> directories = new[] { _root }.Concat(
            Directory.EnumerateDirectories(_root)
        );

        foreach (string dir in directories)
        {
            string manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath))
                continue;

            ProductManifest? manifest = null;
            try
            {
                manifest = JsonSerializer.Deserialize<ProductManifest>(
                    File.ReadAllText(manifestPath),
                    JsonOptions
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Corrupt manifest at {Path}", manifestPath);
            }

            if (manifest is not null && !string.IsNullOrEmpty(manifest.ProductId))
                yield return (dir, manifest);
        }
    }
}
