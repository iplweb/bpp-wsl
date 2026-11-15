namespace BPP.Core;

/// <summary>One line of the tray menu's status section, e.g. "Docker: działa".</summary>
public sealed record StatusLine(string Label, string Value, StatusLevel Level)
{
    public string Text => $"{Label}: {Value}";
}

/// <summary>
/// What the tray icon shows about the running BPP: four lines (WSL distro, Docker, services, website), the
/// overall level (the worst of them) and the tooltip.
/// </summary>
public sealed record StatusReport(StatusLevel Level, string Tooltip, IReadOnlyList<StatusLine> Lines)
{
    /// <summary>The longest text a notification icon's tooltip accepts.</summary>
    public const int MaxTooltip = 127;

    public const string TooltipOk = "BPP — działa";

    private const string Unknown = "—";

    /// <param name="distroRunning">whether WSL lists the BPP distro as running</param>
    /// <param name="stack">the parsed <c>bpp-ctl status</c>; null when it was not asked (distro stopped) or gave no answer</param>
    /// <param name="siteStatusCode">HTTP status of the website as seen from Windows; null when there was no answer</param>
    public static StatusReport Build(bool distroRunning, StackStatus? stack, int? siteStatusCode)
    {
        var distro = distroRunning
            ? new StatusLine("Dystrybucja WSL", "działa", StatusLevel.Ok)
            : new StatusLine("Dystrybucja WSL", "zatrzymana", StatusLevel.Bad);
        var docker = DockerLine(distroRunning, stack);
        var services = ServicesLine(distroRunning && stack is { Docker: true } ? stack.Services : null);
        var site = SiteLine(siteStatusCode, services.Level);

        var lines = new[] { distro, docker, services, site };
        var level = lines.Max(l => l.Level);
        // the first of the worst lines: they are ordered from cause to effect
        var tooltip = level == StatusLevel.Ok ? TooltipOk : "BPP — " + lines.First(l => l.Level == level).Text;
        if (tooltip.Length > MaxTooltip) tooltip = tooltip[..(MaxTooltip - 1)] + "…";
        return new StatusReport(level, tooltip, lines);
    }

    private static StatusLine DockerLine(bool distroRunning, StackStatus? stack)
    {
        const string label = "Docker";
        if (!distroRunning) return new StatusLine(label, Unknown, StatusLevel.Bad);
        if (stack == null) return new StatusLine(label, "stan nieznany (brak odpowiedzi bpp-ctl)", StatusLevel.Bad);
        return stack.Docker
            ? new StatusLine(label, "działa", StatusLevel.Ok)
            : new StatusLine(label, "nie odpowiada", StatusLevel.Bad);
    }

    private static StatusLine ServicesLine(IReadOnlyList<ServiceStatus>? services)
    {
        const string label = "Usługi";
        if (services == null) return new StatusLine(label, Unknown, StatusLevel.Bad);
        if (services.Count == 0) return new StatusLine(label, "brak", StatusLevel.Bad);

        var starting = services.Where(s => s.Level == StatusLevel.Starting).Select(s => s.Name).ToList();
        var bad = services.Where(s => s.Level == StatusLevel.Bad).Select(s => s.Name).ToList();
        var ok = services.Count - starting.Count - bad.Count;

        var value = $"działa {ok} z {services.Count}";
        if (starting.Count > 0) value += "; uruchamianie: " + string.Join(", ", starting);
        if (bad.Count > 0) value += "; nie działa: " + string.Join(", ", bad);
        var level = bad.Count > 0 ? StatusLevel.Bad : starting.Count > 0 ? StatusLevel.Starting : StatusLevel.Ok;
        return new StatusLine(label, value, level);
    }

    /// <summary>While services are still starting, a website that does not answer yet is starting too.</summary>
    private static StatusLine SiteLine(int? statusCode, StatusLevel servicesLevel)
    {
        const string label = "Strona";
        if (statusCode is >= 100 and < 500) return new StatusLine(label, "odpowiada", StatusLevel.Ok);
        var level = servicesLevel == StatusLevel.Starting ? StatusLevel.Starting : StatusLevel.Bad;
        return new StatusLine(label, statusCode == null ? "nie odpowiada" : $"błąd {statusCode}", level);
    }
}
