namespace StorkDrop.App.Services.Cli;

internal static class CliExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int Usage = 2;
    public const int NotFound = 3;
    public const int FeedError = 4;
    public const int FeedLocked = 5;
    public const int MissingConfiguration = 6;

    public static readonly IReadOnlyList<string> HelpLines =
    [
        "Exit codes:",
        "  0  Success",
        "  1  Operation failed (install, uninstall, update, re-execute, apply)",
        "  2  Invalid usage (unknown command, missing or unreadable argument or file)",
        "  3  Not found (product, version, installed product, feed)",
        "  4  Feed error (a feed could not be queried; partial results are still printed)",
        "  5  Feed is locked (missing or wrong unlock password)",
        "  6  Missing required plugin configuration",
    ];
}
