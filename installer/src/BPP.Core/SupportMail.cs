using System.Text.RegularExpressions;

namespace BPP.Core;

/// <summary>Diagnostics for IPLweb support: the ZIP name, the prefilled e-mail and secret masking.</summary>
public static class SupportMail
{
    public const string Address = "support@iplweb.pl";

    private static readonly Regex SecretAssignment = new(
        @"(?i)\b([A-Za-z0-9_]*(?:password|passwd|secret|token|api[_-]?key)[A-Za-z0-9_]*\s*[=:]\s*)(""[^""]*""|'[^']*'|\S+)",
        RegexOptions.CultureInvariant);

    private static readonly Regex UrlCredentials = new(@"(?i)\b([a-z][a-z0-9+.-]*://[^\s:/@]+:)[^\s@/]+@",
        RegexOptions.CultureInvariant);

    public static string ZipFileName(DateTime now) => $"BPP-diagnostyka-{now:yyyyMMdd-HHmmss}.zip";

    /// <summary>Masks <c>…PASSWORD=x</c>, <c>secret: x</c>, <c>token=x</c> and <c>scheme://user:pass@</c>.</summary>
    public static string Redact(string text)
    {
        var masked = SecretAssignment.Replace(text, "$1***");
        return UrlCredentials.Replace(masked, "$1***@");
    }

    /// <summary>A <c>mailto:</c> link to support with subject and body; the user attaches the ZIP.</summary>
    public static string MailtoUri(string zipFileName, string appVersion)
    {
        var subject = $"BPP dla Windows {appVersion} — diagnostyka";
        var body = string.Join("\r\n",
            "Dzień dobry,",
            "",
            $"przesyłam diagnostykę BPP dla Windows (wersja {appVersion}).",
            "",
            "Co się stało: (opisz krótko, na którym kroku wystąpił problem)",
            "",
            $"Załącznik: {zipFileName} (plik zapisany na Pulpicie).",
            "");
        return $"mailto:{Address}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
    }
}
