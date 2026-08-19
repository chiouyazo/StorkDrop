using System.Text.Json;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Services;

/// <summary>
/// Reads and writes the per-instance file manifest. Supports both the current format (an array of
/// <see cref="TrackedFile"/> objects with hashes) and the legacy format (a plain array of relative
/// path strings), so older installs keep working and uninstall/update only ever need the paths.
/// </summary>
public static class FileManifestStore
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = false,
    };

    public static async Task WriteAsync(
        string path,
        IReadOnlyList<TrackedFile> files,
        CancellationToken cancellationToken = default
    )
    {
        string json = JsonSerializer.Serialize(files, Options);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Atomic write: a crash mid-write must never truncate the manifest, because it is the exact
        // list of files uninstall/update is allowed to delete. Write a temp file, flush to disk, then
        // replace.
        string tempPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup of the temp copy.
            }
        }
    }

    public static async Task<List<TrackedFile>?> ReadAsync(
        string path,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(path))
            return null;

        string json = await File.ReadAllTextAsync(path, cancellationToken);
        return Parse(json);
    }

    public static async Task<List<string>?> ReadPathsAsync(
        string path,
        CancellationToken cancellationToken = default
    )
    {
        List<TrackedFile>? entries = await ReadAsync(path, cancellationToken);
        return entries?.Select(f => f.Path).ToList();
    }

    public static List<TrackedFile>? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // A corrupt/half-written manifest must not crash uninstall or the integrity check; treat
            // it as "no usable manifest" so callers fall back to their safe path.
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            List<TrackedFile> result = new List<TrackedFile>();
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    string? legacyPath = element.GetString();
                    if (!string.IsNullOrEmpty(legacyPath))
                        result.Add(new TrackedFile(legacyPath));
                }
                else if (element.ValueKind == JsonValueKind.Object)
                {
                    string? filePath = ReadString(element, "Path");
                    if (string.IsNullOrEmpty(filePath))
                        continue;

                    string? sha = ReadString(element, "Sha256");
                    long size = ReadLong(element, "Size");
                    result.Add(new TrackedFile(filePath, sha, size));
                }
            }

            return result;
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        foreach (string candidate in new[] { name, ToCamel(name) })
        {
            if (
                element.TryGetProperty(candidate, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
            )
                return value.GetString();
        }
        return null;
    }

    private static long ReadLong(JsonElement element, string name)
    {
        foreach (string candidate in new[] { name, ToCamel(name) })
        {
            if (
                element.TryGetProperty(candidate, out JsonElement value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out long parsed)
            )
                return parsed;
        }
        return 0;
    }

    private static string ToCamel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
