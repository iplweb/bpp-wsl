namespace BPP.Setup.Pages;

/// <summary>
/// First page after the restart that enabling WSL required (<c>--resume</c>): says what happens next, so the
/// console and administrator windows that follow do not come out of nowhere. Nothing runs before "OK".
/// </summary>
internal sealed class ResumePage : Page
{
    public ResumePage(Action resume, Action close)
    {
        AddRow(Title("Witaj w kolejnym etapie instalacji BPP"));
        AddRow(Body("Komputer został uruchomiony ponownie i WSL jest włączony. Po kliknięciu „OK” instalator " +
                    "dokończy instalację:"));
        AddRow(Body("1. Sprawdzi WSL i w razie potrzeby go zaktualizuje. Może wtedy pojawić się czarne okno " +
                    "konsoli z postępem oraz okno administratora Windows z pytaniem o zgodę — instalator " +
                    "uprzedzi o tym osobnym komunikatem."));
        AddRow(Body("2. Pobierze dystrybucję i obrazy BPP (ok. 800 MB) — to potrwa od kilku do kilkunastu minut."));
        AddRow(Body("3. Uruchomi BPP i zapyta o zgodę na udostępnienie go w sieci lokalnej."));
        AddButtons(Action("OK", resume), Action("Później", close));
    }
}
