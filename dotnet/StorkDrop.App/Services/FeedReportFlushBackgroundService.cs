using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.App.Services;

/// <summary>
/// Periodically drains the feed-report spool so queued reports are retried until delivered.
/// On-change reports are delivered immediately; this is the safety net for anything that
/// failed (offline, endpoint down) and must be retried later.
/// </summary>
public sealed class FeedReportFlushBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IFeedReportService _feedReportService;
    private readonly ILogger<FeedReportFlushBackgroundService> _logger;

    public FeedReportFlushBackgroundService(
        IFeedReportService feedReportService,
        ILogger<FeedReportFlushBackgroundService> logger
    )
    {
        _feedReportService = feedReportService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using PeriodicTimer timer = new PeriodicTimer(Interval);
        while (true)
        {
            try
            {
                await _feedReportService.FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Feed report flush cycle failed");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
