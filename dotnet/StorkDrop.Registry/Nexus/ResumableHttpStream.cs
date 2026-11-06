using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace StorkDrop.Registry.Nexus;

/// <summary>
/// Forward-only HTTP body stream that survives dropped connections: on a transient read failure it
/// re-requests the remainder with a <c>Range</c> header. When the server cannot honour the range the
/// failure surfaces as an <see cref="IOException"/> so the caller can restart the download cleanly.
/// </summary>
internal sealed class ResumableHttpStream : Stream
{
    private const int DefaultMaxResumes = 5;

    private readonly HttpClient _client;
    private readonly string _url;
    private readonly ILogger _logger;
    private readonly int _maxResumes;
    private readonly TimeSpan _initialBackoff;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly long? _contentLength;

    private HttpResponseMessage? _response;
    private Stream? _body;
    private long _position;
    private int _resumes;

    private ResumableHttpStream(
        HttpClient client,
        string url,
        ILogger logger,
        int maxResumes,
        TimeSpan initialBackoff,
        Func<TimeSpan, CancellationToken, Task> delay,
        HttpResponseMessage response,
        Stream body
    )
    {
        _client = client;
        _url = url;
        _logger = logger;
        _maxResumes = maxResumes;
        _initialBackoff = initialBackoff;
        _delay = delay;
        _response = response;
        _body = body;
        _contentLength = response.Content.Headers.ContentLength;
    }

    public static async Task<ResumableHttpStream> OpenAsync(
        HttpClient client,
        string url,
        ILogger logger,
        CancellationToken cancellationToken,
        int maxResumes = DefaultMaxResumes,
        TimeSpan? initialBackoff = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        HttpResponseMessage response = await client.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        try
        {
            response.EnsureSuccessStatusCode();
            Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new ResumableHttpStream(
                client,
                url,
                logger,
                maxResumes,
                initialBackoff ?? TimeSpan.FromMilliseconds(500),
                delay ?? Task.Delay,
                response,
                body
            );
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length =>
        _contentLength ?? throw new NotSupportedException("The server did not send a length.");

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        while (true)
        {
            try
            {
                _body ??= await ReopenAsync(cancellationToken);

                int read = await _body.ReadAsync(buffer, cancellationToken);
                if (read == 0 && _contentLength is long length && _position < length)
                    throw new IOException(
                        $"Connection closed after {_position} of {length} bytes."
                    );

                _position += read;
                return read;
            }
            catch (Exception ex) when (IsResumable(ex, cancellationToken) && _resumes < _maxResumes)
            {
                _resumes++;
                _logger.LogWarning(
                    ex,
                    "Download of {Url} interrupted at byte {Position}; resuming (attempt {Attempt} of {Max})",
                    _url,
                    _position,
                    _resumes,
                    _maxResumes
                );
                ReleaseConnection();
                await _delay(
                    TimeSpan.FromTicks(_initialBackoff.Ticks * (1L << (_resumes - 1))),
                    cancellationToken
                );
            }
        }
    }

    private async Task<Stream> ReopenAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, _url);
        request.Headers.Range = new RangeHeaderValue(_position, null);

        HttpResponseMessage response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        try
        {
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                ContentRangeHeaderValue? range = response.Content.Headers.ContentRange;
                if (range?.From != _position)
                    throw new ResumeNotSupportedException(
                        $"The server answered with an unexpected byte range (wanted {_position})."
                    );

                if (
                    _contentLength is long total
                    && range.Length is long newTotal
                    && newTotal != total
                )
                    throw new ResumeNotSupportedException(
                        "The package changed on the server during the download."
                    );

                Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
                _response = response;
                return body;
            }

            if (response.StatusCode == HttpStatusCode.OK)
                throw new ResumeNotSupportedException(
                    "The server does not support resuming downloads."
                );

            response.EnsureSuccessStatusCode();
            throw new ResumeNotSupportedException(
                $"Unexpected status {(int)response.StatusCode} while resuming."
            );
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static bool IsResumable(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            OperationCanceledException => false,
            ResumeNotSupportedException => false,
            HttpRequestException http => http.StatusCode is not HttpStatusCode status
                || (int)status >= 500,
            IOException => true,
            _ => false,
        };

    private void ReleaseConnection()
    {
        _body?.Dispose();
        _body = null;
        _response?.Dispose();
        _response = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ReleaseConnection();
        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("Use ReadAsync.");

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    private sealed class ResumeNotSupportedException(string message) : IOException(message);
}
