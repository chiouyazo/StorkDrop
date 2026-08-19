using System.IO.Compression;
using Amazon.S3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Registry.S3;

namespace StorkDrop.Registry.S3.IntegrationTests;

/// <summary>Encryption service that stores/returns values verbatim, so tests can use plaintext secrets.</summary>
internal sealed class PassthroughEncryptionService : IEncryptionService
{
    public string Encrypt(string plainText) => plainText;

    public string Decrypt(string encryptedText) => encryptedText;
}

/// <summary>Configuration service that returns a fixed configuration; only LoadAsync is exercised.</summary>
internal sealed class FixedConfigurationService : IConfigurationService
{
    private readonly AppConfiguration _configuration;

    public FixedConfigurationService(AppConfiguration configuration) =>
        _configuration = configuration;

    public Task<AppConfiguration?> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<AppConfiguration?>(_configuration);

    public Task SaveAsync(
        AppConfiguration configuration,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task ExportAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ImportAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public bool ConfigurationExists() => true;
}

internal static class TestData
{
    public static ProductManifest Manifest(
        string productId,
        string version,
        string? badge = null
    ) =>
        new ProductManifest(
            ProductId: productId,
            Title: $"Title {productId}",
            Version: version,
            ReleaseDate: new DateOnly(2026, 1, 1),
            InstallType: InstallType.Suite,
            Description: $"Description for {productId} {version}",
            BadgeText: badge
        );

    /// <summary>Builds a real (small) zip whose bytes are deterministic for a given content string.</summary>
    public static byte[] Zip(string entryContent)
    {
        using MemoryStream memory = new MemoryStream();
        using (ZipArchive archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("content.txt");
            using StreamWriter writer = new StreamWriter(entry.Open());
            writer.Write(entryContent);
        }
        return memory.ToArray();
    }

    public static S3RegistryClient Client(
        IAmazonS3 s3,
        string bucket,
        string channel,
        string? prefix = null
    ) =>
        new S3RegistryClient(
            s3,
            bucket,
            new S3Layout(prefix),
            channel,
            NullLoggerFactory.Instance.CreateLogger<S3RegistryClient>()
        );

    public static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using MemoryStream memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }
}
