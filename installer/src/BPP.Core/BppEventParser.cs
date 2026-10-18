using System.Text.Json;

namespace BPP.Core;

public static class BppEventParser
{
    public static bool TryParse(string line, out BppEvent? ev)
    {
        ev = null;
        if (string.IsNullOrWhiteSpace(line)) return false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("step", out var step) || step.ValueKind != JsonValueKind.String) return false;

            double? progress = null;
            if (root.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number)
                progress = p.GetDouble();

            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? ""
                : "";

            double? item = null;
            if (root.TryGetProperty("item", out var it) && it.ValueKind == JsonValueKind.Number)
                item = it.GetDouble();

            ev = new BppEvent(step.GetString()!, progress, message, item);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
