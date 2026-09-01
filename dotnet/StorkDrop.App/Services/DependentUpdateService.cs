using Microsoft.Extensions.Logging;
using StorkDrop.App.Localization;
using StorkDrop.App.Views;
using StorkDrop.Contracts.Models;
using StorkDrop.Installer;

namespace StorkDrop.App.Services;

/// <summary>
/// After a product is updated, offers to update the installed products that depend on it and have a
/// newer version on their own channel. Each update runs individually and in dependency order, so their
/// config dialogs and elevation prompts never overlap.
/// </summary>
public sealed class DependentUpdateService
{
    private readonly DependentUpdateResolver _resolver;
    private readonly InstallationCoordinator _coordinator;
    private readonly InstallationTracker _tracker;
    private readonly DialogService _dialogService;
    private readonly ILogger<DependentUpdateService> _logger;

    public DependentUpdateService(
        DependentUpdateResolver resolver,
        InstallationCoordinator coordinator,
        InstallationTracker tracker,
        DialogService dialogService,
        ILogger<DependentUpdateService> logger
    )
    {
        _resolver = resolver;
        _coordinator = coordinator;
        _tracker = tracker;
        _dialogService = dialogService;
        _logger = logger;
    }

    public async Task OfferDependentUpdatesAsync(
        string updatedProductId,
        string updatedProductTitle,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<DependentUpdate> candidates;
        try
        {
            candidates = await Task.Run(
                () => _resolver.ResolveAsync(updatedProductId, cancellationToken),
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Dependent-update resolution failed for {ProductId}",
                updatedProductId
            );
            return;
        }

        if (candidates.Count == 0)
            return;

        IReadOnlyList<DependentUpdate> selected =
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                DependentUpdatesDialog dialog = new DependentUpdatesDialog(
                    updatedProductTitle,
                    candidates
                )
                {
                    Owner = System.Windows.Application.Current.MainWindow,
                };
                return dialog.ShowDialog() == true
                    ? dialog.Selected
                    : (IReadOnlyList<DependentUpdate>)[];
            });

        foreach (DependentUpdate item in selected)
        {
            bool success = await UpdateOneAsync(item, cancellationToken);
            if (success)
                continue;

            bool keepGoing = _dialogService.ShowConfirmation(
                LocalizationManager
                    .GetString("DependentUpdates_ContinuePrompt")
                    .Replace("{0}", item.Title),
                LocalizationManager.GetString("DependentUpdates_Title")
            );
            if (!keepGoing)
                break;
        }
    }

    private async Task<bool> UpdateOneAsync(
        DependentUpdate item,
        CancellationToken cancellationToken
    )
    {
        InstalledProduct installed = item.Installed;
        if (string.IsNullOrEmpty(installed.FeedId))
            return false;

        TrackedInstallation tracked = _tracker.StartInstallation(
            installed.ProductId,
            $"Updating: {installed.Title} -> v{item.TargetVersion}"
        );
        using var logScope = Serilog.Context.LogContext.PushProperty("InstallId", tracked.Id);
        tracked.AddLog(
            $"Updating {installed.Title} from v{item.CurrentVersion} to v{item.TargetVersion}"
        );

        Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
        {
            tracked.Percentage = p.Percentage;
            tracked.StatusMessage = p.Message;
            if (!string.IsNullOrEmpty(p.Message))
                tracked.AddLog(p.Message);
        });

        InstallOptions options = new InstallOptions(
            TargetPath: installed.InstalledPath,
            InstanceId: installed.InstanceId,
            FeedId: installed.FeedId
        );

        try
        {
            InstallResult result = await Task.Run(
                () =>
                    _coordinator.UpdateWithIsolationAsync(
                        installed,
                        item.TargetManifest,
                        options,
                        progress,
                        cancellationToken
                    ),
                cancellationToken
            );
            tracked.Complete(result.Success, result.ErrorMessage);
            _tracker.NotifyChanged();
            return result.Success;
        }
        catch (Exception ex)
        {
            tracked.Complete(false, ex.Message);
            _tracker.NotifyChanged();
            _logger.LogError(ex, "Dependent update failed for {ProductId}", installed.ProductId);
            return false;
        }
    }
}
