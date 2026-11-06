using System.Net;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

/// <summary>
/// Verifies a downloaded file against the <c>SHA256SUMS.txt</c> published next to it
/// (<c>&lt;hex&gt;  &lt;file name&gt;</c> per line).
/// </summary>
public static class PublishedChecksumVerifier
{
    public const string ChecksumFileName = "SHA256SUMS.txt";

    public static Uri ChecksumUrlFor(Uri downloadUrl) => new Uri(downloadUrl, ChecksumFileName);

    public static string? FindChecksum(string checksumFileContent, string fileName)
    {
        foreach (string rawLine in checksumFileContent.Split('\n'))
        {
            string line = rawLine.Trim().TrimStart('﻿');
            int separator = line.IndexOfAny([' ', '\t']);
            if (separator <= 0)
                continue;

            string hash = line[..separator];
            string name = line[separator..].Trim().TrimStart('*');
            if (
                hash.Length == 64
                && hash.All(Uri.IsHexDigit)
                && string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)
            )
                return hash.ToLowerInvariant();
        }

        return null;
    }

    /// <returns>True when the file was verified; false when no checksum could be obtained.</returns>
    /// <exception cref="PackageIntegrityException">The file does not match the published checksum.</exception>
    public static async Task<bool> VerifyAsync(
        HttpClient client,
        Uri downloadUrl,
        string localFile,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        string fileName = Uri.UnescapeDataString(Path.GetFileName(downloadUrl.AbsolutePath));
        Uri checksumUrl = ChecksumUrlFor(downloadUrl);

        string content;
        try
        {
            using HttpResponseMessage response = await client.GetAsync(
                checksumUrl,
                cancellationToken
            );
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogWarning(
                    "No {File} published next to {Download}; the download could NOT be verified",
                    ChecksumFileName,
                    fileName
                );
                return false;
            }

            response.EnsureSuccessStatusCode();
            content = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "{File} could not be fetched; the download could NOT be verified",
                ChecksumFileName
            );
            return false;
        }

        string? expected = FindChecksum(content, fileName);
        if (expected is null)
        {
            logger.LogWarning(
                "{File} lists no checksum for {Download}; the download could NOT be verified",
                ChecksumFileName,
                fileName
            );
            return false;
        }

        await PackageHashVerifier.VerifyFileAsync(
            localFile,
            expected,
            $"download {fileName}",
            cancellationToken
        );
        logger.LogInformation("Verified {Download} against {File}", fileName, ChecksumFileName);
        return true;
    }
}
