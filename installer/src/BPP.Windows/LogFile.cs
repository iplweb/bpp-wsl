using System.Runtime.Versioning;
using System.Text;
using BPP.Core;

namespace BPP.Windows;

/// <summary>"Zapisz log do pliku…": asks where (Desktop by default) and writes the log as UTF-8 text, secrets masked.</summary>
[SupportedOSPlatform("windows")]
public static class LogFile
{
    private const string Caption = "BPP — zapis logu";

    public static string FileName(string baseName, DateTime now) => $"{baseName}-{now:yyyyMMdd-HHmmss}.txt";

    /// <param name="baseName">File name without date and extension, e.g. <c>BPP-instalator-log</c>.</param>
    public static void SaveAs(IWin32Window? owner, string text, string baseName)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Zapisz log do pliku",
            FileName = FileName(baseName, DateTime.Now),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Filter = "Pliki tekstowe (*.txt)|*.txt|Wszystkie pliki (*.*)|*.*",
            DefaultExt = "txt",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, SupportMail.Redact(text), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, $"Nie udało się zapisać pliku {dialog.FileName}: {ex.Message}", Caption,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
