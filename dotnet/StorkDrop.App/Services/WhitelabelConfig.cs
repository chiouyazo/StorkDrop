using System.IO;
using System.Text.Json;
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
            if (
                file.Feed is not null
                && (
                    !string.IsNullOrWhiteSpace(file.Feed.Name)
                    || !string.IsNullOrWhiteSpace(file.Feed.Url)
                )
            )
            {
                feed = new BrandingFeed(
                    Trimmed(file.Feed.Name),
                    Trimmed(file.Feed.Url),
                    Trimmed(file.Feed.LockPasswordHash)
                );
            }

            return new BrandingInfo(
                Prefix: Trimmed(file.Prefix),
                DisplayName: Trimmed(file.DisplayName),
                LogoPath: logoPath,
                ForbidNewFeeds: file.ForbidNewFeeds,
                Feed: feed
            );
        }
        catch
        {
            return BrandingInfo.Default;
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class WhitelabelFile
    {
        public string? Prefix { get; set; }

        public string? DisplayName { get; set; }

        public string? Logo { get; set; }

        public bool ForbidNewFeeds { get; set; }

        public FeedSection? Feed { get; set; }
    }

    private sealed class FeedSection
    {
        public string? Name { get; set; }

        public string? Url { get; set; }

        public string? LockPasswordHash { get; set; }
    }
}
