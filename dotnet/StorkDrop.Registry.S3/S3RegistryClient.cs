using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Registry.S3;

/// <summary>
/// <see cref="IRegistryClient"/> backed by S3 object storage, scoped to a single channel (its top-level
/// prefix). Discovery mirrors the Nexus backend: products and versions are found by listing, manifests
/// are read directly, and there are no side-car index/catalog objects. What a client can see is exactly
/// what its credentials are allowed to list/get, so per-prefix IAM rights are the access boundary.
/// Downloads are streamed to a temp file and verified against the manifest's <c>contentSha256</c>.
/// </summary>
public sealed class S3RegistryClient : IRegistryClient
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly S3Layout _layout;
    private readonly string _channel;
    private readonly bool _allowUnverified;
    private readonly ILogger<S3RegistryClient> _logger;

    public S3RegistryClient(
        IAmazonS3 s3,
        string bucket,
        S3Layout layout,
        string channel,
        ILogger<S3RegistryClient> logger,
        bool allowUnverified = false
    )
    {
        _s3 = s3;
        _bucket = bucket;
        _layout = layout;
        _channel = channel;
        _allowUnverified = allowUnverified;
        _logger = logger;
    }

    public string Channel => _channel;

    public async Task<IReadOnlyList<ProductManifest>> GetAllProductsAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<string> productIds = await ListChildNamesAsync(
                _layout.ChannelRoot(_channel),
                cancellationToken
            )
            .ConfigureAwait(false);

        // Fetch the per-product root manifests in parallel (the S3 client is thread-safe).
        ProductManifest?[] manifests = await Task.WhenAll(
                productIds.Select(id => GetProductManifestAsync(id, cancellationToken))
            )
            .ConfigureAwait(false);

        return manifests.Where(m => m is not null).Cast<ProductManifest>().ToList();
    }

    public Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        CancellationToken cancellationToken = default
    ) => ReadManifestAsync(_layout.LatestManifestKey(_channel, productId), cancellationToken);

    public Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    ) =>
        ReadManifestAsync(
            _layout.VersionManifestKey(_channel, productId, version),
            cancellationToken
        );

    public async Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
        string productId,
        CancellationToken cancellationToken = default
    )
    {
        List<string> versions = await ListChildNamesAsync(
                _layout.VersionsRoot(_channel, productId),
                cancellationToken
            )
            .ConfigureAwait(false);
        versions.Sort(VersionComparer.Instance);
        return versions;
    }

    public async Task<Stream> DownloadProductAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    )
    {
        string packageKey = _layout.PackageKey(_channel, productId, version);

        ProductManifest? manifest = await GetProductManifestAsync(
                productId,
                version,
                cancellationToken
            )
            .ConfigureAwait(false);
        string? expected = manifest?.ContentSha256?.Trim().ToLowerInvariant();

        string tempFile = Path.Combine(Path.GetTempPath(), $"storkdrop-s3-{Guid.NewGuid():N}.zip");
        string actual;

        try
        {
            using GetObjectResponse response = await _s3.GetObjectAsync(
                    new GetObjectRequest { BucketName = _bucket, Key = packageKey },
                    cancellationToken
                )
                .ConfigureAwait(false);

            await using Stream source = response.ResponseStream;
            await using FileStream destination = File.Create(tempFile);
            using SHA256 sha = SHA256.Create();
            await using CryptoStream crypto = new CryptoStream(
                destination,
                sha,
                CryptoStreamMode.Write
            );
            await source.CopyToAsync(crypto, cancellationToken).ConfigureAwait(false);
            await crypto.FlushFinalBlockAsync(cancellationToken).ConfigureAwait(false);
            actual = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            TryDelete(tempFile);
            throw new FileNotFoundException(
                $"Package for {productId} {version} not found in bucket '{_bucket}' at '{packageKey}'.",
                ex
            );
        }
        catch
        {
            TryDelete(tempFile);
            throw;
        }

        if (
            !string.IsNullOrWhiteSpace(expected)
            && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)
        )
        {
            TryDelete(tempFile);
            throw new InvalidOperationException(
                $"Checksum mismatch for {productId} {version}: expected {expected}, downloaded {actual}."
            );
        }

        if (string.IsNullOrWhiteSpace(expected))
        {
            if (!_allowUnverified)
            {
                TryDelete(tempFile);
                throw new InvalidOperationException(
                    $"Manifest for {productId} {version} has no contentSha256, so the download cannot "
                        + "be verified. Publish with a checksum, or set AllowUnverified on the feed to opt out."
                );
            }

            _logger.LogWarning(
                "Manifest for {ProductId} {Version} has no contentSha256; download NOT verified (AllowUnverified)",
                productId,
                version
            );
        }

        return new FileStream(
            tempFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous
        );
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _s3.ListObjectsV2Async(
                    new ListObjectsV2Request
                    {
                        BucketName = _bucket,
                        Prefix = _layout.ChannelRoot(_channel),
                        MaxKeys = 1,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "S3 connection test failed for channel {Channel}", _channel);
            return false;
        }
    }

    private async Task<ProductManifest?> ReadManifestAsync(
        string key,
        CancellationToken cancellationToken
    )
    {
        string? json = await ReadTextAsync(key, cancellationToken).ConfigureAwait(false);
        if (json is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<ProductManifest>(json, S3Json.Options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Manifest at {Key} is corrupt", key);
            return null;
        }
    }

    private async Task<string?> ReadTextAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            using GetObjectResponse response = await _s3.GetObjectAsync(
                    new GetObjectRequest { BucketName = _bucket, Key = key },
                    cancellationToken
                )
                .ConfigureAwait(false);
            using StreamReader reader = new StreamReader(response.ResponseStream);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Lists the immediate child "folder" names under a prefix (via the delimiter), e.g. the product
    /// ids under a channel, or the versions under a product.
    /// </summary>
    private async Task<List<string>> ListChildNamesAsync(
        string prefix,
        CancellationToken cancellationToken
    )
    {
        List<string> names = [];
        string? continuationToken = null;

        do
        {
            ListObjectsV2Response response = await _s3.ListObjectsV2Async(
                    new ListObjectsV2Request
                    {
                        BucketName = _bucket,
                        Prefix = prefix,
                        Delimiter = "/",
                        ContinuationToken = continuationToken,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            foreach (string commonPrefix in response.CommonPrefixes ?? [])
            {
                string name =
                    commonPrefix.Length > prefix.Length
                        ? commonPrefix[prefix.Length..].TrimEnd('/')
                        : string.Empty;
                if (name.Length > 0)
                    names.Add(name);
            }

            continuationToken =
                response.IsTruncated == true ? response.NextContinuationToken : null;
        } while (continuationToken is not null);

        return names;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete temp download {Path}", path);
        }
    }
}
