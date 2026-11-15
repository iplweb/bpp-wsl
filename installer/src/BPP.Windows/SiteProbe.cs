using System.Net.Http;

namespace BPP.Windows;

/// <summary>
/// Asks the BPP website for its front page the way the user's browser does (from Windows, through WSL's
/// localhost forwarding), so "it answers" means what the user will see.
/// </summary>
public sealed class SiteProbe : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _url;

    public SiteProbe(string url)
    {
        _url = new Uri(url);
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false, // any answer of the server is enough
            UseProxy = false, // localhost
            // The probe asks "is the server alive", not "is the certificate trusted": the user may have
            // declined the BPP certificate, and the site works for them anyway (with a browser warning).
            // Nothing is sent to the server and nothing it returns is used beyond the status code.
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    /// <summary>The HTTP status code, or null when the server did not answer (refused, timed out, broken TLS).</summary>
    public async Task<int?> StatusCodeAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            return (int)response.StatusCode;
        }
        catch (HttpRequestException)
        {
            return null; // nobody listens, or the connection broke
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // HttpClient's own timeout
        }
    }

    public void Dispose() => _http.Dispose();
}
