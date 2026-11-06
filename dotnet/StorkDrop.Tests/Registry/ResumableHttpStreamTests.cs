using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;
using StorkDrop.Contracts.Models;
using StorkDrop.Registry;
using StorkDrop.Registry.Nexus;
using Xunit;

namespace StorkDrop.Tests.Registry;

public sealed class ResumableHttpStreamTests
{
    private const string Url = "https://nexus.test.com/repository/r/p/versions/1.0.0/p-1.0.0.zip";

    private static readonly byte[] Content = Enumerable
        .Range(0, 100_000)
        .Select(i => (byte)(i % 253))
        .ToArray();

    private sealed class DroppingStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _dropAfter;
        private int _position;

        public DroppingStream(byte[] data, int dropAfter)
        {
            _data = data;
            _dropAfter = dropAfter;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (_position >= _dropAfter)
                throw new IOException("The response ended prematurely.");

            int count = Math.Min(Math.Min(buffer.Length, 8_000), _dropAfter - _position);
            _data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return new ValueTask<int>(count);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private static HttpResponseMessage Full(byte[] data, int dropAfter = int.MaxValue)
    {
        HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(
                dropAfter == int.MaxValue
                    ? new MemoryStream(data)
                    : new DroppingStream(data, dropAfter)
            ),
        };
        response.Content.Headers.ContentLength = data.Length;
        return response;
    }

