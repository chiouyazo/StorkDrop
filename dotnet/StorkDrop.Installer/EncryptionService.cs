using System.Security.Cryptography;
using System.Text;
using StorkDrop.Core.Interfaces;

namespace StorkDrop.Installer;

/// <summary>
/// Provides encryption and decryption of sensitive strings.
/// On Windows, uses DPAPI (<see cref="ProtectedData"/>) scoped to the current user.
/// On non-Windows platforms, uses AES with a machine-specific key derived from the machine name.
/// </summary>
public sealed class EncryptionService : IEncryptionService
{
    /// <summary>
    /// Encrypts the given plain text string.
    /// Returns an empty string if the input is null or empty.
    /// </summary>
    /// <param name="plainText">The text to encrypt.</param>
    /// <returns>A Base64-encoded encrypted string.</returns>
    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        if (OperatingSystem.IsWindows())
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encryptedBytes = ProtectedData.Protect(
                plainBytes,
                null,
                DataProtectionScope.CurrentUser
            );
            return Convert.ToBase64String(encryptedBytes);
        }

        // Non-Windows fallback: AES with machine-specific key
        return AesEncrypt(plainText);
    }

    /// <summary>
    /// Decrypts the given encrypted text string.
    /// Returns an empty string if the input is null or empty.
    /// </summary>
    /// <param name="encryptedText">A Base64-encoded encrypted string.</param>
    /// <returns>The decrypted plain text.</returns>
    /// <exception cref="CryptographicException">
    /// Thrown when the Base64 string is invalid or decryption fails.
    /// </exception>
    public string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return string.Empty;

        if (OperatingSystem.IsWindows())
        {
            byte[] encryptedBytes;
            try
            {
                encryptedBytes = Convert.FromBase64String(encryptedText);
            }
            catch (FormatException ex)
            {
                throw new CryptographicException(
                    "Entschlüsselung fehlgeschlagen: Der verschlüsselte Text ist kein gültiger Base64-String.",
                    ex
                );
            }

            try
            {
                byte[] plainBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    null,
                    DataProtectionScope.CurrentUser
                );
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException(
                    "Entschlüsselung fehlgeschlagen: Die Daten konnten nicht entschlüsselt werden. "
                        + "Möglicherweise wurden sie von einem anderen Benutzer oder Computer verschlüsselt.",
                    ex
                );
            }
        }

        // Non-Windows fallback: AES with machine-specific key
        return AesDecrypt(encryptedText);
    }

    /// <summary>
    /// Derives a 256-bit AES key from the machine name using PBKDF2.
    /// </summary>
    private static byte[] DeriveAesKey()
    {
        string machineName = Environment.MachineName;
        byte[] salt = "StorkDrop-AES-Salt"u8.ToArray();
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(machineName),
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32
        );
    }

    /// <summary>
    /// Encrypts plain text using AES-256-CBC with a machine-specific key.
    /// The IV is prepended to the ciphertext before Base64 encoding.
    /// </summary>
    private static string AesEncrypt(string plainText)
    {
        byte[] key = DeriveAesKey();
        using Aes aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using ICryptoTransform encryptor = aes.CreateEncryptor();
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend IV to ciphertext
        byte[] result = new byte[aes.IV.Length + cipherBytes.Length];
        aes.IV.CopyTo(result, 0);
        cipherBytes.CopyTo(result, aes.IV.Length);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypts AES-256-CBC encrypted text. Expects the IV prepended to the ciphertext, Base64-encoded.
    /// </summary>
    private static string AesDecrypt(string encryptedText)
    {
        byte[] combined;
        try
        {
            combined = Convert.FromBase64String(encryptedText);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException(
                "Entschlüsselung fehlgeschlagen: Der verschlüsselte Text ist kein gültiger Base64-String.",
                ex
            );
        }

        byte[] key = DeriveAesKey();
        using Aes aes = Aes.Create();
        aes.Key = key;

        if (combined.Length < aes.BlockSize / 8)
            throw new CryptographicException(
                "Entschlüsselung fehlgeschlagen: Die verschlüsselten Daten sind zu kurz."
            );

        int ivLength = aes.BlockSize / 8;
        byte[] iv = combined[..ivLength];
        byte[] cipherBytes = combined[ivLength..];

        aes.IV = iv;

        try
        {
            using ICryptoTransform decryptor = aes.CreateDecryptor();
            byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException(
                "Entschlüsselung fehlgeschlagen: Die Daten konnten nicht entschlüsselt werden.",
                ex
            );
        }
    }
}
