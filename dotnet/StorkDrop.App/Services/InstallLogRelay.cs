using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace StorkDrop.App.Services;

// Routes Serilog events tagged with an "InstallId" LogContext property to that installation's log
// window, so everything logged during an install (engine + plugin) reaches the UI, not just progress.
public static class InstallLogRouter
{
    private static readonly ConcurrentDictionary<string, TrackedInstallation> Active = new();

    public static void Register(TrackedInstallation installation) =>
        Active[installation.Id] = installation;

    public static void Unregister(string id) => Active.TryRemove(id, out _);

    public static void Append(string id, string message)
    {
        if (Active.TryGetValue(id, out TrackedInstallation? installation))
            installation.AddLog(message);
    }
}

public sealed class InstallLogSink : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        if (
            !logEvent.Properties.TryGetValue("InstallId", out LogEventPropertyValue? value)
            || value is not ScalarValue { Value: string id }
            || id.Length == 0
        )
            return;

        string message = logEvent.RenderMessage();
        if (logEvent.Exception is not null)
            message = $"{message} — {logEvent.Exception.Message}";

        InstallLogRouter.Append(id, message);
    }
}
