using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Demo.Services;

internal sealed class DemoEncryptionService : IEncryptionService
{
    public string Encrypt(string plainText) => plainText;

    public string Decrypt(string encryptedText) => encryptedText;
}
