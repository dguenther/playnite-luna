using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LunaCatalogBrowser;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class ArtworkClientTests
{
    private const string Url = "https://m.media-amazon.com/images/I/sample.png";
    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        internal Func<CancellationToken, Task<HttpResponseMessage>> Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) });
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("m.media-amazon.com", request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            return Respond(token);
        }
    }
    private sealed class ChunkedStream : MemoryStream
    { internal ChunkedStream(byte[] bytes) : base(bytes) { } public override bool CanSeek => false; }
    private sealed class BlockingStream : MemoryStream
    {
        public override bool CanSeek => false;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return 0; }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://m.media-amazon.com/sample.png")]
    [InlineData("https://example.com/sample.png")]
    [InlineData("https://m.media-amazon.com:444/sample.png")]
    [InlineData("https://user@m.media-amazon.com/sample.png")]
    [InlineData("https://m.media-amazon.com.example.com/sample.png")]
    public async Task UnsupportedArtworkNeverStartsARequest(string? url)
    {
        var handler = new Handler();
        using var client = new HttpClient(handler);
        Assert.Null(await new CatalogArtworkClient(client).DownloadAsync(url, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AllowedArtworkDownloadsAnonymously()
    {
        var handler = new Handler();
        using var client = new HttpClient(handler);
        Assert.Equal(new byte[] { 1, 2, 3 }, await new CatalogArtworkClient(client).DownloadAsync(Url, CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImageSizeIsBoundedForDeclaredAndStreamedBodies(bool declared)
    {
        var bytes = new byte[CatalogArtworkClient.MaximumImageBytes + 1];
        var handler = new Handler { Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = declared ? new ByteArrayContent(bytes) : new StreamContent(new ChunkedStream(bytes)) }) };
        using var client = new HttpClient(handler);
        Assert.Null(await new CatalogArtworkClient(client).DownloadAsync(Url, CancellationToken.None));
    }

    [Theory]
    [InlineData("http")]
    [InlineData("network")]
    [InlineData("stream")]
    public async Task OptionalArtworkFailureReturnsNoImageAndReleasesTheDownloadSlot(string failure)
    {
        var handler = new Handler { Respond = _ => failure switch
        {
            "network" => throw new HttpRequestException("sensitive transport error"),
            "stream" => throw new IOException("sensitive stream error"),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
        }};
        using var client = new HttpClient(handler);
        var artwork = new CatalogArtworkClient(client);
        for (var i = 0; i < 5; i++) Assert.Null(await artwork.DownloadAsync(Url, CancellationToken.None));
        Assert.Equal(5, handler.Calls);
    }

    [Fact]
    public async Task DeadlineCoversBodyReads()
    {
        var handler = new Handler { Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new BlockingStream()) }) };
        using var client = new HttpClient(handler);
        var artwork = new CatalogArtworkClient(client, TimeSpan.FromMilliseconds(25));
        Assert.Null(await artwork.DownloadAsync(Url, CancellationToken.None));
    }

    [Fact]
    public async Task UnloadedRowsCanCancelDuringBodyReads()
    {
        using var stream = new BlockingStream();
        var handler = new Handler { Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }) };
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var pending = new CatalogArtworkClient(client).DownloadAsync(Url, cancellation.Token);
        await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task AtMostFourArtworkRequestsRunAndQueuedRequestsCanBeCancelled()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler();
        handler.Respond = async token =>
        {
            if (handler.Calls == 4) started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var artwork = new CatalogArtworkClient(client);
        var pending = new Task<byte[]?>[5];
        for (var i = 0; i < pending.Length; i++) pending[i] = artwork.DownloadAsync(Url, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, handler.Calls);
        cancellation.Cancel();
        foreach (var download in pending) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.Equal(4, handler.Calls);
    }
}
