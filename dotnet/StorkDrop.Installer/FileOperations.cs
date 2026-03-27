namespace StorkDrop.Installer;

public sealed class FileOperations
{
    public async Task CopyDirectoryAsync(
        string sourceDir,
        string targetDir,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(sourceDir))
            throw new ArgumentException("Quellpfad darf nicht leer sein.", nameof(sourceDir));
        if (string.IsNullOrWhiteSpace(targetDir))
            throw new ArgumentException("Zielpfad darf nicht leer sein.", nameof(targetDir));

        DirectoryInfo source = new(sourceDir);
        if (!source.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

        Directory.CreateDirectory(targetDir);

        FileInfo[] allFiles = source.GetFiles("*", SearchOption.AllDirectories);
        int totalFiles = allFiles.Length;
        int processedFiles = 0;
        List<string> copiedFiles = [];

        try
        {
            foreach (FileInfo file in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string relativePath = Path.GetRelativePath(sourceDir, file.FullName);
                string targetPath = Path.Combine(targetDir, relativePath);

                string? targetFileDir = Path.GetDirectoryName(targetPath);
                if (targetFileDir is not null)
                    Directory.CreateDirectory(targetFileDir);

                await CopyFileAsync(file.FullName, targetPath, cancellationToken);
                copiedFiles.Add(targetPath);

                processedFiles++;
                int percentage =
                    totalFiles > 0 ? (int)((double)processedFiles / totalFiles * 100) : 100;
                progress?.Report(percentage);
            }
        }
        catch
        {
            foreach (string copiedFile in copiedFiles)
            {
                try
                {
                    if (File.Exists(copiedFile))
                        File.Delete(copiedFile);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
            throw;
        }
    }

    public async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Quelldateipfad darf nicht leer sein.", nameof(source));
        if (string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("Zieldateipfad darf nicht leer sein.", nameof(destination));

        const int bufferSize = 81920;
        await using FileStream sourceStream = new(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize,
            true
        );
        await using FileStream destStream = new(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            true
        );
        await sourceStream.CopyToAsync(destStream, bufferSize, cancellationToken);
    }

    public async Task MoveFileAsync(
        string source,
        string destination,
        bool overwrite = false,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Quelldateipfad darf nicht leer sein.", nameof(source));
        if (string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("Zieldateipfad darf nicht leer sein.", nameof(destination));

        if (!overwrite && File.Exists(destination))
            throw new IOException($"Zieldatei existiert bereits: {destination}");

        await CopyFileAsync(source, destination, cancellationToken);
        File.Delete(source);
    }

    public void AtomicReplace(string sourcePath, string destinationPath)
    {
        string backupPath = destinationPath + ".bak";

        if (File.Exists(destinationPath))
        {
            File.Replace(sourcePath, destinationPath, backupPath);
            if (File.Exists(backupPath))
                File.Delete(backupPath);
        }
        else
        {
            File.Move(sourcePath, destinationPath);
        }
    }

    public void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    public void DeleteDirectoryRecursive(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