    private static HttpResponseMessage Partial(byte[] data, long from)
    {
        HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(data[(int)from..]),
        };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
            from,
            data.Length - 1,
            data.Length
        );
        return response;
    }

    private static async Task<byte[]> ReadAll(Stream stream)
    {
        using MemoryStream buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static Task<ResumableHttpStream> Open(
        HttpClient client,
        List<TimeSpan>? delays = null,
        int maxResumes = 5
    ) =>
        ResumableHttpStream.OpenAsync(
            client,
            Url,
            NullLogger.Instance,
            CancellationToken.None,
            maxResumes,
            TimeSpan.FromSeconds(1),
            (delay, _) =>
            {
                delays?.Add(delay);
                return Task.CompletedTask;
            }
        );

    [Fact]
    public async Task Read_WithoutInterruption_ReturnsTheWholeBody_AndExposesLength()
    {
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        handler.When(Url).Respond(_ => Full(Content));

        await using ResumableHttpStream stream = await Open(handler.ToHttpClient());

        stream.Length.Should().Be(Content.Length);
        (await ReadAll(stream)).Should().Equal(Content);
        stream.Position.Should().Be(Content.Length);
    }

    [Fact]
    public async Task Read_ResumesWithRangeHeader_AfterTheConnectionDrops()
    {
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        List<string?> ranges = [];
        int calls = 0;
        handler
            .When(Url)
            .Respond(request =>
            {
                calls++;
                ranges.Add(request.Headers.Range?.ToString());
                if (calls == 1)
                    return Full(Content, dropAfter: 40_000);
                long from = request.Headers.Range!.Ranges.Single().From!.Value;
                return Partial(Content, from);
            });
        List<TimeSpan> delays = [];

        await using ResumableHttpStream stream = await Open(handler.ToHttpClient(), delays);
        byte[] result = await ReadAll(stream);

        result.Should().Equal(Content);
        calls.Should().Be(2);
        ranges[0].Should().BeNull();
        ranges[1].Should().Be("bytes=40000-");
        delays.Should().Equal(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Read_ResumesRepeatedly_WithGrowingBackoff()
    {
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        int calls = 0;
        handler
            .When(Url)
            .Respond(request =>
            {
                calls++;
                long from = request.Headers.Range?.Ranges.Single().From ?? 0;
                if (calls == 1)
                    return Full(Content, dropAfter: 10_000);
                if (calls == 2)
                {
                    HttpResponseMessage part = Partial(Content, from);
                    part.Content = new StreamContent(
                        new DroppingStream(Content[(int)from..], 20_000)
                    );
                    part.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                        from,
                        Content.Length - 1,
                        Content.Length
                    );
                    return part;
                }
                return Partial(Content, from);
            });
        List<TimeSpan> delays = [];

        await using ResumableHttpStream stream = await Open(handler.ToHttpClient(), delays);

        (await ReadAll(stream)).Should().Equal(Content);
        delays.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Read_WhenServerIgnoresRange_FailsWithIOExceptionSoTheCallerRestarts()
    {
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        int calls = 0;
        handler
            .When(Url)
            .Respond(_ => calls++ == 0 ? Full(Content, dropAfter: 5_000) : Full(Content));

        await using ResumableHttpStream stream = await Open(handler.ToHttpClient());
        Func<Task> act = () => ReadAll(stream);

        (await act.Should().ThrowAsync<IOException>())
            .Which.Message.Should()
            .Contain("does not support resuming");
    }

    [Fact]
    public async Task Read_WhenRangeAnswerStartsAtTheWrongOffset_Fails()
    {
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        int calls = 0;
        handler
            .When(Url)
            .Respond(_ => calls++ == 0 ? Full(Content, dropAfter: 5_000) : Partial(Content, 100));

        await using ResumableHttpStream stream = await Open(handler.ToHttpClient());
        Func<Task> act = () => ReadAll(stream);

        (await act.Should().ThrowAsync<IOException>())
            .Which.Message.Should()
            .Contain("unexpected byte range");
    }

    [Fact]
    public async Task Read_GivesUpAfterTheResumeBudget()
    {
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        int calls = 0;
        handler
            .When(Url)
            .Respond(request =>
            {
                calls++;
                if (calls == 1)
                    return Full(Content, dropAfter: 5_000);
                long from = request.Headers.Range!.Ranges.Single().From!.Value;
                HttpResponseMessage part = Partial(Content, from);
                part.Content = new StreamContent(new DroppingStream(Content[(int)from..], 0));
                part.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                    from,
                    Content.Length - 1,
                    Content.Length
                );
                return part;
            });

        await using ResumableHttpStream stream = await Open(handler.ToHttpClient(), maxResumes: 2);
        Func<Task> act = () => ReadAll(stream);

        await act.Should().ThrowAsync<IOException>();
        calls.Should().Be(3);
    }

    [Fact]
    public async Task Open_PropagatesHttpErrorStatus()
    {
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        handler.When(Url).Respond(HttpStatusCode.Unauthorized);

        Func<Task> act = () => Open(handler.ToHttpClient());

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Read_StopsOnUserCancellation_WithoutResuming()
    {
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        int calls = 0;
        handler
            .When(Url)
            .Respond(_ =>
            {
                calls++;
                return Full(Content);
            });
        using CancellationTokenSource cts = new CancellationTokenSource();
        await using ResumableHttpStream stream = await Open(handler.ToHttpClient());
        cts.Cancel();

        Func<Task> act = async () => await stream.ReadAsync(new byte[10], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Stream_IsForwardOnlyAndReadOnly()
    {
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        handler.When(Url).Respond(_ => Full(Content));
        await using ResumableHttpStream stream = await Open(handler.ToHttpClient());

        stream.CanRead.Should().BeTrue();
        stream.CanSeek.Should().BeFalse();
        stream.CanWrite.Should().BeFalse();
        Action seek = () => stream.Seek(0, SeekOrigin.Begin);
        seek.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task NexusClient_DownloadProductAsync_SurvivesAConnectionDrop()
    {
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        int calls = 0;
        handler
            .When(Url)
            .Respond(request =>
            {
                calls++;
                if (calls == 1)
                    return Full(Content, dropAfter: 30_000);
                return Partial(Content, request.Headers.Range!.Ranges.Single().From!.Value);
            });
        NexusOptions options = new NexusOptions
        {
            BaseUrl = "https://nexus.test.com",
            Repository = "r",
        };
        HttpClient http = handler.ToHttpClient();
        http.BaseAddress = new Uri(options.BaseUrl);
        NexusRegistryClient client = new NexusRegistryClient(
            http,
            Options.Create(options),
            NullLogger<NexusRegistryClient>.Instance
        );

        await using Stream stream = await client.DownloadProductAsync("p", "1.0.0");

        (await ReadAll(stream)).Should().Equal(Content);
        calls.Should().Be(2);
    }
}
