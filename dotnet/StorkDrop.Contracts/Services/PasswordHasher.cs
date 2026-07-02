using System.Security.Cryptography;
using System.Text;

namespace StorkDrop.Contracts.Services;

/// <summary>
/// Salted PBKDF2 hashing for the optional per-feed operation lock ("soft lock").
/// This is intentionally a lightweight barrier, not a hardened secret store: the
/// encoded hash lives in the local config file and is offline-verifiable by design.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Produces a salted PBKDF2 hash of <paramref name="password"/>, encoded as Base64
    /// (16-byte salt followed by the 32-byte derived key).
    /// </summary>
    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            Algorithm,
            HashSize
        );

        byte[] combined = new byte[SaltSize + HashSize];
        Buffer.BlockCopy(salt, 0, combined, 0, SaltSize);
        Buffer.BlockCopy(hash, 0, combined, SaltSize, HashSize);
        return Convert.ToBase64String(combined);
    }

    /// <summary>
    /// Verifies <paramref name="password"/> against a hash produced by <see cref="Hash"/>.
    /// Returns false for null/empty/malformed input rather than throwing.
    /// </summary>
    public static bool Verify(string? password, string? encodedHash)
    {
        if (string.IsNullOrEmpty(encodedHash) || password is null)
            return false;

        byte[] combined;
        try
        {
            combined = Convert.FromBase64String(encodedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (combined.Length != SaltSize + HashSize)
            return false;

        byte[] salt = combined[..SaltSize];
        byte[] expected = combined[SaltSize..];
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            Algorithm,
            HashSize
        );

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
