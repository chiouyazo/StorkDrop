using System.Text;

namespace StorkDrop.Installer;

internal static class SafeFileWriter
{
    // UTF-8 without BOM, matching File.WriteAllText's default.
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false
    );

    public static async Task WriteAtomicAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken = default
    )
    {
        string tempPath = filePath + ".tmp";
        try
        {
            // Write the temp file WriteThrough + flush so the bytes are durably on disk before the
            // rename; otherwise a crash/power loss can leave the rename pointing at unflushed data.
            byte[] bytes = Utf8NoBom.GetBytes(content);
            await using (
                FileStream stream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough | FileOptions.Asynchronous
                )
            )
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

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
