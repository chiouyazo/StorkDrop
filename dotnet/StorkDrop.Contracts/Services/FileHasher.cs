using System.Security.Cryptography;

namespace StorkDrop.Contracts.Services;

/// <summary>
/// Computes SHA-256 hashes of files for install-time recording and integrity verification.
/// </summary>
public static class FileHasher
{
    public static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken = default
    )
    {
        await using FileStream stream = File.OpenRead(filePath);
        using SHA256 sha = SHA256.Create();
        byte[] hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
