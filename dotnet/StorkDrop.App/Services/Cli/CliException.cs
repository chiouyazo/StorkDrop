namespace StorkDrop.App.Services.Cli;

internal sealed class CliException : Exception
{
    public CliException(int exitCode, string message)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
