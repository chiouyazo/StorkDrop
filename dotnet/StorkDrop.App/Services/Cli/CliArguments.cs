namespace StorkDrop.App.Services.Cli;

internal static class CliArguments
{
    public static string? GetFlag(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == flag)
                return args[i + 1];
        }
        return null;
    }

    public static bool HasFlag(string[] args, string flag) => Array.IndexOf(args, flag) >= 0;
}
