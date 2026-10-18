namespace BPP.Setup.Pages;

internal sealed class RestartPage : Page
{
    public RestartPage(Action restart, Action close)
    {
        AddRow(Title("Uruchom ponownie komputer"));
        AddRow(Body("WSL został włączony. Aby dokończyć instalację, uruchom ponownie komputer. " +
                    "Po ponownym zalogowaniu instalator BPP uruchomi się sam i będzie kontynuował."));
        AddButtons(Action("Później", close), Action("Uruchom ponownie teraz", restart));
    }
}
