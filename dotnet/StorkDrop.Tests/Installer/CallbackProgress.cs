using StorkDrop.Contracts.Models;

namespace StorkDrop.Tests.Installer;

internal sealed class CallbackProgress : IProgress<InstallProgress>
{
    private readonly Action<InstallProgress> _callback;

    public CallbackProgress(Action<InstallProgress> callback)
    {
        _callback = callback;
    }

    public List<string> Messages { get; } = [];

    public void Report(InstallProgress value)
    {
        Messages.Add(value.Message);
        _callback(value);
    }
}
