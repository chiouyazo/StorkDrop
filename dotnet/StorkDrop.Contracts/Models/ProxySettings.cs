namespace StorkDrop.Contracts.Models;

public sealed record ProxySettings(
    string Host,
    int Port,
    string? Username = null,
    string? EncryptedPassword = null
);
