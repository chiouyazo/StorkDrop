namespace StorkDrop.Installer;

public sealed class PackageIntegrityException(string message) : IOException(message);
