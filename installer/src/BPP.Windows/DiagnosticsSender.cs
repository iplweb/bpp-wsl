using System.Diagnostics;
using System.Runtime.Versioning;
using BPP.Core;

namespace BPP.Windows;

/// <summary>
/// "Wyślij diagnostykę do IPLweb…": asks, builds the ZIP, shows it in Explorer and opens a prefilled e-mail to
/// support. Nothing is uploaded: the user attaches the file and sends the message.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DiagnosticsSender
{
    private const string Caption = "BPP — diagnostyka";
    private static bool _busy;

    /// <summary>True while a ZIP is being built (callers must not start or stop the distro meanwhile).</summary>
    public static bool IsBusy => _busy;

    /// <param name="createZip">Builds the ZIP and returns its path (see <see cref="Diagnostics.CreateZipAsync"/>).</param>
    /// <param name="busy">Called with true/false around the collection, e.g. to disable a button.</param>
    public static async Task SendAsync(IWin32Window? owner, Func<Task<string>> createZip, Action<bool>? busy = null)
    {
        if (_busy) return;
        var ok = MessageBox.Show(owner,
            "Instalator przygotuje plik z diagnostyką BPP: logi programu i usług BPP oraz informacje " +
            "o komputerze i WSL (bez haseł). Zbieranie może potrwać do kilku minut.\n\n" +
            $"Potem otworzy się nowa wiadomość do {SupportMail.Address}, do której dołączysz ten plik.\n\nKontynuować?",
            Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        if (!ok) return;

        string zip;
        _busy = true;
        busy?.Invoke(true);
        var cursor = Cursor.Current;
        Cursor.Current = Cursors.WaitCursor;
        try
        {
            zip = await createZip();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(owner, "Nie udało się przygotować diagnostyki: " + ex.Message, Caption,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            Cursor.Current = cursor;
            busy?.Invoke(false);
            _busy = false;
        }

        MessageBox.Show(owner,
            $"Zapisano plik:\n{zip}\n\n" +
            $"Teraz otworzy się nowa wiadomość do {SupportMail.Address} i okno z tym plikiem. " +
            "Dołącz plik do wiadomości (np. przeciągnij go do okna wiadomości), opisz krótko problem i kliknij Wyślij.\n\n" +
            "Jeśli wiadomość się nie otworzy (np. gdy używasz poczty w przeglądarce), wyślij ten plik samodzielnie " +
            $"na adres {SupportMail.Address}.",
            Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);

        Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            $"/select,\"{zip}\"");
        var version = Process.GetCurrentProcess().MainModule?.FileVersionInfo.ProductVersion
                      ?? typeof(DiagnosticsSender).Assembly.GetName().Version?.ToString(3) ?? "?";
        if (!Start(SupportMail.MailtoUri(Path.GetFileName(zip), version.Split('+')[0]), null))
        {
            MessageBox.Show(owner,
                $"Nie udało się otworzyć programu pocztowego. Wyślij plik {Path.GetFileName(zip)} na adres " +
                $"{SupportMail.Address} (adres skopiowano do schowka).",
                Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            try { Clipboard.SetText(SupportMail.Address); }
            catch (System.Runtime.InteropServices.ExternalException) { }
        }
    }

    private static bool Start(string target, string? args)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = args ?? "" });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
