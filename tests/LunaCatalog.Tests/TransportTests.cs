using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using LunaCatalog;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class TransportTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public Func<HttpResponseMessage> Response { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            return Task.FromResult(Response());
        }
    }

    private sealed class ChunkedStream : MemoryStream
    {
        public ChunkedStream(byte[] bytes) : base(bytes) { }
        public override bool CanSeek => false;
    }

    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static LunaCatalogProvider Provider(HttpClient client, TimeSpan? timeout = null) =>
        new LunaCatalogProvider(client, "TestBrowser/154", "154.0.8037.57", timeout);

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    public async Task SessionWideFailuresStopRemainingCatalogQueries(int status)
    {
        var handler = new Handler { Response = () => new HttpResponseMessage((HttpStatusCode)status) };
        using var client = new HttpClient(handler);
        var snapshot = await Provider(client).FetchAsync("US", CancellationToken.None);
        Assert.Equal(1, handler.Requests);
        Assert.False(snapshot.AllQueriesSucceeded);
        Assert.Equal("Incomplete", snapshot.CoverageStatus);
        Assert.True(Assert.Single(snapshot.Queries).StopFurtherRequests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LongRetryAfterDefersRemainingQueriesWithoutRetryingEarly(bool useDate)
    {
        var handler = new Handler
        {
            Response = () =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                response.Headers.RetryAfter = useDate ? new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(2)) :
                    new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
                return response;
            }
        };
        using var client = new HttpClient(handler);
        var snapshot = await Provider(client).FetchAsync("US", CancellationToken.None);
        Assert.Equal(1, handler.Requests);
        Assert.False(snapshot.AllRequestedChecksSucceeded);
        Assert.True(Assert.Single(snapshot.Queries).StopFurtherRequests);
        Assert.Contains(snapshot.Queries[0].Diagnostics, d => d.Contains("longer retry delay"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedBodiesAreRejectedWithOrWithoutContentLength(bool declaredLength)
    {
        var bytes = new byte[LunaCatalogProvider.MaximumResponseBytes + 1];
        var handler = new Handler
        {
            Response = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = declaredLength ? new ByteArrayContent(bytes) : new StreamContent(new ChunkedStream(bytes))
            }
        };
        using var client = new HttpClient(handler);
        var query = await Provider(client).FetchQueryAsync(CatalogQuery.UsQueries[0], CancellationToken.None);
        Assert.False(query.ResponseValid);
        Assert.Equal(200, query.HttpStatus);
        Assert.Contains(query.Diagnostics, d => d.Contains("size limit"));
    }

    [Fact]
    public async Task DeadlineStillAppliesAfterResponseHeadersArrive()
    {
        var handler = new Handler { Response = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BlockingStream()) } };
        using var client = new HttpClient(handler);
        var query = await Provider(client, TimeSpan.FromMilliseconds(25)).FetchQueryAsync(CatalogQuery.UsQueries[0], CancellationToken.None);
        Assert.False(query.ResponseValid);
        Assert.Equal(200, query.HttpStatus);
        Assert.Contains(query.Diagnostics, d => d.Contains("timed out"));
    }

    [Fact]
    public async Task CallerCanCancelWhileReadingResponseBody()
    {
        var handler = new Handler { Response = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BlockingStream()) } };
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Provider(client).FetchQueryAsync(CatalogQuery.UsQueries[0], cancellation.Token));
    }

    [Fact]
    public async Task InvalidUtf8CannotBeSilentlyReplaced()
    {
        var handler = new Handler { Response = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 0xc3, 0x28 }) } };
        using var client = new HttpClient(handler);
        var query = await Provider(client).FetchQueryAsync(CatalogQuery.UsQueries[0], CancellationToken.None);
        Assert.False(query.ResponseValid);
        Assert.Contains(query.Diagnostics, d => d.Contains("UTF-8"));
    }
}
