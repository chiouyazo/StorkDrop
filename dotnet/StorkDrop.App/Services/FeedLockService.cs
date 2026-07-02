using Microsoft.Extensions.Logging;
using StorkDrop.App.Localization;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.Services;

/// <inheritdoc />
public sealed class FeedLockService : IFeedLockService
{
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<FeedLockService> _logger;

    public FeedLockService(
        IConfigurationService configurationService,
        ILogger<FeedLockService> logger
    )
    {
        _configurationService = configurationService;
        _logger = logger;
    }

    public FeedUnlockScope CreateScope() => new FeedUnlockScope();

    public async Task<bool> IsLockedAsync(string? feedId)
    {
        FeedConfiguration? feed = await ResolveAsync(feedId);
        return FeedLock.IsLocked(feed);
    }

    public async Task<bool> EnsureAuthorizedAsync(
        string? feedId,
        string operationName,
        FeedUnlockScope? scope = null
    )
    {
        FeedConfiguration? feed = await ResolveAsync(feedId);
        if (!FeedLock.IsLocked(feed))
            return true;

        string baseId = feed!.Id;
        if (scope is not null && scope.UnlockedBaseFeedIds.Contains(baseId))
            return true;

        bool authorized = Prompt(feed, operationName);
        if (authorized)
            scope?.UnlockedBaseFeedIds.Add(baseId);
        return authorized;
    }

    private async Task<FeedConfiguration?> ResolveAsync(string? feedId)
    {
        if (string.IsNullOrEmpty(feedId))
            return null;

        try
        {
            AppConfiguration? config = await _configurationService.LoadAsync();
            return FeedLock.ResolveFeed(config?.Feeds ?? [], feedId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load configuration for feed lock check");
            return null;
        }
    }

    private static bool Prompt(FeedConfiguration feed, string operationName)
    {
        System.Windows.Application? app = System.Windows.Application.Current;
        if (app?.Dispatcher is null)
            return false;

        return app.Dispatcher.Invoke(() =>
        {
            string? error = null;
            while (true)
            {
                Views.FeedLockPromptDialog dialog = new Views.FeedLockPromptDialog(
                    feed.Name,
                    operationName,
                    error
                )
                {
                    Owner = app.MainWindow,
                };

                if (dialog.ShowDialog() != true)
                    return false;

                if (FeedLock.Verify(feed, dialog.EnteredPassword))
                    return true;

                error = LocalizationManager.GetString("FeedLock_WrongPassword");
            }
        });
    }
}
