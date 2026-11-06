using System.Net;
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

    private string ComponentsUrl(string? continuationToken)
    {
        string url =
            $"{BaseApi}/service/rest/v1/components?repository={Uri.EscapeDataString(options.Value.Repository)}";
        return continuationToken is not null
            ? $"{url}&continuationToken={Uri.EscapeDataString(continuationToken)}"
            : url;
    }

    public async Task<IReadOnlyList<ProductManifest>> GetAllProductsAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<ProductManifest> products = [];
        HashSet<string> discoveredProducts = [];
        string? continuationToken = null;

        do
        {
            string url = ComponentsUrl(continuationToken);

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

        List<HttpRequestException> failures = [];
        foreach (string productId in discoveredProducts)
        {
            try
            {
                ProductManifest? manifest = await GetProductManifestAsync(
                        productId,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (manifest is not null)
                {
                    products.Add(manifest);
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode != HttpStatusCode.Unauthorized)
            {
                logger.LogWarning(ex, "Manifest of {ProductId} could not be read", productId);
                failures.Add(ex);
            }
        }

        if (products.Count == 0 && failures.Count > 0)
            throw failures[0];

        return products;
    }

    public Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        CancellationToken cancellationToken = default
    ) =>
        FetchManifestAsync(
            $"{RawContentBase}/{productId}/manifest.json",
            productId,
            null,
            cancellationToken
        );

    public Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    ) =>
        FetchManifestAsync(
            $"{RawContentBase}/{productId}/versions/{version}/manifest.json",
            productId,
            version,
            cancellationToken
        );

    private async Task<ProductManifest?> FetchManifestAsync(
        string url,
        string productId,
        string? version,
        CancellationToken cancellationToken
    )
    {
        using HttpResponseMessage response = await httpClient
            .GetAsync(url, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        string json = await response
            .Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        ProductManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ProductManifest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Fehler beim Deserialisieren des Manifests für {ProductId} {Version}",
                productId,
                version
            );
            return null;
        }

        if (manifest is not null && !IsRequestedManifest(manifest, productId, version))
        {
            logger.LogWarning(
                "Manifest at {Url} describes {ActualId} {ActualVersion}, not {ProductId} {Version}; ignoring it",
                url,
                manifest.ProductId,
                manifest.Version,
                productId,
                version
            );
            return null;
        }

        return manifest;
    }

    private static bool IsRequestedManifest(
        ProductManifest manifest,
        string productId,
        string? version
    ) =>
        string.Equals(manifest.ProductId, productId, StringComparison.OrdinalIgnoreCase)
        && (version is null || VersionComparer.Instance.Compare(manifest.Version, version) == 0);

    public async Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
        string productId,
        CancellationToken cancellationToken = default
    )
    {
        List<string> versions = [];
        string? continuationToken = null;

        do
        {
            string url = ComponentsUrl(continuationToken);

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
                    string[] segments = asset.Path.TrimStart('/').Split('/');
                    if (
                        segments.Length == 4
                        && string.Equals(segments[0], productId, StringComparison.Ordinal)
                        && string.Equals(segments[1], "versions", StringComparison.Ordinal)
                        && string.Equals(segments[3], "manifest.json", StringComparison.Ordinal)
                        && !versions.Contains(segments[2])
                    )
                    {
                        versions.Add(segments[2]);
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
    /// Lists all raw hosted and group repositories accessible to the authenticated account.
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
                && (
                    r.Type.Equals("hosted", StringComparison.OrdinalIgnoreCase)
                    || r.Type.Equals("group", StringComparison.OrdinalIgnoreCase)
                )
            )
            .ToList();
    }
}
