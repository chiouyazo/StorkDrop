using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using StorkDrop.App.Localization;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.Services;

/// <summary>
/// Helpers for deciding whether an install placed a StorkDrop plugin and for restarting the app so a
/// newly installed plugin is loaded.
/// </summary>
public static class PluginInstallHelper
{
    /// <summary>
    /// True when the given install target resolves to a location inside the StorkDrop plugins
    /// directory, i.e. the product is a plugin that only takes effect after a restart. The
    /// <c>{StorkPath}</c> token is resolved the same way the installer resolves it.
    /// </summary>
    public static bool IsPluginTarget(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return false;

        try
        {
            string baseDir = AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            );
            string resolved = targetPath.Replace("{StorkPath}", baseDir);
            string pluginsDir = Path.GetFullPath(StorkPaths.PluginsDirectory);
            return Path.GetFullPath(resolved)
                .StartsWith(pluginsDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Informs the user that a required dependency turned out to be a plugin, so the pending install
    /// has been stopped and a restart (plus configuration) is needed first. Restarts on confirmation.
    /// </summary>
    public static void PromptRestartForRequiredPlugin(string pluginTitle, string productTitle)
    {
        MessageBoxResult result = MessageBox.Show(
            LocalizationManager
                .GetString("Restart_RequiredPluginInstalled")
                .Replace("{0}", pluginTitle)
                .Replace("{1}", productTitle),
            Branding.Current.WindowTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Information
        );
        if (result == MessageBoxResult.Yes)
            RestartStorkDrop();
    }

    /// <summary>
    /// Relaunches StorkDrop after a short delay so the current process can exit and release its mutex,
    /// then shuts the current instance down.
    /// </summary>
    public static void RestartStorkDrop()
    {
        string? exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout /t 2 /nobreak >nul & \"{exePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            );
        }
        Application.Current.Shutdown();
    }
}
