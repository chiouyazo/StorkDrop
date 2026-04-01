using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.Services;

public sealed class SelfUpdateService
{
    private readonly ILogger<SelfUpdateService> _logger;

    public SelfUpdateService(ILogger<SelfUpdateService> logger)
    {
        _logger = logger;
    }

    public async Task DownloadAndLaunchInstallerAsync(
        UpdateInfo update,
        CancellationToken cancellationToken = default
    )
    {
        string downloadDir = Path.Combine(StorkPaths.TempDir, "self-update");
        Directory.CreateDirectory(downloadDir);
        string installerPath = Path.Combine(downloadDir, $"StorkDrop-{update.Version}-Setup.exe");

        _logger.LogInformation(
            "Downloading StorkDrop {Version} installer from {Url}",
            update.Version,
            update.DownloadUrl
        );

        using HttpClientHandler handler = new() { AllowAutoRedirect = true };
        using HttpClient client = new(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StorkDrop-SelfUpdate");

        using HttpResponseMessage response = await client.GetAsync(
            update.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        _logger.LogInformation(
            "Download started ({Size} bytes)",
            totalBytes?.ToString() ?? "unknown"
        );

        await using Stream downloadStream = await response.Content.ReadAsStreamAsync(
            cancellationToken
        );
        await using FileStream fileStream = File.Create(installerPath);
        await downloadStream.CopyToAsync(fileStream, cancellationToken);

        _logger.LogInformation(
            "Download complete ({Bytes} bytes), launching installer: {Path}",
            new FileInfo(installerPath).Length,
            installerPath
        );

        _logger.LogInformation("Shutting down StorkDrop, then launching installer");

        // Launch installer after StorkDrop exits (installer needs the exe to not be in use)
        Process.Start(
            new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t 2 /nobreak >nul & \"{installerPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        );

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            System.Windows.Application.Current.Shutdown();
        });
    }
}
