namespace StorkDrop.Contracts.Interfaces;

public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string encryptedText);
}
