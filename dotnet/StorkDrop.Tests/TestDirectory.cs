namespace StorkDrop.Tests;

internal sealed class TestDirectory : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "StorkDropTests",
            Guid.NewGuid().ToString("N")
        );

    public TestDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
