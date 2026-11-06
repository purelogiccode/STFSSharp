using System.Buffers.Binary;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using StfsSharp.TestDataGenerator;

namespace StfsSharp.Benchmarks;

/// <summary>
/// End-to-end STFS/GOD benchmarks over deterministic synthetic fixtures. No
/// game data is used.
/// </summary>
[MemoryDiagnoser]
public class StfsBenchmarks
{
    private string _root = string.Empty;
    private string _package = string.Empty;
    private string _godHeader = string.Empty;
    private byte[] _readBuffer = [];

    [GlobalSetup]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "stfssharp-bench", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _package = Path.Combine(_root, "game");
        SyntheticStfsPackage.Create(_package, [
            new SyntheticStfsFile("default.xex", CreatePayload(64 * 1024)),
            new SyntheticStfsFile("data/large.bin", CreatePayload(4 * 1024 * 1024)),
            new SyntheticStfsFile("data/small.bin", CreatePayload(4096)),
        ], extraDirectories: ["empty-dir"]);

        _godHeader = CreateGodVolume(_root);
        _readBuffer = new byte[64 * 1024];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    [Benchmark]
    public long OpenAndList()
    {
        using StfsPackage package = StfsPackage.Open(_package);
        return package.Entries.Count + package.Info.TotalFileSize;
    }

    [Benchmark]
    public int Validate()
    {
        using StfsPackage package = StfsPackage.Open(_package);
        return package.Validate().BlocksChecked;
    }

    [Benchmark]
    public int ReadLargeFile()
    {
        using StfsPackage package = StfsPackage.Open(_package);
        return package.ReadAllBytes(package.Find("data/large.bin")!).Length;
    }

    [Benchmark]
    public int Extract()
    {
        using StfsPackage package = StfsPackage.Open(_package);
        string output = Path.Combine(_root, "extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            return package.ExtractTo(output);
        }
        finally
        {
            try
            {
                Directory.Delete(output, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }

    [Benchmark]
    public int GodRandomReads()
    {
        using GodVolume volume = GodVolume.Open(_godHeader);
        int total = 0;
        for (long offset = 0; offset < volume.Length; offset += 1 << 20)
        {
            total += volume.Read(offset, _readBuffer);
        }

        return total;
    }

    private static byte[] CreatePayload(int length)
    {
        byte[] data = new byte[length];
        for (int index = 0; index < length; index++)
        {
            data[index] = (byte)((index * 31) + 7);
        }

        return data;
    }

    private static string CreateGodVolume(string root)
    {
        string header = Path.Combine(root, "god");
        const uint dataBlockCount = 0x400;
        byte[] headerBytes = new byte[GodHeader.MinimumHeaderLength];
        BinaryPrimitives.WriteUInt32BigEndian(headerBytes.AsSpan(0), 0x4C495645);
        BinaryPrimitives.WriteUInt32BigEndian(headerBytes.AsSpan(0x344), 0x7000);
        BinaryPrimitives.WriteUInt32BigEndian(headerBytes.AsSpan(GodHeader.TitleIdOffset), 0x4D5307E6);
        headerBytes[GodHeader.DiscNumberOffset] = 1;
        headerBytes[GodHeader.DiscCountOffset] = 1;
        WriteUInt24LittleEndian(headerBytes, 0x392, dataBlockCount);
        WriteUInt24LittleEndian(headerBytes, 0x395, 0);
        BinaryPrimitives.WriteUInt32BigEndian(headerBytes.AsSpan(0x39D), 1);
        BinaryPrimitives.WriteUInt32BigEndian(headerBytes.AsSpan(0x3A9), 1);
        File.WriteAllBytes(header, headerBytes);

        string dataDirectory = header + ".data";
        Directory.CreateDirectory(dataDirectory);
        using FileStream data = File.Create(Path.Combine(dataDirectory, "Data0000"));
        data.SetLength(8 << 20);
        data.Position = 0x2000;
        byte[] block = new byte[0x198 * 2048];
        RandomNumberGenerator.Fill(block);
        data.Write(block);

        return header;
    }

    private static void WriteUInt24LittleEndian(Span<byte> buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
    }
}
