using System.Security.Cryptography;
using BPP.Core;

namespace BPP.Core.Tests;

public sealed class PayloadArchiveTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("bpp-payload-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, byte[] data)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static byte[] Random(int n, int seed)
    {
        var b = new byte[n];
        new System.Random(seed).NextBytes(b);
        return b;
    }

    private static byte[] ReadAll(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void RoundTrip_TwoEntries_ByteForByte()
    {
        var exe = Write("setup.exe", Random(5000, 1));
        var a = Random(12345, 2);
        var b = Random(70000, 3);
        PayloadArchive.Append(exe, new[]
        {
            ("rootfs", Write("a.bin", a)),
            ("BPP.exe", Write("b.bin", b)),
        });

        var index = PayloadArchive.ReadIndex(exe);
        Assert.Equal(2, index.Count);
        Assert.Equal(a.Length, index["rootfs"].Length);
        Assert.Equal(b.Length, index["BPP.exe"].Length);
        using (var s = PayloadArchive.Open(exe, index["rootfs"])) Assert.Equal(a, ReadAll(s));
        using (var s = PayloadArchive.Open(exe, index["BPP.exe"])) Assert.Equal(b, ReadAll(s));
    }

    [Fact]
    public void Append_PreservesOriginalExeBytes()
    {
        var original = Random(3000, 4);
        var exe = Write("setup.exe", original);
        PayloadArchive.Append(exe, new[] { ("x", Write("x.bin", Random(10, 5))) });
        Assert.Equal(original, File.ReadAllBytes(exe)[..original.Length]);
    }

    [Fact]
    public void ReadIndex_SurvivesTrailingSignatureBytes()
    {
        var exe = Write("setup.exe", Random(2000, 6));
        var a = Random(4096, 7);
        PayloadArchive.Append(exe, new[] { ("rootfs", Write("a.bin", a)) });
        using (var fs = new FileStream(exe, FileMode.Append))
            fs.Write(Random(16 * 1024, 8));

        var index = PayloadArchive.ReadIndex(exe);
        using var s = PayloadArchive.Open(exe, index["rootfs"]);
        Assert.Equal(a, ReadAll(s));
    }

    [Fact]
    public void ReadIndex_DecoyMagicAfterRealFooter_FindsRealIndex()
    {
        var exe = Write("setup.exe", Random(2000, 20));
        var a = Random(2048, 21);
        PayloadArchive.Append(exe, new[] { ("rootfs", Write("a.bin", a)) });
        using (var fs = new FileStream(exe, FileMode.Append))
        {
            fs.Write("BPPPAYL1"u8);
            fs.Write(Random(500, 22));
            fs.Write("BPPPAYL1"u8);
            fs.Write(Random(40, 23));
        }

        var index = PayloadArchive.ReadIndex(exe);
        using var s = PayloadArchive.Open(exe, index["rootfs"]);
        Assert.Equal(a, ReadAll(s));
    }

    [Fact]
    public void ReadIndex_WorksWhileFileOpenWithDeleteSharing()
    {
        var exe = Write("setup.exe", Random(100, 24));
        PayloadArchive.Append(exe, new[] { ("x", Write("x.bin", new byte[] { 1 })) });
        using var other = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Assert.Single(PayloadArchive.ReadIndex(exe));
    }

    [Fact]
    public void Pack_FailedAppend_DeletesPartialOutput()
    {
        var setup = Write("setup.exe", Random(100, 25));
        var good = Write("good.bin", Random(100, 26));
        var outExe = Path.Combine(_dir, "out.exe");
        var dll = Path.Combine(AppContext.BaseDirectory, "BPP.Pack.dll");
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in new[] { dll, setup, outExe, $"ok={good}", $"bad={Path.Combine(_dir, "missing.bin")}" })
            psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(1, p.ExitCode);
        Assert.False(File.Exists(outExe));
    }

    [Fact]
    public void ReadIndex_NoPayload_Throws()
    {
        var exe = Write("plain.exe", Random(5000, 9));
        Assert.Throws<InvalidDataException>(() => PayloadArchive.ReadIndex(exe));
    }

    [Fact]
    public void ReadIndex_TinyFile_Throws()
    {
        var exe = Write("tiny.exe", new byte[] { 1, 2, 3 });
        Assert.Throws<InvalidDataException>(() => PayloadArchive.ReadIndex(exe));
    }

    [Fact]
    public void ReadIndex_MagicWithGarbageOffsets_Throws()
    {
        var data = new byte[200];
        "BPPPAYL1"u8.CopyTo(data.AsSpan(100));
        BitConverter.GetBytes(long.MaxValue).CopyTo(data, 108);
        BitConverter.GetBytes(long.MaxValue).CopyTo(data, 116);
        var exe = Write("garbage.exe", data);
        Assert.Throws<InvalidDataException>(() => PayloadArchive.ReadIndex(exe));
    }

    [Fact]
    public void Open_IsBoundedToEntryRange()
    {
        var exe = Write("setup.exe", Random(100, 10));
        PayloadArchive.Append(exe, new[]
        {
            ("one", Write("1.bin", new byte[] { 1, 1, 1, 1 })),
            ("two", Write("2.bin", new byte[] { 2, 2, 2 })),
        });
        var index = PayloadArchive.ReadIndex(exe);
        using var s = PayloadArchive.Open(exe, index["one"]);
        Assert.Equal(4, s.Length);
        Assert.Equal(new byte[] { 1, 1, 1, 1 }, ReadAll(s));
        Assert.Equal(0, s.Read(new byte[8]));
    }

    [Fact]
    public void Append_DuplicateName_Throws()
    {
        var exe = Write("setup.exe", Random(100, 11));
        var f = Write("f.bin", new byte[] { 1 });
        Assert.Throws<ArgumentException>(() => PayloadArchive.Append(exe, new[] { ("a", f), ("a", f) }));
    }

    [Fact]
    [Trait("Category", "Heavy")]
    public void LargeEntry_300MB_StaysUnder100MBWorkingMemory()
    {
        const int chunk = 1024 * 1024;
        const int chunks = 300;
        var buf = Random(chunk, 12);
        var big = Path.Combine(_dir, "big.bin");
        string expectedHash;
        using (var fs = File.Create(big))
        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            for (var i = 0; i < chunks; i++)
            {
                fs.Write(buf);
                sha.AppendData(buf);
            }
            expectedHash = Convert.ToHexString(sha.GetHashAndReset());
        }
        var exe = Write("setup.exe", Random(1000, 13));

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var peak = before;
        PayloadArchive.Append(exe, new[] { ("big", big) });
        peak = Math.Max(peak, GC.GetTotalMemory(false));

        var index = PayloadArchive.ReadIndex(exe);
        Assert.Equal((long)chunk * chunks, index["big"].Length);
        string actualHash;
        using (var s = PayloadArchive.Open(exe, index["big"]))
        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var rb = new byte[81920];
            int n;
            while ((n = s.Read(rb)) > 0)
            {
                sha.AppendData(rb, 0, n);
                peak = Math.Max(peak, GC.GetTotalMemory(false));
            }
            actualHash = Convert.ToHexString(sha.GetHashAndReset());
        }
        var after = GC.GetTotalMemory(forceFullCollection: true);

        Assert.Equal(expectedHash, actualHash);
        Assert.True(peak - before < 100L * 1024 * 1024, $"peak delta {peak - before} bytes");
        Assert.True(after - before < 100L * 1024 * 1024, $"after delta {after - before} bytes");
    }
}
