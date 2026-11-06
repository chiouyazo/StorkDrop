using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using StorkDrop.Contracts.Models;
using StorkDrop.Demo.Sandbox;
using StorkDrop.Demo.Scenarios;
using StorkDrop.Demo.Services;

namespace StorkDrop.Demo.Ui;

internal sealed class DemoControlWindow : Window
{
    private readonly DemoFeedRegistry _registry;
    private readonly DemoControlState _control;
    private readonly DemoLockState _locks;
    private readonly Func<Process?> _startLockHolder;
    private readonly TextBlock _status = new TextBlock
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
    };
    private Process? _holder;

    public DemoControlWindow(
        DemoFeedRegistry registry,
        DemoControlState control,
        DemoLockState locks,
        Process? holder
    )
    {
        _registry = registry;
        _control = control;
        _locks = locks;
        _holder = holder;
        _startLockHolder = DemoLockHolder.Start;

        Title = "StorkDrop Demo - Control";
        Width = 420;
        Height = 640;
        Left = 20;
        Top = 40;

        StackPanel panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(
            Heading("Update version changes (product: Demo: Update version changes)")
        );
        panel.Children.Add(Button("Publish newer version 1.2.0 (1.1.0 stays)", PublishNewer));
        panel.Children.Add(Button("Replace shown version 1.1.0 with 1.2.0", ReplaceShown));
        panel.Children.Add(Button("Withdraw the update (only 1.0.0 left)", WithdrawUpdate));
        panel.Children.Add(Button("Reset (1.1.0 is the latest again)", ResetStale));

        panel.Children.Add(Heading("File locks"));
        panel.Children.Add(Button("Set the demo file locks again (update/uninstall)", RearmLocks));

        panel.Children.Add(Heading("Integrity"));
        panel.Children.Add(
            Button("Tamper with the files of 'Demo: File check and repair' again", TamperAgain)
        );

        panel.Children.Add(Heading("Lock conflict (second process)"));
        panel.Children.Add(
            Button("Other process holds the lock: stop it (resolve the conflict)", ReleaseHolder)
        );
        panel.Children.Add(Button("Other process holds the lock: start it again", RestartHolder));

        panel.Children.Add(Heading("Feeds and repository"));
        panel.Children.Add(
            Button("Change the feed configuration from outside (CLI add-feed)", AddFeedExternally)
        );
        panel.Children.Add(
            Button("Toggle repository error (Updates banner)", ToggleRepositoryFailure)
        );

        panel.Children.Add(Heading("Other"));
        panel.Children.Add(Button("Open sandbox folder", OpenSandbox));
        panel.Children.Add(_status);

        Content = new ScrollViewer { Content = panel };
    }

    private static TextBlock Heading(string text) =>
        new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 4),
            TextWrapping = TextWrapping.Wrap,
        };

    private Button Button(string text, Action action)
    {
        Button button = new Button
        {
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 2, 0, 2),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                _status.Text = "Error: " + ex.Message;
            }
        };
        return button;
    }

    private DemoFeedContent StaleFeed() => _registry.Feed(DemoIds.FeedUpdateScenarios)!;

    private void PublishNewer()
    {
        ProductManifest? current = StaleFeed().Find(DemoIds.UpdateStale, "1.1.0");
        if (current is null)
        {
            _status.Text = "Version 1.1.0 no longer exists. Reset first.";
            return;
        }

        StaleFeed().Publish(current with { Version = "1.2.0" });
        _status.Text =
            "The feed now offers 1.2.0 and 1.1.0 still exists. Clicking Update installs the shown 1.1.0.";
    }

    private void ReplaceShown()
    {
        ProductManifest? current = StaleFeed().Find(DemoIds.UpdateStale, "1.1.0");
        if (current is null)
        {
            _status.Text = "Version 1.1.0 no longer exists. Reset first.";
            return;
        }

        StaleFeed().Withdraw(DemoIds.UpdateStale, "1.1.0");
        StaleFeed().Publish(current with { Version = "1.2.0" });
        _status.Text =
            "1.1.0 was replaced by 1.2.0. Clicking Update in the already loaded list must ask again.";
    }

    private void WithdrawUpdate()
    {
        StaleFeed().Withdraw(DemoIds.UpdateStale, "1.1.0");
        StaleFeed().Withdraw(DemoIds.UpdateStale, "1.2.0");
        StaleFeed().SetLatest(DemoIds.UpdateStale, "1.0.0");
        _status.Text =
            "The update was withdrawn. Clicking Update reports that it is no longer available.";
    }

    private void ResetStale()
    {
        ProductManifest? old = StaleFeed().Find(DemoIds.UpdateStale, "1.0.0");
        if (old is null)
            return;

        StaleFeed().Withdraw(DemoIds.UpdateStale, "1.2.0");
        StaleFeed().Publish(old with { Version = "1.1.0" });
        _status.Text = "The feed offers 1.1.0 again.";
    }

    private void RearmLocks()
    {
        _locks.Rearm();
        _status.Text = "The demo file locks are set again.";
    }

    private void TamperAgain()
    {
        string installPath = Path.Combine(DemoSandbox.AppsRoot, "Scenarios", DemoIds.Integrity);
        if (!Directory.Exists(installPath))
        {
            _status.Text = "The product is not installed.";
            return;
        }

        DemoSeeder.Tamper(installPath);
        _status.Text =
            "lib/core.dll was modified and config/default.json was deleted (if it still existed).";
    }

    private void ReleaseHolder()
    {
        if (_holder is { HasExited: false })
            _holder.Kill();

        _holder = null;
        _status.Text = "The other process was stopped, the locks are free.";
    }

    private void RestartHolder()
    {
        if (_holder is { HasExited: false })
            return;

        _holder = _startLockHolder();
        _status.Text = "A new process holds the locks.";
    }

    private void AddFeedExternally()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DemoSandbox.NewFeedMarkerFile)!);
        File.WriteAllText(
            DemoSandbox.NewFeedMarkerFile,
            $"{{\"feeds\":[{{\"id\":\"{DemoIds.FeedAddedLater}\"}}]}}"
        );
        _status.Text =
            "config.json was changed. The app reloads the feeds shortly. Refresh the Marketplace.";
    }

    private void ToggleRepositoryFailure()
    {
        _control.RepositoryFails = !_control.RepositoryFails;
        _status.Text = _control.RepositoryFails
            ? "Repository error ON: Updates and Marketplace report a loading error."
            : "Repository error OFF.";
    }

    private void OpenSandbox() =>
        Process.Start(new ProcessStartInfo(DemoSandbox.AppsRoot) { UseShellExecute = true });
}
