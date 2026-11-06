namespace StorkDrop.Installer;

public sealed class PackageDownloadException(string message, Exception innerException)
    : IOException(message, innerException);
