using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Registry.S3;

namespace StorkDrop.Publisher;

/// <summary>
/// Publishes products into a StorkDrop S3 bucket using the same layout as a Nexus raw repository (see
/// <see cref="S3Layout"/>), with the channel as the top-level prefix. A publish validates the manifest,
/// uploads the version manifest (with the package SHA-256 embedded in
/// <see cref="ProductManifest.ContentSha256"/>) and the package, then replaces the product's root
/// <c>manifest.json</c> only if the published version is not lower than the current root version
/// (or the publish is forced). The root manifest is written conditionally on its ETag so concurrent
/// publishers cannot overwrite each other. There are no index/catalog/pointer objects: clients discover
/// by listing, so what a customer can see is governed purely by the IAM rights on the channel/product
/// prefixes.
/// </summary>
public sealed class S3Publisher
{
    private const int MaxLatestAttempts = 5;

    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly S3Layout _layout;
    private readonly ManifestValidator _validator = new ManifestValidator();

    public S3Publisher(IAmazonS3 s3, string bucket, string? prefix = null)
    {
        _s3 = s3;
        _bucket = bucket;
        _layout = new S3Layout(prefix);
    }

    /// <summary>
    /// Publishes one product version into a channel. The result carries the manifest actually stored (with
    /// <see cref="ProductManifest.ContentSha256"/> populated from the package) and whether the root
    /// "latest" manifest was replaced. <paramref name="forceLatest"/> replaces it regardless of version.
    /// </summary>
    public async Task<PublishResult> PublishAsync(
        string channel,
        ProductManifest manifest,
        Stream packageZip,
        bool forceLatest = false,
        CancellationToken cancellationToken = default
    )
    {
        S3Names.Require(channel, nameof(channel));
        string productId = S3Names.Require(manifest.ProductId, "productId");
        string version = S3Names.Require(manifest.Version, "version");

        ManifestValidationResult validation = _validator.Validate(manifest);
        if (!validation.IsValid)
        {
            throw new ArgumentException(
                $"Manifest is invalid: {string.Join(" ", validation.Errors)}",
                nameof(manifest)
            );
        }

        string tempFile = Path.Combine(
            Path.GetTempPath(),
            $"storkdrop-publish-{Guid.NewGuid():N}.zip"
        );
        try
        {
            string hash = await CopyAndHashAsync(packageZip, tempFile, cancellationToken);
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
            );

            await PutJsonAsync(
                _layout.VersionManifestKey(channel, productId, version),
                stored,
                cancellationToken
            );

            (bool latestUpdated, string? previousLatestVersion) = await UpdateLatestAsync(
                channel,
                stored,
                forceLatest,
                cancellationToken
            );

            return new PublishResult(stored, latestUpdated, previousLatestVersion);
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    private async Task<(bool Updated, string? PreviousVersion)> UpdateLatestAsync(
        string channel,
        ProductManifest published,
        bool force,
        CancellationToken cancellationToken
    )
    {
        string key = _layout.LatestManifestKey(channel, published.ProductId);

        for (int attempt = 0; attempt < MaxLatestAttempts; attempt++)
        {
            (string? etag, ProductManifest? current) = await ReadLatestAsync(
                key,
                cancellationToken
            );

            if (!LatestManifestPolicy.ShouldReplace(current, published, force))
                return (false, current?.Version);

            PutObjectRequest request = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                ContentBody = JsonSerializer.Serialize(published, S3Json.Options),
                ContentType = "application/json",
            };
            if (etag is null)
                request.IfNoneMatch = "*";
            else
                request.IfMatch = etag;

            try
            {
                await _s3.PutObjectAsync(request, cancellationToken);
                return (true, current?.Version);
            }
            catch (AmazonS3Exception ex)
                when (ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
            {
                continue;
            }
        }

        throw new InvalidOperationException(
            $"The root manifest of {published.ProductId} in channel '{channel}' kept changing "
                + $"during {MaxLatestAttempts} attempts. The version manifest and package were uploaded; "
                + "publish again to update 'latest'."
        );
    }

    private async Task<(string? ETag, ProductManifest? Manifest)> ReadLatestAsync(
        string key,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using GetObjectResponse response = await _s3.GetObjectAsync(
                new GetObjectRequest { BucketName = _bucket, Key = key },
                cancellationToken
            );
            using StreamReader reader = new StreamReader(response.ResponseStream);
            string json = await reader.ReadToEndAsync(cancellationToken);
            try
            {
                return (
                    response.ETag,
                    JsonSerializer.Deserialize<ProductManifest>(json, S3Json.Options)
                );
            }
            catch (JsonException)
            {
                return (response.ETag, null);
            }
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, null);
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
        await source.CopyToAsync(crypto, cancellationToken);
        await crypto.FlushFinalBlockAsync(cancellationToken);
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
