using System.Drawing.Printing;
using System.Text;

namespace BPP.Setup;

/// <summary>"Zapisz licencję…" and "Wydrukuj licencję…" of the welcome page.</summary>
internal static class LicenceText
{
    private const string Title = "Licencja BPP";

    public static void Save(IWin32Window? owner, string text)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Zapisz licencję do pliku",
            FileName = "Licencja-BPP.txt",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Filter = "Pliki tekstowe (*.txt)|*.txt|Wszystkie pliki (*.*)|*.*",
            DefaultExt = "txt",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, "Nie udało się zapisać licencji: " + ex.Message, Title, MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>Windows' print dialog, then the text on as many pages as it takes, wrapped within the margins.</summary>
    public static void Print(IWin32Window? owner, string text)
    {
        var whole = text.Replace("\r\n", "\n");
        var remaining = whole;
        using var font = new Font("Segoe UI", 10f);
        using var document = new PrintDocument { DocumentName = Title };
        document.BeginPrint += (_, _) => remaining = whole;
        document.PrintPage += (_, e) =>
        {
            if (e.Graphics is not { } g) return;
            using var format = new StringFormat(StringFormatFlags.LineLimit);
            g.MeasureString(remaining, font, (SizeF)e.MarginBounds.Size, format, out int fitted, out int _);
            g.DrawString(remaining[..fitted], font, Brushes.Black, e.MarginBounds, format);
            remaining = remaining[fitted..].TrimStart('\n');
            // fitted == 0: not even one line fits these margins, so more pages would never end
            e.HasMorePages = fitted > 0 && remaining.Length > 0;
        };

        using var dialog = new PrintDialog { Document = document, UseEXDialog = true };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            document.Print();
        }
        catch (Exception ex) when (ex is InvalidPrinterException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(owner, "Nie udało się wydrukować licencji: " + ex.Message, Title, MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
