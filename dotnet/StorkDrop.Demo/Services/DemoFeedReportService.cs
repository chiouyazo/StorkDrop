using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Demo.Services;

internal sealed class DemoFeedReportService : IFeedReportService
{
    public Task NotifyFeedChangedAsync(
        string? feedId,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
