namespace BPP.Setup.Pages;

internal sealed class WelcomePage : Page
{
    public WelcomePage(Action install)
    {
        AddRow(Title("Zainstaluj BPP (wersja testowa)"));
        AddRow(Body("Instalator przygotuje BPP na tym komputerze. W trakcie instalacji zostanie pobranych " +
                    "około 2 GB danych, dlatego wymagane jest połączenie z internetem."));
        AddRow(Body("To instalacja do testów — do wypróbowania BPP na tym komputerze, nie do pracy " +
                    "produkcyjnej. Dane nie mają kopii zapasowych i są usuwane przy odinstalowaniu " +
                    "oraz przy instalacji nowszej wersji."));
        AddButtons(Action("Zainstaluj", install));
    }
}
