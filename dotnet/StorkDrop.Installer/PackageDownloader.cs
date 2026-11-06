using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace StorkDrop.Installer;

/// <summary>
/// Downloads a package stream to a file with free-space precheck, per-read inactivity timeout,
/// progress, checksum verification and retry with exponential backoff for transient failures. Every
/// attempt restarts cleanly; resuming mid-body is the job of the stream the registry client returns.
/// </summary>
internal sealed class PackageDownloader
{
    private const int BufferSize = 81920;
    private const int ErrorDiskFull = 0x70;
    private const int ErrorHandleDiskFull = 0x27;

    private readonly ILogger _logger;
    private readonly PackageDownloadOptions _options;

    public PackageDownloader(ILogger logger, PackageDownloadOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new PackageDownloadOptions();
    }

    public async Task<PackageDownloadResult> DownloadAsync(
        Func<CancellationToken, Task<Stream>> openStream,
        string destinationPath,
        string description,
        string? expectedSha256,
        long? expectedSizeBytes,
        Action<long, long?>? onProgress,
        CancellationToken cancellationToken
    )
    {
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (expectedSizeBytes is long size and > 0)
        {
            DiskSpaceGuard.EnsureFreeSpace(
                destinationPath,
                size + _options.DiskReserveBytes,
                $"downloading {description}",
                _options.AvailableBytes
            );
        }

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                PackageDownloadResult result = await AttemptAsync(
                    openStream,
                    destinationPath,
                    expectedSizeBytes,
                    onProgress,
                    cancellationToken
                );

                if (PackageHashVerifier.Normalize(expectedSha256) is string expected)
                {
                    PackageHashVerifier.EnsureMatches(expected, result.Sha256, description);
                    return result with { Verified = true };
                }

                _logger.LogWarning(
                    "The manifest of {Package} publishes no checksum; the download could NOT be verified",
                    description
                );
                return result;
            }
            catch (Exception ex)
                when (IsTransient(ex, cancellationToken) && attempt < _options.MaxAttempts)
            {
                DeleteQuietly(destinationPath);
                TimeSpan backoff = TimeSpan.FromTicks(
                    _options.InitialBackoff.Ticks * (1L << (attempt - 1))
                );
                _logger.LogWarning(
                    ex,
                    "Download of {Package} failed (attempt {Attempt} of {Max}); retrying in {Delay}s",
                    description,
                    attempt,
                    _options.MaxAttempts,
                    backoff.TotalSeconds
                );
                await _options.Delay(backoff, cancellationToken);
            }
            catch (Exception ex)
                when (ex
                        is not (
                            OperationCanceledException
                            or PackageIntegrityException
                            or InsufficientDiskSpaceException
                            or PackageDownloadException
                        )
                )
            {
                DeleteQuietly(destinationPath);
                throw new PackageDownloadException(
                    attempt > 1
                        ? $"Download of {description} failed after {attempt} attempts: {Describe(ex)}"
                        : $"Download of {description} failed: {Describe(ex)}",
                    ex
                );
            }
            catch
            {
                DeleteQuietly(destinationPath);
                throw;
            }
        }
    }

    private async Task<PackageDownloadResult> AttemptAsync(
        Func<CancellationToken, Task<Stream>> openStream,
        string destinationPath,
        long? expectedSizeBytes,
        Action<long, long?>? onProgress,
        CancellationToken cancellationToken
    )
    {
        using CancellationTokenSource openCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        openCts.CancelAfter(_options.ReadTimeout);
        Stream source;
        try
        {
            source = await openStream(openCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No response within {_options.ReadTimeout.TotalSeconds:0} seconds."
            );
        }

        await using (source)
        {
            long? total = SafeLength(source) ?? expectedSizeBytes;
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[BufferSize];
            long read = 0;

            await using FileStream destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );

            while (true)
            {
                int count = await ReadWithTimeoutAsync(source, buffer, cancellationToken);
                if (count == 0)
                    break;

                hash.AppendData(buffer, 0, count);
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                read += count;
                onProgress?.Invoke(read, total);
            }

            await destination.FlushAsync(cancellationToken);
            return new PackageDownloadResult(
                read,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
                false
            );
        }
    }

    private async Task<int> ReadWithTimeoutAsync(
        Stream source,
        byte[] buffer,
        CancellationToken cancellationToken
    )
    {
        using CancellationTokenSource readCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        readCts.CancelAfter(_options.ReadTimeout);
        try
        {
            return await source.ReadAsync(buffer.AsMemory(), readCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No data received for {_options.ReadTimeout.TotalSeconds:0} seconds."
            );
        }
    }

    private static long? SafeLength(Stream stream)
    {
        try
        {
            long length = stream.Length;
            return length > 0 ? length : null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    internal static bool IsTransient(Exception exception, CancellationToken userToken)
    {
        switch (exception)
        {
            case OperationCanceledException when userToken.IsCancellationRequested:
                return false;
            case OperationCanceledException:
            case TimeoutException:
            case SocketException:
                return true;
            case HttpRequestException http:
                return http.StatusCode is not HttpStatusCode status
                    || (int)status >= 500
                    || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
            case InsufficientDiskSpaceException:
            case FileNotFoundException:
            case DirectoryNotFoundException:
            case PathTooLongException:
            case PackageIntegrityException:
                return false;
            case IOException io:
                return (io.HResult & 0xFFFF) is not (ErrorDiskFull or ErrorHandleDiskFull);
            default:
                return false;
        }
    }

    private static string Describe(Exception exception) =>
        exception is HttpRequestException { StatusCode: HttpStatusCode status }
            ? $"the server answered {(int)status} {status}"
            : exception.Message;

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
