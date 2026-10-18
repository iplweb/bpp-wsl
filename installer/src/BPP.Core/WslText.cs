using System.Text;

namespace BPP.Core;

public static class WslText
{
    public static string Decode(byte[] raw)
    {
        var text = IsUtf16Le(raw) ? Encoding.Unicode.GetString(raw) : Encoding.UTF8.GetString(raw);
        return text.TrimStart('﻿').Replace("\0", "").Replace("\r", "");
    }

    /// <summary>Cleans one line of child output: drops NULs (UTF-16 leftovers) and CRs.</summary>
    public static string CleanLine(string line) => line.Replace("\0", "").Replace("\r", "");

    private static bool IsUtf16Le(byte[] raw)
    {
        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE) return true;
        if (raw.Length < 2) return false;
        for (var i = 1; i < raw.Length; i += 2)
            if (raw[i] != 0) return false;
        return true;
    }
}
