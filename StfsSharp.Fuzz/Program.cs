using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using StfsSharp.Models;
using StfsSharp.TestDataGenerator;

namespace StfsSharp.Fuzz;

/// <summary>
/// Bounded, deterministic mutation fuzzer for the STFS package and GOD/SVOD
/// readers. It starts from synthetic valid containers (no copyrighted game
/// data), mutates bytes or truncates them, then drives every public read API
/// and fails on any exception outside the documented format/I/O contract.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        int seconds = ReadInt(args, "--seconds", 30);
        int iterationsLimit = ReadInt(args, "--iterations", 0);
        int seed = ReadInt(args, "--seed", Environment.TickCount);
        Random random = new(seed);
        Console.WriteLine($"FUZZ: seed={seed} seconds={seconds} iterations={iterationsLimit}");

        string root = Path.Combine(Path.GetTempPath(), $"stfs_fuzz_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            List<FuzzTarget> targets =
            [
                new("stfs", CreateStfs(root), IsGod: false),
                new("god", CreateGod(root), IsGod: true),
            ];

            Stopwatch clock = Stopwatch.StartNew();
            int iterations = 0;
            int failures = 0;
            while (iterationsLimit <= 0
                       ? clock.Elapsed < TimeSpan.FromSeconds(seconds)
                       : iterations < iterationsLimit)
            {
                iterations++;
                FuzzTarget target = targets[random.Next(targets.Count)];
                string mutated = Path.Combine(
                    root, $"mutated_{iterations}{Path.GetExtension(target.Path)}");
                CleanupVariant(mutated);
                try
                {
                    if (target.IsGod)
                    {
                        MutateGod(target.Path, mutated, random);
                    }
                    else
                    {
                        MutateFile(target.Path, mutated, random);
                    }

                    RunChecks(target, mutated, root, iterations);
                }
                catch (Exception ex) when (IsExpected(ex))
                {
                    // Expected: corrupt input is rejected by the documented contract.
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.Error.WriteLine(
                        $"FUZZ FAILURE format={target.Format} iteration={iterations} seed={seed}");
                    Console.Error.WriteLine(ex);
                    if (failures >= 5)
                    {
                        break;
                    }
                }
                finally
                {
                    CleanupVariant(mutated);
                }
            }

            Console.WriteLine($"FUZZ: iterations={iterations} failures={failures}");
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void RunChecks(FuzzTarget target, string path, string root, int iteration)
    {
        if (target.IsGod)
        {
            using GodVolume volume = GodVolume.Open(path);
            byte[] buffer = new byte[64 * 1024];
            _ = volume.Read(0, buffer);
            if (volume.Length > 0)
            {
                _ = volume.Read(volume.Length / 2, buffer);
                _ = volume.Read(Math.Max(0, volume.Length - 100), buffer);
            }

            using GodVolumeStream stream = volume.OpenStream();
            stream.Seek(0, SeekOrigin.Begin);
            _ = stream.Read(buffer, 0, buffer.Length);
            return;
        }

        using StfsPackage package = StfsPackage.Open(path);
        _ = package.Validate();
        foreach (StfsEntry entry in package.Entries)
        {
            if (entry.IsDirectory || entry.Size > 1_000_000)
            {
                continue;
            }

            byte[] data = package.ReadAllBytes(entry);
            if (data.Length != entry.Size)
            {
                throw new InvalidOperationException("STFS entry size mismatch.");
            }
        }

        string extract = Path.Combine(root, $"extract_{iteration}");
        try
        {
            package.ExtractTo(extract);
        }
        finally
        {
            TryDeleteDirectory(extract);
        }
    }

    private static string CreateStfs(string root)
    {
        string path = Path.Combine(root, "base.stfs");
        SyntheticStfsPackage.Create(path, [
            new SyntheticStfsFile("default.xex", "XEX2fuzz"u8.ToArray()),
            new SyntheticStfsFile("data/payload.bin", RandomNumberGenerator.GetBytes(9000)),
            new SyntheticStfsFile("data/empty.bin", []),
        ], extraDirectories: ["empty-dir"]);
        return path;
    }

    private static string CreateGod(string root)
    {
        string header = Path.Combine(root, "base.god");
        const uint dataBlockCount = 0x22;
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
        data.SetLength(0x24000);
        data.Position = 0x2000;
        RandomNumberGenerator.Fill(headerBytes);
        data.Write(headerBytes);
        return header;
    }

    private static void MutateGod(string sourceHeader, string destinationHeader, Random random)
    {
        Directory.CreateDirectory(destinationHeader + ".data");
        foreach (string fragment in Directory.EnumerateFiles(sourceHeader + ".data"))
        {
            File.Copy(
                fragment,
                Path.Combine(destinationHeader + ".data", Path.GetFileName(fragment)),
                overwrite: true);
        }

        MutateFile(sourceHeader, destinationHeader, random);
        string dataFile = Path.Combine(destinationHeader + ".data", "Data0000");
        if (File.Exists(dataFile) && random.Next(4) == 0)
        {
            MutateFile(dataFile, dataFile, random);
        }
    }

    private static void MutateFile(string source, string destination, Random random)
    {
        byte[] data = File.ReadAllBytes(source);
        int operation = random.Next(10);
        if (operation < 6)
        {
            int flips = random.Next(1, 9);
            for (int index = 0; index < flips && data.Length > 0; index++)
            {
                int offset = random.Next(data.Length);
                data[offset] ^= (byte)(1 << random.Next(8));
            }
        }
        else if (operation < 8 && data.Length > 64)
        {
            Array.Resize(ref data, random.Next(64, data.Length));
        }
        else if (operation < 9)
        {
            int extra = random.Next(1, 4097);
            int original = data.Length;
            Array.Resize(ref data, original + extra);
            random.NextBytes(data.AsSpan(original));
        }
        else if (data.Length > 0)
        {
            int start = random.Next(data.Length);
            int length = Math.Min(data.Length - start, random.Next(1, 4097));
            data.AsSpan(start, length).Clear();
        }

        File.WriteAllBytes(destination, data);
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or FormatException;

    private static int ReadInt(string[] args, string name, int fallback)
    {
        for (int index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture,
                    out int value))
            {
                return value;
            }
        }

        return fallback;
    }

    private static void WriteUInt24LittleEndian(Span<byte> buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
    }

    private static void CleanupVariant(string path)
    {
        TryDeleteFile(path);
        TryDeleteDirectory(path + ".data");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private sealed record FuzzTarget(string Format, string Path, bool IsGod);
}
