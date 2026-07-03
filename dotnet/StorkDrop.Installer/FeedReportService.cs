using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CloudNative.CloudEvents;
using CloudNative.CloudEvents.SystemTextJson;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

/// <summary>
/// Builds a per-feed inventory snapshot and delivers it to the feed's configured report endpoint.
/// Wire format: a CloudEvents 1.0 structured-mode JSON event (type
/// "com.storkdrop.inventory.report", with the <see cref="FeedReport"/> as its <c>data</c>),
/// HTTP POSTed to <c>FeedConfiguration.ReportUrl</c> with content type <c>application/json</c> and
/// signed with HMAC-SHA256 over the body (keyed with the feed's report secret) in the
/// <c>X-Signature</c> header (format <c>sha256=&lt;hex&gt;</c>). Delivery uses a resilient on-disk
/// spool with retry, so reports survive offline periods and restarts.
/// </summary>
public sealed class FeedReportService : IFeedReportService
{
    private const string EventType = "com.storkdrop.inventory.report";
    private const string SignatureHeader = "X-Signature";
    private const string CloudEventContentType = "application/json";

    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    private static readonly JsonEventFormatter EventFormatter = new JsonEventFormatter(
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
        default
    );

    private readonly IConfigurationService _configurationService;
    private readonly IProductRepository _productRepository;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<FeedReportService> _logger;
    private readonly SemaphoreSlim _flushLock = new SemaphoreSlim(1, 1);

    public FeedReportService(
        IConfigurationService configurationService,
        IProductRepository productRepository,
        IEncryptionService encryptionService,
        ILogger<FeedReportService> logger
    )
    {
        _configurationService = configurationService;
        _productRepository = productRepository;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task NotifyFeedChangedAsync(
        string? feedId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(feedId))
            return;

        try
        {
            AppConfiguration? config = await _configurationService.LoadAsync(cancellationToken);
            FeedConfiguration[] feeds = config?.Feeds ?? [];

            FeedConfiguration? feed = FeedLock.ResolveFeed(feeds, feedId);
            if (feed is null || string.IsNullOrWhiteSpace(feed.ReportUrl))
                return;

            IReadOnlyList<InstalledProduct> installed = await _productRepository.GetAllAsync(
                cancellationToken
            );

            List<FeedReportProduct> products = SelectFeedProducts(feeds, installed, feed);

            FeedReport report = new FeedReport(
                MachineId: MachineIdentity.MachineId,
                Hostname: MachineIdentity.Hostname,
                OperatingSystem: MachineIdentity.OperatingSystem,
                StorkDropVersion: MachineIdentity.StorkDropVersion,
                SentAt: DateTimeOffset.UtcNow,
                FeedId: feed.Id,
                FeedName: feed.Name,
                CustomerId: string.IsNullOrWhiteSpace(feed.ReportCustomerId)
                    ? null
                    : feed.ReportCustomerId,
                Products: products
            );

            byte[] body = EncodeCloudEvent(report, feed.Id);
            string signature = Sign(body, DecryptSecret(feed));

            Enqueue(
                new SpooledReport(
                    Url: feed.ReportUrl!,
                    Signature: signature,
                    ContentType: CloudEventContentType,
                    Body: Convert.ToBase64String(body)
                )
            );

            _ = Task.Run(() => FlushAsync(CancellationToken.None), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to queue feed report for {FeedId}", feedId);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(StorkPaths.FeedReportSpoolDir))
            return;

        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string[] files = Directory
                .GetFiles(StorkPaths.FeedReportSpoolDir, "*.json")
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SpooledReport? item;
                try
                {
                    string json = await File.ReadAllTextAsync(file, cancellationToken)
                        .ConfigureAwait(false);
                    item = JsonSerializer.Deserialize<SpooledReport>(json);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Dropping corrupt spooled report {File}", file);
                    TryDelete(file);
                    continue;
                }

                if (item is null)
                {
                    TryDelete(file);
                    continue;
                }

                try
                {
                    using HttpRequestMessage request = new HttpRequestMessage(
                        HttpMethod.Post,
                        item.Url
                    );
                    ByteArrayContent content = new ByteArrayContent(
                        Convert.FromBase64String(item.Body)
                    );
                    content.Headers.ContentType = new MediaTypeHeaderValue(item.ContentType);
                    request.Content = content;
                    if (!string.IsNullOrEmpty(item.Signature))
                        request.Headers.TryAddWithoutValidation(SignatureHeader, item.Signature);

                    using HttpResponseMessage response = await Http.SendAsync(
                            request,
                            cancellationToken
                        )
                        .ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        TryDelete(file);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Feed report POST to {Url} returned {Status}; will retry later",
                            item.Url,
                            (int)response.StatusCode
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Feed report delivery to {Url} failed; will retry later",
                        item.Url
                    );
                }
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private string DecryptSecret(FeedConfiguration feed)
    {
        if (string.IsNullOrEmpty(feed.EncryptedReportSecret))
            return string.Empty;

        try
        {
            return _encryptionService.Decrypt(feed.EncryptedReportSecret);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt report secret for feed {FeedId}", feed.Id);
            return string.Empty;
        }
    }

    /// <summary>
    /// Selects the installed products that belong to <paramref name="feed"/>, resolving each
    /// product's (possibly composite) feed id back to its base configured feed.
    /// </summary>
    internal static List<FeedReportProduct> SelectFeedProducts(
        FeedConfiguration[] feeds,
        IReadOnlyList<InstalledProduct> installed,
        FeedConfiguration feed
    )
    {
        return installed
            .Where(p =>
                string.Equals(
                    FeedLock.ResolveFeed(feeds, p.FeedId)?.Id,
                    feed.Id,
                    StringComparison.Ordinal
                )
            )
            .Select(p => new FeedReportProduct(
                p.ProductId,
                p.Title,
                p.Version,
                p.FeedId,
                p.InstanceId,
                p.InstalledDate
            ))
            .ToList();
    }

    internal static byte[] EncodeCloudEvent(FeedReport report, string feedId)
    {
        CloudEvent cloudEvent = new CloudEvent
        {
            Id = Guid.NewGuid().ToString(),
            Type = EventType,
            Source = new Uri($"storkdrop://{report.MachineId}"),
            Subject = feedId,
            Time = report.SentAt,
            DataContentType = "application/json",
            Data = report,
        };

        ReadOnlyMemory<byte> encoded = EventFormatter.EncodeStructuredModeMessage(
            cloudEvent,
            out _
        );
        return encoded.ToArray();
    }

    internal static string Sign(byte[] body, string secret)
    {
        if (string.IsNullOrEmpty(secret))
            return string.Empty;

        using HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(body);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void Enqueue(SpooledReport report)
    {
        Directory.CreateDirectory(StorkPaths.FeedReportSpoolDir);
        string fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json";
        string path = Path.Combine(StorkPaths.FeedReportSpoolDir, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(report));
    }

    private void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete spooled report {File}", file);
        }
    }
}
