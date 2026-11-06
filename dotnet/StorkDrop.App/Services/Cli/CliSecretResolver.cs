using System.IO;

namespace StorkDrop.App.Services.Cli;

internal static class CliSecretResolver
{
    public static string? Resolve(
        string[] args,
        string flag,
        string fileFlag,
        string environmentVariable,
        Func<string, string?> getEnvironmentVariable
    )
    {
        string? direct = CliArguments.GetFlag(args, flag);
        if (!string.IsNullOrEmpty(direct))
            return direct;

        string? filePath = CliArguments.GetFlag(args, fileFlag);
        if (filePath is not null)
        {
            if (!File.Exists(filePath))
                throw new CliException(
                    CliExitCodes.Usage,
                    $"File for {fileFlag} not found: {filePath}"
                );

            string content = File.ReadAllText(filePath).TrimEnd('\r', '\n');
            return content.Length == 0 ? null : content;
        }

        string? fromEnvironment = getEnvironmentVariable(environmentVariable);
        return string.IsNullOrEmpty(fromEnvironment) ? null : fromEnvironment;
    }
}
