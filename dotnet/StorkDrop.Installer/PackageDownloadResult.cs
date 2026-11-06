namespace StorkDrop.Installer;

internal sealed record PackageDownloadResult(long Bytes, string Sha256, bool Verified);
