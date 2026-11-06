using System.IO;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Demo.Sandbox;

namespace StorkDrop.Demo.Services;

internal sealed class DemoIntegrityService : IIntegrityService
{
    public async Task<IntegrityReport> VerifyAsync(
        InstalledProduct product,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        string uniqueId = product.InstanceUniqueId ?? string.Empty;
        List<TrackedFile> tracked =
            await DemoPackage.ReadFileManifestAsync(product.ProductId, uniqueId, cancellationToken)
            ?? [];

        List<FileIntegrityEntry> entries = [];
        for (int i = 0; i < tracked.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TrackedFile file = tracked[i];
            string fullPath = Path.Combine(product.InstalledPath, file.Path);
            FileIntegrityStatus status;
            if (!File.Exists(fullPath))
                status = FileIntegrityStatus.Missing;
            else if (file.Sha256 is null)
                status = FileIntegrityStatus.Unverifiable;
            else
                status = string.Equals(
                    await FileHasher.ComputeSha256Async(fullPath, cancellationToken),
                    file.Sha256,
                    StringComparison.OrdinalIgnoreCase
                )
                    ? FileIntegrityStatus.Ok
                    : FileIntegrityStatus.Modified;

            entries.Add(new FileIntegrityEntry(file.Path, status));
            progress?.Report((i + 1) * 100 / Math.Max(1, tracked.Count));
            await Task.Delay(250, cancellationToken);
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
        int repaired = 0;
        foreach (string relative in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = Path.Combine(product.InstalledPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(
                fullPath,
                DemoPackage.Content(product.ProductId, product.Version, relative),
                cancellationToken
            );
            repaired++;
            progress?.Report(repaired * 100 / relativePaths.Count);
            await Task.Delay(300, cancellationToken);
        }

        return repaired;
    }
}
