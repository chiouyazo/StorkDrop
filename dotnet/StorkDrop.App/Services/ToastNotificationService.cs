using System.Diagnostics;
using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.App.Services;

/// <summary>
/// Notification service that uses Windows toast notifications at runtime.
/// Falls back to Debug.WriteLine on non-Windows or when toast infrastructure is unavailable.
/// </summary>
public sealed class ToastNotificationService : INotificationService
{
    public void ShowInfo(string title, string message) => DispatchToast(title, message);

    public void ShowSuccess(string title, string message) => DispatchToast(title, message);

    public void ShowWarning(string title, string message) => DispatchToast(title, message);

    public void ShowError(string title, string message) => DispatchToast(title, message);

    public void ShowUpdateAvailable(string productTitle, string version) =>
        DispatchToast("Update Available", $"{productTitle} v{version} is now available.");

    private static void DispatchToast(string title, string message)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Use reflection to call ToastNotificationManagerCompat.Show at runtime
                // This avoids compile-time dependency on WinRT APIs while still supporting toast on Windows
                // Looks weird because we reference Uwp below, but it works.
                Type? compatType = Type.GetType(
                    "Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat, Microsoft.Toolkit.Uwp.Notifications"
                );

                if (compatType is not null)
                {
                    Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder builder = new();
                    builder.AddText(title);
                    builder.AddText(message);

                    System.Reflection.MethodInfo? showMethod = compatType.GetMethod(
                        "Show",
                        System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.Static
                    );

                    if (showMethod is not null)
                    {
                        Microsoft.Toolkit.Uwp.Notifications.ToastContent content =
                            builder.GetToastContent();
                        // may throw on non-UWP environments
                        showMethod.Invoke(null, [content]);
                        return;
                    }
                }
            }

            Debug.WriteLine($"[Toast] {title}: {message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Toast notification failed: {ex.Message}");
        }
    }
}
