using System.IO.Compression;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

/// <summary>
/// Verifies and repairs installed product files against install-time SHA-256 hashes, scoped strictly
/// to the files recorded in the product's file manifest.
/// </summary>
public sealed class IntegrityService : IIntegrityService
{
    private readonly IFeedRegistry _feedRegistry;
    private readonly ILogger<IntegrityService> _logger;

    public IntegrityService(IFeedRegistry feedRegistry, ILogger<IntegrityService> logger)
    {
        _feedRegistry = feedRegistry;
        _logger = logger;
    }

    public async Task<IntegrityReport> VerifyAsync(
        InstalledProduct product,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        string uniqueId = product.InstanceUniqueId ?? string.Empty;
        List<TrackedFile> tracked = await LoadManifestAsync(
            product.ProductId,
            uniqueId,
            cancellationToken
        );

        List<FileIntegrityEntry> entries = new List<FileIntegrityEntry>(tracked.Count);
        for (int i = 0; i < tracked.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TrackedFile file = tracked[i];
            string fullPath = Path.Combine(product.InstalledPath, file.Path);

            FileIntegrityStatus status = await DetermineStatusAsync(
                file,
                fullPath,
                cancellationToken
            );
            entries.Add(new FileIntegrityEntry(file.Path, status));

            progress?.Report((int)((i + 1) * 100.0 / Math.Max(1, tracked.Count)));
        }

        return new IntegrityReport(product.ProductId, uniqueId, entries);
    }

    public async Task<int> RepairAsync(
        InstalledProduct product,
        IReadOnlyList<string> relativePaths,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(product.FeedId))
        {
            _logger.LogWarning(
                "Cannot repair {ProductId}: no feed recorded for this install",
                product.ProductId
            );
            return 0;
        }

        string uniqueId = product.InstanceUniqueId ?? string.Empty;
        List<TrackedFile> tracked = await LoadManifestAsync(
            product.ProductId,
            uniqueId,
            cancellationToken
        );
        HashSet<string> trackedSet = new HashSet<string>(
            tracked.Select(f => Normalize(f.Path)),
            StringComparer.OrdinalIgnoreCase
        );

        // Only ever restore files that are actually tracked for this product.
        List<string> targets = relativePaths
            .Select(Normalize)
            .Where(trackedSet.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targets.Count == 0)
            return 0;

        string workRoot = Path.Combine(StorkPaths.TempDir, "repair", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);
        try
        {
            string contentDir = await DownloadAndExtractAsync(product, workRoot, cancellationToken);

            int repaired = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = targets[i];
                string source = Path.Combine(contentDir, relative);
                if (File.Exists(source))
                {
                    string destination = Path.Combine(product.InstalledPath, relative);
                    string? destinationDir = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(destinationDir))
                        Directory.CreateDirectory(destinationDir);

                    File.Copy(source, destination, overwrite: true);
                    repaired++;
                    _logger.LogInformation(
                        "Repaired {File} for {ProductId}",
                        relative,
                        product.ProductId
                    );
                }
                else
                {
                    _logger.LogWarning(
                        "Repair source missing for {File}; the version's archive no longer contains it",
                        relative
                    );
                }

                progress?.Report((int)((i + 1) * 100.0 / targets.Count));
            }

            return repaired;
        }
        finally
        {
            TryDelete(workRoot);
        }
    }

    private static async Task<FileIntegrityStatus> DetermineStatusAsync(
        TrackedFile file,
        string fullPath,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(fullPath))
            return FileIntegrityStatus.Missing;

        if (string.IsNullOrEmpty(file.Sha256))
            return FileIntegrityStatus.Unverifiable;

        // Cheap pre-check: a different size is already a mismatch, no need to hash.
        if (file.Size > 0 && new FileInfo(fullPath).Length != file.Size)
            return FileIntegrityStatus.Modified;

        string actual = await FileHasher.ComputeSha256Async(fullPath, cancellationToken);
        return string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase)
            ? FileIntegrityStatus.Ok
            : FileIntegrityStatus.Modified;
    }

    private async Task<string> DownloadAndExtractAsync(
        InstalledProduct product,
        string workRoot,
        CancellationToken cancellationToken
    )
    {
        IRegistryClient client = _feedRegistry.GetClient(product.FeedId!);

        string zipPath = Path.Combine(workRoot, "package.zip");
        await using (
            Stream downloadStream = await client.DownloadProductAsync(
                product.ProductId,
                product.Version,
                cancellationToken
            )
        )
        await using (FileStream fileStream = File.Create(zipPath))
        {
            await downloadStream.CopyToAsync(fileStream, cancellationToken);
        }

        string extractPath = Path.Combine(workRoot, "extracted");
        ZipFile.ExtractToDirectory(zipPath, extractPath);

        // Two-layer packaging: a single inner .zip holds the actual product files.
        string[] innerZips = Directory.GetFiles(extractPath, "*.zip");
        if (innerZips.Length == 1)
        {
            string contentPath = Path.Combine(workRoot, "content");
            ZipFile.ExtractToDirectory(innerZips[0], contentPath);
            return contentPath;
        }

        return extractPath;
    }

    private static async Task<List<TrackedFile>> LoadManifestAsync(
        string productId,
        string uniqueId,
        CancellationToken cancellationToken
    )
    {
        string path = StorkPaths.FileManifestPath(productId, uniqueId);
        if (!File.Exists(path))
        {
            string legacyPath = StorkPaths.LegacyFileManifestPath(productId);
            if (File.Exists(legacyPath))
                path = legacyPath;
            else
                return new List<TrackedFile>();
        }

        List<TrackedFile>? entries = await FileManifestStore.ReadAsync(path, cancellationToken);
        return entries ?? new List<TrackedFile>();
    }

    private static string Normalize(string relativePath) =>
        relativePath.Replace('/', '\\').TrimStart('\\');

    private void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not clean up repair temp dir {Dir}", directory);
        }
    }
}
