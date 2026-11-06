using StorkDrop.Contracts.Services;

namespace StorkDrop.Tests.Installer;

/// <summary>
/// Redirects every StorkPaths location (config, temp, backups, logs) to a branding folder that exists
/// only for the lifetime of the instance, and removes it afterwards.
/// </summary>
internal sealed class IsolatedStorkEnvironment : IDisposable
{
    private readonly string _folderName;

    public IsolatedStorkEnvironment()
    {
        string prefix = "unittest" + Guid.NewGuid().ToString("N")[..8];
        Branding.Initialize(new BrandingInfo(Prefix: prefix));
        _folderName = Branding.Current.AppFolderName;
    }

    public void Dispose()
    {
        Branding.Initialize(BrandingInfo.Default);
        foreach (
            string root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Path.GetTempPath(),
            }
        )
        {
            string folder = Path.Combine(root, _folderName);
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }
    }
}
