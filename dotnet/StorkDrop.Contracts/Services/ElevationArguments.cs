using System.Text;

namespace StorkDrop.Contracts.Services;

internal static class ElevationArguments
{
    public const string LockOwnerFlag = "--lock-owner";
    public const string ProgressLogFlag = "--progress-log";

    public static string Quote(string value)
    {
        if (value.Length > 0 && value.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            return value;

        StringBuilder builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        int backslashes = 0;
        foreach (char c in value)
        {
            if (c == '\\')
            {
                backslashes++;
            }
            else if (c == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1).Append('"');
                backslashes = 0;
            }
            else
            {
                builder.Append('\\', backslashes).Append(c);
                backslashes = 0;
            }
        }
        builder.Append('\\', backslashes * 2).Append('"');
        return builder.ToString();
    }

    public static string Join(IEnumerable<string> tokens) => string.Join(" ", tokens.Select(Quote));

    public static string ForInstall(
        string productId,
        string version,
        string targetPath,
        string feedId,
        string instanceId,
        string? configFilePath,
        string[] commandLineArgs,
        int lockOwnerProcessId,
        string? progressLogPath
    )
    {
        List<string> tokens =
        [
            "--install",
            productId,
            targetPath,
            feedId,
            "--instance",
            instanceId,
            "--version",
            version,
        ];
        AppendCommon(tokens, commandLineArgs, configFilePath, lockOwnerProcessId, progressLogPath);
        return Join(tokens);
    }

    public static string ForUpdate(
        string productId,
        string version,
        string targetPath,
        string feedId,
        string instanceId,
        string? configFilePath,
        string[] commandLineArgs,
        int lockOwnerProcessId,
        string? progressLogPath
    )
    {
        List<string> tokens =
        [
            "--update",
            productId,
            targetPath,
            feedId,
            "--instance",
            instanceId,
            "--version",
            version,
        ];
        AppendCommon(tokens, commandLineArgs, configFilePath, lockOwnerProcessId, progressLogPath);
        return Join(tokens);
    }

    public static string ForUninstall(
        string productId,
        string instanceId,
        string[] commandLineArgs,
        int lockOwnerProcessId,
        string? progressLogPath
    )
    {
        List<string> tokens = ["--uninstall", productId, "--instance", instanceId];
        AppendCommon(tokens, commandLineArgs, null, lockOwnerProcessId, progressLogPath);
        return Join(tokens);
    }

    public static string ForReExecute(
        string productId,
        string instanceId,
        bool runPreInstall,
        bool runPostInstall,
        string? configFilePath,
        string[] commandLineArgs,
        int lockOwnerProcessId,
        string? progressLogPath
    )
    {
        List<string> tokens = ["--reexecute", productId, "--instance", instanceId];
        if (!runPreInstall)
            tokens.Add("--skip-pre");
        if (!runPostInstall)
            tokens.Add("--skip-post");
        AppendCommon(tokens, commandLineArgs, configFilePath, lockOwnerProcessId, progressLogPath);
        return Join(tokens);
    }

    public static IReadOnlyList<string> PluginDirTokens(string[] commandLineArgs)
    {
        List<string> tokens = [];
        for (int i = 0; i < commandLineArgs.Length - 1; i++)
        {
            if (commandLineArgs[i] == "--plugin-dir")
            {
                tokens.Add("--plugin-dir");
                tokens.Add(commandLineArgs[i + 1]);
            }
        }
        return tokens;
    }

    private static void AppendCommon(
        List<string> tokens,
        string[] commandLineArgs,
        string? configFilePath,
        int lockOwnerProcessId,
        string? progressLogPath
    )
    {
        tokens.AddRange(PluginDirTokens(commandLineArgs));
        if (configFilePath is not null)
            tokens.AddRange(["--config-file", configFilePath]);

        tokens.AddRange([LockOwnerFlag, lockOwnerProcessId.ToString()]);
        if (progressLogPath is not null)
            tokens.AddRange([ProgressLogFlag, progressLogPath]);
    }
}
