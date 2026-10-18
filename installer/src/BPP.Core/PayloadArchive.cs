using System.Buffers.Binary;
using System.Text.Json;

namespace BPP.Core;

public sealed record PayloadEntry(long Offset, long Length);

/// <summary>
/// Ładunek doklejany do exe: [exe][dane wpisów][indeks JSON][stopka 24 B].
/// Stopka: 8 B magic "BPPPAYL1", 8 B offset indeksu, 8 B długość indeksu (little-endian).
/// </summary>
public static class PayloadArchive
{
    private static readonly byte[] Magic = "BPPPAYL1"u8.ToArray();
    private const int FooterSize = 24;
    private const int ScanWindow = 1024 * 1024;
    private const int MaxIndexSize = 16 * 1024 * 1024;
    private const int BufferSize = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void Append(string exePath, IEnumerable<(string Name, string FilePath)> entries)
    {
        var index = new Dictionary<string, PayloadEntry>();
        using var exe = new FileStream(exePath, FileMode.Open, FileAccess.Write, FileShare.None, BufferSize);
        exe.Seek(0, SeekOrigin.End);

        foreach (var (name, filePath) in entries)
        {
            if (index.ContainsKey(name))
                throw new ArgumentException($"Zduplikowana nazwa wpisu: {name}", nameof(entries));
            var offset = exe.Position;
            using (var src = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize))
                src.CopyTo(exe, BufferSize);
            index.Add(name, new PayloadEntry(offset, exe.Position - offset));
        }

        var indexOffset = exe.Position;
        var indexBytes = JsonSerializer.SerializeToUtf8Bytes(index, Json);
        exe.Write(indexBytes);

        Span<byte> footer = stackalloc byte[FooterSize];
        Magic.CopyTo(footer);
        BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(8, 8), indexOffset);
        BinaryPrimitives.WriteInt64LittleEndian(footer.Slice(16, 8), indexBytes.Length);
        exe.Write(footer);
    }

    public static IReadOnlyDictionary<string, PayloadEntry> ReadIndex(string exePath)
    {
        using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var fileLen = fs.Length;
        if (fileLen < FooterSize) throw new InvalidDataException("Brak ładunku w pliku (plik zbyt krótki).");

        var windowLen = (int)Math.Min(fileLen, ScanWindow);
        var windowStart = fileLen - windowLen;
        var window = new byte[windowLen];
        fs.Seek(windowStart, SeekOrigin.Begin);
        fs.ReadExactly(window);

        // Od końca: pierwszy kandydat na stopkę, który przejdzie walidację, wygrywa.
        var searchEnd = windowLen - FooterSize + Magic.Length;
        while (searchEnd >= Magic.Length)
        {
            var rel = window.AsSpan(0, searchEnd).LastIndexOf(Magic);
            if (rel < 0) break;
            searchEnd = rel + Magic.Length - 1;
            var index = TryReadIndex(fs, window, rel, windowStart + rel);
            if (index is not null) return index;
        }
        throw new InvalidDataException("Brak ładunku w pliku (nie znaleziono prawidłowej stopki).");
    }

    private static Dictionary<string, PayloadEntry>? TryReadIndex(FileStream fs, byte[] window, int rel, long footerPos)
    {
        var indexOffset = BinaryPrimitives.ReadInt64LittleEndian(window.AsSpan(rel + 8, 8));
        var indexLength = BinaryPrimitives.ReadInt64LittleEndian(window.AsSpan(rel + 16, 8));
        if (indexOffset < 0 || indexLength <= 0 || indexLength > MaxIndexSize
            || indexOffset > footerPos || indexLength != footerPos - indexOffset)
            return null;

        var indexBytes = new byte[indexLength];
        fs.Seek(indexOffset, SeekOrigin.Begin);
        fs.ReadExactly(indexBytes);

        Dictionary<string, PayloadEntry>? index;
        try
        {
            index = JsonSerializer.Deserialize<Dictionary<string, PayloadEntry>>(indexBytes, Json);
        }
        catch (JsonException)
        {
            return null;
        }
        if (index is null) return null;

        foreach (var (_, e) in index)
            if (e is null || e.Offset < 0 || e.Length < 0 || e.Offset > indexOffset || e.Length > indexOffset - e.Offset)
                return null;
        return index;
    }

    public static Stream Open(string exePath, PayloadEntry e)
    {
        var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, BufferSize);
        try
        {
            if (e.Offset < 0 || e.Length < 0 || e.Offset > fs.Length || e.Length > fs.Length - e.Offset)
                throw new InvalidDataException("Wpis wykracza poza plik.");
            return new RangeStream(fs, e.Offset, e.Length);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    private sealed class RangeStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _start;
        private readonly long _length;
        private long _pos;

        public RangeStream(Stream inner, long start, long length)
        {
            _inner = inner;
            _start = start;
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _pos;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var remaining = _length - _pos;
            if (remaining <= 0 || buffer.IsEmpty) return 0;
            if (buffer.Length > remaining) buffer = buffer[..(int)remaining];
            _inner.Seek(_start + _pos, SeekOrigin.Begin);
            var n = _inner.Read(buffer);
            _pos += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _pos + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (target < 0) throw new IOException("Próba ustawienia pozycji przed początkiem wpisu.");
            _pos = target;
            return _pos;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
