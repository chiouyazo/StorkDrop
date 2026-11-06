using System.IO;
using System.Security.Cryptography;
using System.Text;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Demo.Sandbox;

/// <summary>Generates the small, deterministic files a demo product consists of and tracks them like the real engine.</summary>
internal static class DemoPackage
{
    public static IReadOnlyList<string> RelativePaths(ProductManifest manifest) =>
        [
            $"{ExecutableName(manifest)}.exe",
            "lib/core.dll",
            "lib/data.dll",
            "config/default.json",
            "README.txt",
        ];

    public static string MainFile(ProductManifest manifest) => $"{ExecutableName(manifest)}.exe";

    public static byte[] Content(string productId, string version, string relativePath) =>
        Encoding.UTF8.GetBytes(
            $"StorkDrop demo file{Environment.NewLine}product={productId}{Environment.NewLine}version={version}{Environment.NewLine}file={relativePath}{Environment.NewLine}"
        );

    public static async Task<List<TrackedFile>> WriteAsync(
        ProductManifest manifest,
        string installPath,
        IProgress<string>? onFile,
        TimeSpan perFileDelay,
        CancellationToken cancellationToken
    )
    {
        List<TrackedFile> tracked = [];
        foreach (string relative in RelativePaths(manifest))
        {
            cancellationToken.ThrowIfCancellationRequested();
            onFile?.Report(relative);
            if (perFileDelay > TimeSpan.Zero)
                await Task.Delay(perFileDelay, cancellationToken);

            string fullPath = Path.Combine(installPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            byte[] bytes = Content(manifest.ProductId, manifest.Version, relative);
            await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);
            tracked.Add(
                new TrackedFile(
                    relative,
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                    bytes.Length
                )
            );
        }

        return tracked;
    }

    public static Task WriteFileManifestAsync(
        string productId,
        string uniqueId,
        IReadOnlyList<TrackedFile> files,
        CancellationToken cancellationToken
    ) =>
        FileManifestStore.WriteAsync(
            StorkPaths.FileManifestPath(productId, uniqueId),
            files,
            cancellationToken
        );

    public static Task<List<TrackedFile>?> ReadFileManifestAsync(
        string productId,
        string uniqueId,
        CancellationToken cancellationToken
    ) =>
        FileManifestStore.ReadAsync(
            StorkPaths.FileManifestPath(productId, uniqueId),
            cancellationToken
        );

    private static string ExecutableName(ProductManifest manifest) =>
        string.Concat(manifest.Title.Where(char.IsLetterOrDigit)) is { Length: > 0 } name
            ? name
            : "App";
}
