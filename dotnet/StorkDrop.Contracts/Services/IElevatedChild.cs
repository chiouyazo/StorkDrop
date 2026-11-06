namespace StorkDrop.Contracts.Services;

internal interface IElevatedChild
{
    int ExitCode { get; }

    bool WaitForExit(TimeSpan timeout);

    void Kill();
}
