namespace StorkDrop.Core.Services;

public sealed class PathResolver
{
    public string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string expanded = Environment.ExpandEnvironmentVariables(path);

        if (expanded.StartsWith("~/") || expanded.StartsWith("~\\"))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            expanded = Path.Combine(home, expanded[2..]);
        }

        // Don't try to get full path for UNC paths on non-Windows or if path is relative placeholder
        if (expanded.StartsWith("\\\\") || expanded.StartsWith("//"))
        {
            return NormalizeSeparators(expanded);
        }

        try
        {
            return Path.GetFullPath(expanded);
        }
        catch (Exception)
        {
            return NormalizeSeparators(expanded);
        }
    }

    public bool IsUncPath(string path) => path.StartsWith("\\\\") || path.StartsWith("//");

    public bool IsValidPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            string resolved = Resolve(path);
            char[] invalidChars = Path.GetInvalidPathChars();
            return resolved.IndexOfAny(invalidChars) < 0;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeSeparators(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);
}
