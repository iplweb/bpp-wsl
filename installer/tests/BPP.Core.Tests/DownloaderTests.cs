using System.Net;
using System.Net.Http.Headers;
using BPP.Core.Download;

namespace BPP.Core.Tests;

public sealed class DownloaderTests : IDisposable
{
    private static readonly Uri Url = new("https://example.invalid/bpp-rootfs.tar.gz");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bpp-dl-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _data = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 31 % 251)).ToArray();

    public DownloaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Dest => Path.Combine(_dir, "sub", "file.bin");

    private string Sha(byte[] bytes)
    {
        var p = Path.Combine(_dir, Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(p, bytes);
        return FileHash.Sha256Hex(p);
    }

    private static Downloader Make(FakeServer server, int attempts = 3)
        => new(new HttpClient(server) { Timeout = Timeout.InfiniteTimeSpan }, attempts, _ => TimeSpan.Zero);

    [Fact]
    public async Task Downloads_and_verifies()
    {
        var server = new FakeServer(_data);
        var reports = new List<DownloadProgress>();
        await Make(server).DownloadAsync(Url, Dest, Sha(_data), new SyncProgress(reports.Add), CancellationToken.None);
        Assert.Equal(_data, File.ReadAllBytes(Dest));
        Assert.False(File.Exists(Dest + Downloader.PartSuffix));
        Assert.Equal(1.0, reports[^1].Fraction);
    }

    [Fact]
    public async Task Verified_file_already_there_is_not_downloaded_again()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Dest)!);
        File.WriteAllBytes(Dest, _data);
        var server = new FakeServer(_data);
        await Make(server).DownloadAsync(Url, Dest, Sha(_data), null, CancellationToken.None);
        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public async Task Corrupt_file_already_there_is_replaced()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Dest)!);
        File.WriteAllBytes(Dest, new byte[] { 1, 2, 3 });
        await Make(new FakeServer(_data)).DownloadAsync(Url, Dest, Sha(_data), null, CancellationToken.None);
        Assert.Equal(_data, File.ReadAllBytes(Dest));
    }

    [Fact]
    public async Task Resumes_a_partial_file_with_a_range_request()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Dest)!);
        File.WriteAllBytes(Dest + Downloader.PartSuffix, _data[..100_000]);
        var server = new FakeServer(_data);
        await Make(server).DownloadAsync(Url, Dest, Sha(_data), null, CancellationToken.None);
        Assert.Equal(_data, File.ReadAllBytes(Dest));
        Assert.Equal(100_000, server.LastRangeFrom);
    }

    [Fact]
    public async Task Server_ignoring_range_restarts_from_zero()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Dest)!);
        File.WriteAllBytes(Dest + Downloader.PartSuffix, new byte[] { 9, 9, 9, 9 });
        var server = new FakeServer(_data) { SupportsRange = false };
        await Make(server).DownloadAsync(Url, Dest, Sha(_data), null, CancellationToken.None);
        Assert.Equal(_data, File.ReadAllBytes(Dest));
    }

    [Fact]
    public async Task Connection_dropped_midway_is_retried_and_resumed()
    {
        var server = new FakeServer(_data) { CutFirstResponseAt = 120_000 };
        await Make(server).DownloadAsync(Url, Dest, Sha(_data), null, CancellationToken.None);
        Assert.Equal(_data, File.ReadAllBytes(Dest));
        Assert.Equal(2, server.Requests);
        Assert.Equal(120_000, server.LastRangeFrom);
    }

    [Fact]
    public async Task Server_errors_are_retried()
    {
        var server = new FakeServer(_data) { FailFirst = 2, FailWith = HttpStatusCode.ServiceUnavailable };
        await Make(server, attempts: 3).DownloadAsync(Url, Dest, Sha(_data), null, CancellationToken.None);
        Assert.Equal(_data, File.ReadAllBytes(Dest));
    }

    [Fact]
    public async Task Not_found_fails_at_once_with_a_readable_message()
    {
        var server = new FakeServer(_data) { FailFirst = 99, FailWith = HttpStatusCode.NotFound };
        var ex = await Assert.ThrowsAsync<DownloadFailedException>(
            () => Make(server).DownloadAsync(Url, Dest, Sha(_data), null, CancellationToken.None));
        Assert.Equal(1, server.Requests);
        Assert.Contains("404", ex.Message);
        Assert.Contains(Url.ToString(), ex.Message);
    }

    [Fact]
    public async Task Wrong_checksum_never_leaves_a_file_and_fails_after_retries()
    {
        var server = new FakeServer(_data);
        var ex = await Assert.ThrowsAsync<DownloadFailedException>(
            () => Make(server, attempts: 2).DownloadAsync(Url, Dest, new string('0', 64), null, CancellationToken.None));
        Assert.Contains("SHA-256", ex.Message);
        Assert.False(File.Exists(Dest));
        Assert.False(File.Exists(Dest + Downloader.PartSuffix));
        Assert.Equal(2, server.Requests);
    }

    [Fact]
    public async Task Cancellation_is_not_a_failure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Make(new FakeServer(_data)).DownloadAsync(Url, Dest, Sha(_data), null, cts.Token));
    }

    private sealed class SyncProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }

    /// <summary>In-memory HTTP server: optional Range support, injected errors and a dropped first response.</summary>
    private sealed class FakeServer(byte[] data) : HttpMessageHandler
    {
        public bool SupportsRange { get; init; } = true;
        public int FailFirst { get; init; }
        public HttpStatusCode FailWith { get; init; } = HttpStatusCode.InternalServerError;
        public int CutFirstResponseAt { get; init; } = -1;
        public int Requests { get; private set; }
        public long? LastRangeFrom { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Requests++;
            if (Requests <= FailFirst) return Task.FromResult(new HttpResponseMessage(FailWith));

            var from = request.Headers.Range?.Ranges.First().From;
            LastRangeFrom = from;
            HttpResponseMessage response;
            if (from is { } f && SupportsRange)
            {
                if (f >= data.Length) return Task.FromResult(new HttpResponseMessage((HttpStatusCode)416));
                response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = Body(data[(int)f..]) };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(f, data.Length - 1, data.Length);
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Body(data) };
            }
            return Task.FromResult(response);
        }

        private HttpContent Body(byte[] bytes)
        {
            if (Requests == 1 + FailFirst && CutFirstResponseAt >= 0)
            {
                // the headers announce the full length, the stream ends early: a dropped connection
                var content = new StreamContent(new MemoryStream(bytes[..CutFirstResponseAt]));
                content.Headers.ContentLength = bytes.Length;
                return content;
            }
            return new ByteArrayContent(bytes);
        }
    }
}
