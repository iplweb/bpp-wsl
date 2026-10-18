using BPP.Core;

namespace BPP.Pack;

internal static class Program
{
    private const string Usage =
        "Użycie:\n  BPP.Pack <setup.exe> <out.exe> name=path ...\n  BPP.Pack --list <exe>";

    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--list") return List(args[1]);
        if (args.Length >= 3 && args[0] != "--list") return Pack(args);
        Console.Error.WriteLine(Usage);
        return 2;
    }

    private static int List(string exe)
    {
        try
        {
            foreach (var (name, e) in PayloadArchive.ReadIndex(exe))
                Console.WriteLine($"{name}\t{e.Length}");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Błąd: {ex.Message}");
            return 1;
        }
    }

    private static int Pack(string[] args)
    {
        var entries = new List<(string Name, string FilePath)>();
        foreach (var arg in args.Skip(2))
        {
            var eq = arg.IndexOf('=');
            if (eq <= 0 || eq == arg.Length - 1)
            {
                Console.Error.WriteLine($"Nieprawidłowy argument '{arg}' (oczekiwano name=path).");
                Console.Error.WriteLine(Usage);
                return 2;
            }
            entries.Add((arg[..eq], arg[(eq + 1)..]));
        }

        try
        {
            File.Copy(args[0], args[1], overwrite: true);
            PayloadArchive.Append(args[1], entries);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            try { File.Delete(args[1]); } catch (Exception) { /* best effort */ }
            Console.Error.WriteLine($"Błąd: {ex.Message}");
            return 1;
        }
    }
}
