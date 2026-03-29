using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Registry.Nexus;

public sealed class NexusRegistryClient(
    HttpClient httpClient,
    IOptions<NexusOptions> options,
    ILogger<NexusRegistryClient> logger
) : IRegistryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private string BaseApi => options.Value.BaseUrl.TrimEnd('/');

    private string RawContentBase => $"{BaseApi}/repository/{options.Value.Repository}";

    public async Task<IReadOnlyList<ProductManifest>> GetAllProductsAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<ProductManifest> products = [];
        HashSet<string> discoveredProducts = [];

        string componentsUrl =
            $"{BaseApi}/service/rest/v1/components?repository={options.Value.Repository}";
        string? continuationToken = null;

        do
        {
            string url = continuationToken is not null
                ? $"{componentsUrl}&continuationToken={continuationToken}"
                : componentsUrl;

            NexusComponentSearchResponse response =
                await httpClient
                    .GetFromJsonAsync<NexusComponentSearchResponse>(
                        url,
                        JsonOptions,
                        cancellationToken
                    )
                    .ConfigureAwait(false) ?? new NexusComponentSearchResponse();

            foreach (NexusComponent component in response.Items)
            {
                foreach (NexusAsset asset in component.Assets)
                {
                    // Look for manifest.json at the root level: {productId}/manifest.json
                    string path = asset.Path.TrimStart('/');
                    string[] segments = path.Split('/');
                    if (segments.Length == 2 && segments[1] == "manifest.json")
                    {
                        discoveredProducts.Add(segments[0]);
                    }
                }
            }

            continuationToken = response.ContinuationToken;
        } while (continuationToken is not null);

        foreach (string productId in discoveredProducts)
        {
            ProductManifest? manifest = await GetProductManifestAsync(productId, cancellationToken)
                .ConfigureAwait(false);
            if (manifest is not null)
            {
                products.Add(manifest);
            }
        }

        return products;
    }

    public async Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        CancellationToken cancellationToken = default
    )
    {
        string url = $"{RawContentBase}/{productId}/manifest.json";

        using HttpResponseMessage response = await httpClient
            .GetAsync(url, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        if (response.Content is null)
            return null;

        string json = await response
            .Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return JsonSerializer.Deserialize<ProductManifest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Fehler beim Deserialisieren des Manifests für {ProductId}",
                productId
            );
            return null;
        }
    }

    public async Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    )
    {
        string url = $"{RawContentBase}/{productId}/versions/{version}/manifest.json";

        using HttpResponseMessage response = await httpClient
            .GetAsync(url, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        if (response.Content is null)
            return null;

        string json = await response
            .Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return JsonSerializer.Deserialize<ProductManifest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Fehler beim Deserialisieren des Manifests für {ProductId} v{Version}",
                productId,
                version
            );
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
        string productId,
        CancellationToken cancellationToken = default
    )
    {
        List<string> versions = [];

        string componentsUrl =
            $"{BaseApi}/service/rest/v1/components?repository={options.Value.Repository}";

        string prefix = $"{productId}/versions/";
        string? continuationToken = null;

        do
        {
            string url = continuationToken is not null
                ? $"{componentsUrl}&continuationToken={continuationToken}"
                : componentsUrl;

            NexusComponentSearchResponse response =
                await httpClient
                    .GetFromJsonAsync<NexusComponentSearchResponse>(
                        url,
                        JsonOptions,
                        cancellationToken
                    )
                    .ConfigureAwait(false) ?? new NexusComponentSearchResponse();

            foreach (NexusComponent component in response.Items)
            {
                foreach (NexusAsset asset in component.Assets)
                {
                    string path = asset.Path.TrimStart('/');
                    if (path.StartsWith(prefix) && path.EndsWith("/manifest.json"))
                    {
                        string version = path[prefix.Length..];
                        version = version[..version.IndexOf('/')];
                        if (!versions.Contains(version))
                        {
                            versions.Add(version);
                        }
                    }
                }
            }

            continuationToken = response.ContinuationToken;
        } while (continuationToken is not null);

        versions.Sort(VersionComparer.Instance);
        return versions;
    }

    public async Task<Stream> DownloadProductAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    )
    {
        string url = $"{RawContentBase}/{productId}/versions/{version}/{productId}-{version}.zip";

        HttpResponseMessage response = await httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string url = $"{BaseApi}/service/rest/v1/repositories";
            using HttpResponseMessage response = await httpClient
                .GetAsync(url, cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            // Connection test failure is expected in offline/misconfigured scenarios
            logger.LogDebug(ex, "Verbindungstest fehlgeschlagen");
            return false;
        }
    }

    /// <summary>
    /// Lists all raw hosted repositories accessible to the authenticated account.
    /// </summary>
    public static async Task<IReadOnlyList<NexusRepositoryInfo>> ListRawHostedRepositoriesAsync(
        HttpClient httpClient,
        string baseUrl,
        CancellationToken cancellationToken = default
    )
    {
        string url = $"{baseUrl.TrimEnd('/')}/service/rest/v1/repositories";

        NexusRepositoryInfo[]? repos = await httpClient
            .GetFromJsonAsync<NexusRepositoryInfo[]>(url, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (repos is null)
            return [];

        return repos
            .Where(r =>
                r.Format.Equals("raw", StringComparison.OrdinalIgnoreCase)
                && r.Type.Equals("hosted", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
    }
}
