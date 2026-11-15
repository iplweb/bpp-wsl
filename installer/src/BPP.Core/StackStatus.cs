using System.Text.Json;

namespace BPP.Core;

public enum StatusLevel
{
    Ok,
    Starting,
    Bad,
}

/// <summary>One service of the stack, with the container's state and health as Docker reports them.</summary>
public sealed record ServiceStatus(string Name, string State, string Health)
{
    /// <summary>
    /// Running counts as fine also without a healthcheck (empty health). A container that is being restarted
    /// or was only created is on its way up; anything else (exited, dead, paused, absent) is down.
    /// </summary>
    public StatusLevel Level => State switch
    {
        "running" => Health switch
        {
            "unhealthy" => StatusLevel.Bad,
            "starting" => StatusLevel.Starting,
            _ => StatusLevel.Ok,
        },
        "restarting" or "created" => StatusLevel.Starting,
        _ => StatusLevel.Bad,
    };
}

/// <summary>What <c>bpp-ctl status</c> prints: whether Docker answers, and every service of the stack.</summary>
public sealed record StackStatus(bool Docker, IReadOnlyList<ServiceStatus> Services)
{
    /// <summary>Parses the output of <c>bpp-ctl status</c>: the first line that is such a JSON object.</summary>
    public static bool TryParse(string text, out StackStatus? status)
    {
        status = null;
        foreach (var line in text.Split('\n'))
        {
            if (TryParseLine(line.Trim(), out status)) return true;
        }
        return false;
    }

    private static bool TryParseLine(string line, out StackStatus? status)
    {
        status = null;
        if (line.Length == 0) return false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("docker", out var docker)
                || docker.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            if (!root.TryGetProperty("services", out var list) || list.ValueKind != JsonValueKind.Array) return false;

            var services = new List<ServiceStatus>();
            foreach (var s in list.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object) return false;
                var name = Text(s, "name");
                if (name.Length == 0) return false;
                services.Add(new ServiceStatus(name, Text(s, "state"), Text(s, "health")));
            }
            status = new StackStatus(docker.GetBoolean(), services);
            return true;
        }
        catch (JsonException)
        {
            return false; // not JSON: some other line of output
        }
    }

    private static string Text(JsonElement obj, string property)
        => obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
