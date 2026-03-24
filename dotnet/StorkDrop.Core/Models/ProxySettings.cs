namespace StorkDrop.Core.Models;

public sealed record ProxySettings(
    string Host,
    int Port,
    string? Username = null,
    string? EncryptedPassword = null
);
