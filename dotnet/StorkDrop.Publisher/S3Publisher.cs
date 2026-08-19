using System.Security.Cryptography;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using StorkDrop.Contracts.Models;
using StorkDrop.Registry.S3;

namespace StorkDrop.Publisher;

/// <summary>
/// Publishes products into a StorkDrop S3 bucket using the same layout as a Nexus raw repository (see
/// <see cref="S3Layout"/>), with the channel as the top-level prefix. A publish uploads the version
/// manifest (with the package SHA-256 embedded in <see cref="ProductManifest.ContentSha256"/>) and the
/// package, then refreshes the product's root <c>manifest.json</c> to point at the highest version.
/// There are no index/catalog/pointer objects: clients discover by listing, so what a customer can see
/// is governed purely by the IAM rights on the channel/product prefixes.
/// </summary>
public sealed class S3Publisher
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly S3Layout _layout;

    public S3Publisher(IAmazonS3 s3, string bucket, string? prefix = null)
    {
        _s3 = s3;
        _bucket = bucket;
        _layout = new S3Layout(prefix);
    }

    /// <summary>
    /// Publishes one product version into a channel. Returns the manifest actually stored (with
    /// <see cref="ProductManifest.ContentSha256"/> populated from the package).
    /// </summary>
    public async Task<ProductManifest> PublishAsync(
        string channel,
        ProductManifest manifest,
        Stream packageZip,
        CancellationToken cancellationToken = default
    )
    {
        // Validate at the write boundary: these become S3 key segments and drive IAM prefixes.
        S3Names.Require(channel, nameof(channel));
        string productId = S3Names.Require(manifest.ProductId, "productId");
        string version = S3Names.Require(manifest.Version, "version");

        string tempFile = Path.Combine(
            Path.GetTempPath(),
            $"storkdrop-publish-{Guid.NewGuid():N}.zip"
        );
        try
        {
            string hash = await CopyAndHashAsync(packageZip, tempFile, cancellationToken)
                .ConfigureAwait(false);
            ProductManifest stored = manifest with { ContentSha256 = hash };

            await _s3.PutObjectAsync(
                    new PutObjectRequest
                    {
                        BucketName = _bucket,
                        Key = _layout.PackageKey(channel, productId, version),
                        FilePath = tempFile,
                        ContentType = "application/zip",
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            await PutJsonAsync(
                    _layout.VersionManifestKey(channel, productId, version),
                    stored,
                    cancellationToken
                )
                .ConfigureAwait(false);

            // Nexus parity: the version being published becomes "latest" (the root manifest is a copy
            // of it). No auto-max, no listing - publish the version you want as latest, last.
            await PutJsonAsync(
                    _layout.LatestManifestKey(channel, productId),
                    stored,
                    cancellationToken
                )
                .ConfigureAwait(false);

            return stored;
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    private Task PutJsonAsync<T>(string key, T value, CancellationToken cancellationToken) =>
        _s3.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                ContentBody = JsonSerializer.Serialize(value, S3Json.Options),
                ContentType = "application/json",
            },
            cancellationToken
        );

    private static async Task<string> CopyAndHashAsync(
        Stream source,
        string destinationPath,
        CancellationToken cancellationToken
    )
    {
        await using FileStream destination = File.Create(destinationPath);
        using SHA256 sha = SHA256.Create();
        await using CryptoStream crypto = new CryptoStream(
            destination,
            sha,
            CryptoStreamMode.Write
        );
        await source.CopyToAsync(crypto, cancellationToken).ConfigureAwait(false);
        await crypto.FlushFinalBlockAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup of the temp upload copy.
        }
    }
}
