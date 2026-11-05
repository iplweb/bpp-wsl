// Shared, source-linked into the .NET Framework 4.8 stub (BPP.Bootstrap): keep it to APIs both frameworks have
// (no File.Move overwrite, no Convert.ToHexString, no records, explicit usings).
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BPP.Core.Download;

/// <summary>Bytes received so far and the file size, when the server told it.</summary>
public readonly struct DownloadProgress
{
    public DownloadProgress(long received, long? total)
    {
        Received = received;
        Total = total;
    }

    public long Received { get; }
    public long? Total { get; }

    /// <summary>0..1, or null when the size is unknown.</summary>
    public double? Fraction => Total is { } t && t > 0 ? Math.Min(1.0, Received / (double)t) : (double?)null;
}

/// <summary>The download did not succeed; <see cref="Exception.Message"/> is shown to the user (Polish).</summary>
public sealed class DownloadFailedException : Exception
{
    public DownloadFailedException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Downloads one file over HTTP(S) into <c>dest</c>: resumes a previous <c>dest.part</c> with a Range request,
/// retries transient failures, and verifies the pinned SHA-256 before the file gets its final name, so a file at
/// <c>dest</c> is always complete and verified. A verified file already at <c>dest</c> is not downloaded again.
/// </summary>
public sealed class Downloader
{
    public const string PartSuffix = ".part";
    private const int BufferSize = 1 << 20;

    private readonly HttpClient _http;
    private readonly int _attempts;
    private readonly Func<int, TimeSpan> _delay;
    private readonly TimeSpan _stallTimeout;

    /// <param name="http">Should have an infinite <see cref="HttpClient.Timeout"/>: stalls are detected per read.</param>
    /// <param name="attempts">Tries per call (transient failures only).</param>
    /// <param name="delay">Wait before try n (1-based retry number); default 2 s, 5 s, 10 s, …</param>
    /// <param name="stallTimeout">No bytes for this long = transient failure; default 60 s.</param>
    public Downloader(HttpClient http, int attempts = 5, Func<int, TimeSpan>? delay = null, TimeSpan? stallTimeout = null)
    {
        _http = http;
        _attempts = Math.Max(1, attempts);
        _delay = delay ?? (n => TimeSpan.FromSeconds(n switch { 1 => 2, 2 => 5, _ => 10 }));
        _stallTimeout = stallTimeout ?? TimeSpan.FromSeconds(60);
    }

    /// <param name="sha256">Expected lowercase/uppercase hex digest; null = not verified (development builds only).</param>
    /// <param name="log">Optional diagnostic lines (retries, resume offsets).</param>
    public async Task DownloadAsync(Uri url, string dest, string? sha256, IProgress<DownloadProgress>? progress,
        CancellationToken ct, Action<string>? log = null)
    {
        if (File.Exists(dest))
        {
            if (sha256 == null || FileHash.Matches(dest, sha256))
            {
                log?.Invoke("Plik już pobrany: " + dest);
                return;
            }
            log?.Invoke("Usuwanie uszkodzonego pliku: " + dest);
            File.Delete(dest);
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(dest));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var part = dest + PartSuffix;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await DownloadOnceAsync(url, part, progress, ct, log).ConfigureAwait(false);
                if (sha256 != null && !FileHash.Matches(part, sha256))
                {
                    TryDelete(part);
                    throw new TransientException(new InvalidDataException(
                        "Suma kontrolna SHA-256 pobranego pliku się nie zgadza."));
                }
                ReplaceFile(part, dest);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < _attempts)
            {
                var wait = _delay(attempt);
                log?.Invoke($"Pobieranie przerwane ({Inner(ex).Message}) — ponawiam za {wait.TotalSeconds:0} s " +
                            $"(próba {attempt + 1} z {_attempts}).");
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not DownloadFailedException)
            {
                throw new DownloadFailedException(Describe(url, Inner(ex)), Inner(ex));
            }
        }
    }

    private async Task DownloadOnceAsync(Uri url, string part, IProgress<DownloadProgress>? progress,
        CancellationToken ct, Action<string>? log)
    {
        long existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);

        using var headersCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headersCts.CancelAfter(_stallTimeout);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TransientException(new TimeoutException("Serwer nie odpowiada."));
        }

        using (response)
        {
            var code = (int)response.StatusCode;
            if (existing > 0 && code == 416)
            {
                // the partial file already holds the whole thing (or is garbage): the hash check decides
                log?.Invoke("Plik częściowy jest kompletny.");
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                var http = new HttpRequestException($"Serwer zwrócił kod HTTP {code} ({response.ReasonPhrase}).");
                if (code == 408 || code == 429 || code >= 500) throw new TransientException(http);
                throw new DownloadFailedException(Describe(url, http), http);
            }

            var resumed = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (existing > 0 && !resumed) log?.Invoke("Serwer nie wznawia pobierania — pobieram od początku.");
            if (resumed) log?.Invoke($"Wznawianie pobierania od {existing} B.");
            long offset = resumed ? existing : 0;
            long? total = response.Content.Headers.ContentLength is { } len ? offset + len : (long?)null;
            if (resumed && response.Content.Headers.ContentRange?.Length is { } full) total = full;

            using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var file = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write,
                FileShare.None, BufferSize);
            var buffer = new byte[BufferSize];
            var received = offset;
            progress?.Report(new DownloadProgress(received, total));
            while (true)
            {
                int n;
                using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    readCts.CancelAfter(_stallTimeout);
                    try
                    {
                        n = await body.ReadAsync(buffer, 0, buffer.Length, readCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new TransientException(new TimeoutException("Pobieranie zatrzymało się (brak danych)."));
                    }
                }
                if (n == 0) break;
                await file.WriteAsync(buffer, 0, n, ct).ConfigureAwait(false);
                received += n;
                progress?.Report(new DownloadProgress(received, total));
            }
            if (total is { } expected && received < expected)
                throw new TransientException(new IOException($"Połączenie zerwane po {received} z {expected} B."));
        }
    }

    private static bool IsTransient(Exception ex)
        => ex is TransientException or HttpRequestException or IOException or WebException;

    private static Exception Inner(Exception ex) => ex is TransientException t ? t.InnerException! : ex;

    private static string Describe(Uri url, Exception ex)
        => $"Nie udało się pobrać pliku {url}: {ex.Message}";

    /// <summary>Atomic-enough rename that works on .NET Framework (no File.Move overwrite there).</summary>
    private static void ReplaceFile(string from, string to)
    {
        if (File.Exists(to)) File.Delete(to);
        File.Move(from, to);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class TransientException : Exception
    {
        public TransientException(Exception inner) : base(inner.Message, inner) { }
    }
}

public static class FileHash
{
    public static string Sha256Hex(string path)
    {
#if NETFRAMEWORK
        // SHA256.Create() is SHA256Managed there, which throws on machines with the FIPS policy enabled
        using var sha = new SHA256CryptoServiceProvider();
#else
        using var sha = SHA256.Create();
#endif
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        var hash = sha.ComputeHash(fs);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public static bool Matches(string path, string expectedHex)
        => string.Equals(Sha256Hex(path), expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
}
