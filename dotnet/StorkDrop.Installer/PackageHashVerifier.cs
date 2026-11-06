using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

public static class PackageHashVerifier
{
    public static string? Normalize(string? checksum) =>
        string.IsNullOrWhiteSpace(checksum) ? null : checksum.Trim().ToLowerInvariant();

    public static bool Matches(string? expected, string actual) =>
        string.Equals(Normalize(expected), Normalize(actual), StringComparison.Ordinal);

    public static async Task<string> VerifyFileAsync(
        string filePath,
        string expectedSha256,
        string description,
        CancellationToken cancellationToken
    )
    {
        string actual = await FileHasher.ComputeSha256Async(filePath, cancellationToken);
        EnsureMatches(expectedSha256, actual, description);
        return actual;
    }

    public static void EnsureMatches(string expected, string actual, string description)
    {
        if (!Matches(expected, actual))
        {
            throw new PackageIntegrityException(
                $"Checksum mismatch for {description}: expected {Normalize(expected)}, downloaded {Normalize(actual)}. The package was not installed."
            );
        }
    }
}
