using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

public sealed class GitHubUpdateChecker : ISelfUpdateChecker
{
    private const string GitHubRepo = "chiouyazo/StorkDrop";
    private const string GitHubApiBase = "https://api.github.com";
    private const string SetupExePattern = "-Setup.exe";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<GitHubUpdateChecker> _logger;

    public GitHubUpdateChecker(ILogger<GitHubUpdateChecker> logger)
    {
        _logger = logger;
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(
        bool includeDevVersions,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("StorkDrop-UpdateChecker");
            client.Timeout = TimeSpan.FromSeconds(15);

            GitHubRelease? release = includeDevVersions
                ? await GetLatestIncludingPrereleasesAsync(client, cancellationToken)
                : await GetLatestStableAsync(client, cancellationToken);

            if (release is null)
            {
                _logger.LogDebug("No release found on GitHub");
                return null;
            }

            string remoteVersion = release.TagName.TrimStart('v');
            string? currentVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();

            if (string.IsNullOrEmpty(currentVersion))
            {
                _logger.LogWarning("Could not determine current app version");
                return null;
            }

            // Strip the 4th segment (.0) if present for comparison
            if (currentVersion.Split('.').Length == 4 && currentVersion.EndsWith(".0"))
                currentVersion = currentVersion[..currentVersion.LastIndexOf('.')];

            VersionComparer comparer = new();
            int comparison = comparer.Compare(remoteVersion, currentVersion);
            if (comparison <= 0)
            {
                _logger.LogDebug(
                    "Current version {Current} is up-to-date (remote: {Remote})",
                    currentVersion,
                    remoteVersion
                );
                return null;
            }

            string? setupUrl = release
                .Assets?.FirstOrDefault(a =>
                    a.Name.Contains(SetupExePattern, StringComparison.OrdinalIgnoreCase)
                )
                ?.BrowserDownloadUrl;

            if (setupUrl is null)
            {
                _logger.LogWarning(
                    "Release {Version} found but no Setup.exe asset available",
                    remoteVersion
                );
                return null;
            }

            _logger.LogInformation(
                "Update available: {Current} -> {Remote}",
                currentVersion,
                remoteVersion
            );

            return new UpdateInfo(
                Version: remoteVersion,
                DownloadUrl: setupUrl,
                ReleaseNotes: release.Body,
                ReleaseDate: release.PublishedAt
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for StorkDrop updates");
            return null;
        }
    }

    private static async Task<GitHubRelease?> GetLatestStableAsync(
        HttpClient client,
        CancellationToken ct
    )
    {
        string url = $"{GitHubApiBase}/repos/{GitHubRepo}/releases/latest";
        HttpResponseMessage response = await client.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<GitHubRelease>(JsonOptions, ct);
    }

    private static async Task<GitHubRelease?> GetLatestIncludingPrereleasesAsync(
        HttpClient client,
        CancellationToken ct
    )
    {
        string url = $"{GitHubApiBase}/repos/{GitHubRepo}/releases?per_page=10";
        HttpResponseMessage response = await client.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
            return null;

        GitHubRelease[]? releases = await response.Content.ReadFromJsonAsync<GitHubRelease[]>(
            JsonOptions,
            ct
        );

        if (releases is null or { Length: 0 })
            return null;

        return releases.OrderByDescending(r => r.PublishedAt).First();
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
