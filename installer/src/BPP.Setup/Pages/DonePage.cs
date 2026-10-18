namespace BPP.Setup.Pages;

internal sealed class DonePage : Page
{
    public DonePage(bool lanEnabled, Action openBpp, Action close)
    {
        AddRow(Title("Gotowe"));
        AddRow(Body("BPP zostało zainstalowane i działa pod adresem " + SetupForm.BppUrl));
        AddRow(Body("Przy pierwszym otwarciu przeglądarka ostrzeże o certyfikacie. Kliknij „Zaawansowane”, " +
                    "a potem „Przejdź do localhost”."));
        if (!lanEnabled) AddRow(Body("BPP jest dostępne tylko na tym komputerze."));
        AddRow(Body("Po zamknięciu instalatora BPP pojawi się jako ikonka w zasobniku systemowym."));

        var open = Action("Otwórz BPP", openBpp);
        AddButtons(Action("Zamknij", close), open);
    }
}
