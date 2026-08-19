namespace StorkDrop.Contracts.Models;

/// <summary>
/// The storage backend a feed talks to. Nexus is the original HTTP raw-repository backend; S3 is an
/// object-storage backend (AWS S3 or any S3-compatible service such as MinIO, Cloudflare R2, Wasabi);
/// Local is a folder on disk for developer sideloading.
/// </summary>
public enum FeedProvider
{
    Nexus = 0,
    S3 = 1,
    Local = 2,
}
