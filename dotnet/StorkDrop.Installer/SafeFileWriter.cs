namespace StorkDrop.Installer;

internal static class SafeFileWriter
{
    public static async Task WriteAtomicAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken = default
    )
    {
        string tempPath = filePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken)
                .ConfigureAwait(false);
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception)
            { /* Best effort cleanup */
            }
        }
    }
}
