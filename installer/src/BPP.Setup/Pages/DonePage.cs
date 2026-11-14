namespace BPP.Setup.Pages;

internal sealed class DonePage : Page
{
    public DonePage(bool lanEnabled, bool certificateTrusted, Action openBpp, Action close)
    {
        AddRow(Title("Gotowe"));
        AddRow(Body("BPP zostało zainstalowane, wszystkie usługi są uruchomione. BPP działa pod adresem:"));
        var address = new LinkLabel
        {
            Text = SetupForm.BppUrl,
            AutoSize = true,
            Font = BoldFont,
            Margin = new Padding(0, 0, 0, 10),
        };
        address.LinkClicked += (_, _) => address.BeginInvoke(openBpp);
        AddRow(address);
        if (!certificateTrusted)
            AddRow(Body("Przy pierwszym otwarciu przeglądarka ostrzeże o certyfikacie. Kliknij „Zaawansowane”, " +
                        "a potem „Przejdź do localhost” — albo dodaj certyfikat BPP z menu ikonki BPP."));
        if (!lanEnabled) AddRow(Body("BPP jest dostępne tylko na tym komputerze."));
        AddRow(Body("Ikonka BPP jest już w zasobniku systemowym (obok zegara) — z jej menu otworzysz, zatrzymasz " +
                    "i odinstalujesz BPP."));

        var open = Action("Otwórz BPP", openBpp);
        AddButtons(Action("Zamknij", close), open);
    }
}
