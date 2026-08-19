using System.IO;
using System.Linq;
using System.Text.Json;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.Services;

/// <summary>
/// Reads the optional <c>whitelabel.json</c> that sits next to the executable and turns it into a
/// <see cref="BrandingInfo"/>. A missing or unreadable file yields <see cref="BrandingInfo.Default"/>,
/// so an unbranded install behaves exactly as before.
/// </summary>
internal static class WhitelabelConfig
{
    private const string FileName = "whitelabel.json";

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    public static BrandingInfo Load(string installDirectory)
    {
        try
        {
            string path = Path.Combine(installDirectory, FileName);
            if (!File.Exists(path))
                return BrandingInfo.Default;

            using FileStream stream = File.OpenRead(path);
            WhitelabelFile? file = JsonSerializer.Deserialize<WhitelabelFile>(stream, Options);
            if (file is null)
                return BrandingInfo.Default;

            string? logoPath = string.IsNullOrWhiteSpace(file.Logo)
                ? null
                : Path.Combine(installDirectory, file.Logo);

            BrandingFeed? feed = null;
            if (file.Feed is not null)
            {
                FeedProvider provider = ParseProvider(file.Feed.Provider);
                BrandingS3? s3 = null;
                if (
                    provider == FeedProvider.S3
                    && file.Feed.S3 is not null
                    && !string.IsNullOrWhiteSpace(file.Feed.S3.Bucket)
                )
                {
                    s3 = new BrandingS3(
                        Bucket: file.Feed.S3.Bucket!.Trim(),
                        Region: Trimmed(file.Feed.S3.Region),
                        ServiceUrl: Trimmed(file.Feed.S3.ServiceUrl),
                        UsePathStyle: file.Feed.S3.UsePathStyle,
                        Prefix: Trimmed(file.Feed.S3.Prefix),
                        Channels: NormalizeChannels(file.Feed.S3.Channels)
                    );
                }

                bool hasIdentity =
                    !string.IsNullOrWhiteSpace(file.Feed.Name)
                    || !string.IsNullOrWhiteSpace(file.Feed.Url)
                    || s3 is not null;

                if (hasIdentity)
                {
                    feed = new BrandingFeed(
                        Trimmed(file.Feed.Name),
                        Trimmed(file.Feed.Url),
                        Trimmed(file.Feed.LockPasswordHash),
                        provider,
                        s3
                    );
                }
            }

            return new BrandingInfo(
                Prefix: Trimmed(file.Prefix),
                DisplayName: Trimmed(file.DisplayName),
                LogoPath: logoPath,
                ForbidNewFeeds: file.ForbidNewFeeds,
                Feed: feed,
                VisibleChannels: NormalizeChannels(file.VisibleChannels)
            );
        }
        catch
        {
            return BrandingInfo.Default;
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static FeedProvider ParseProvider(string? value) =>
        string.Equals(value, "s3", StringComparison.OrdinalIgnoreCase)
            ? FeedProvider.S3
            : FeedProvider.Nexus;

    private static string[]? NormalizeChannels(string[]? channels)
    {
        if (channels is null)
            return null;

        string[] cleaned = channels
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToArray();
        return cleaned.Length > 0 ? cleaned : null;
    }

    private sealed class WhitelabelFile
    {
        public string? Prefix { get; set; }

        public string? DisplayName { get; set; }

        public string? Logo { get; set; }

        public bool ForbidNewFeeds { get; set; }

        public FeedSection? Feed { get; set; }

        public string[]? VisibleChannels { get; set; }
    }

    private sealed class FeedSection
    {
        public string? Name { get; set; }

        public string? Url { get; set; }

        public string? LockPasswordHash { get; set; }

        public string? Provider { get; set; }

        public S3Section? S3 { get; set; }
    }

    private sealed class S3Section
    {
        public string? Bucket { get; set; }

        public string? Region { get; set; }

        public string? ServiceUrl { get; set; }

        public bool UsePathStyle { get; set; }

        public string? Prefix { get; set; }

        public string[]? Channels { get; set; }
    }
}
