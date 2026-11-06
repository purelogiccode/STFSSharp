namespace StfsSharp.Tests;

/// <summary>
/// Read-only GOD/SVOD sector mapping: fragment selection, hash-table
/// interleave, the security-sector special case and the zero-filled prefix.
/// Fixtures are synthetic header plus <c>DataNNNN</c> files; no game data is
/// used.
/// </summary>
public sealed class GodVolumeTests
{
    [Fact]
    public void ReadsSectorsAndUnalignedRanges()
    {
        using TempDir work = new();
        string header = GodHeaderTests.CreateGodHeader(
            work.Path, "game", dataBlockCount: 0x22);
        string dataPath = CreateDataFile(header, "Data0000");

        byte[] sector0 = Pattern(1);
        byte[] sector1 = Pattern(2);
        byte[] security = Pattern(3);
        using (FileStream data = new(dataPath, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            WriteAt(data, 0x2000, sector0);
            WriteAt(data, 0x2800, sector1);
            WriteAt(data, 0x12000, security);
        }

        using GodVolume volume = GodVolume.Open(header);
        Assert.Equal(0x22 * 2 * 2048, volume.Length);

        byte[] actual = new byte[2048];
        Assert.Equal(2048, volume.Read(0, actual));
        Assert.Equal(sector0, actual);

        // A range crossing a sector boundary must be stitched from two sectors.
        byte[] crossed = new byte[512];
        Assert.Equal(512, volume.Read(0x700, crossed));
        Assert.Equal(sector0.AsSpan(0x700).ToArray(), crossed.AsSpan(0, 256).ToArray());
        Assert.Equal(sector1.AsSpan(0, 256).ToArray(), crossed.AsSpan(256, 256).ToArray());

        // Sector 0x20 is the security sector, stored outside the data stream.
        Assert.Equal(2048, volume.Read(0x20L * 2048, actual));
        Assert.Equal(security, actual);

        // The final read is bounded by the logical length.
        byte[] tail = new byte[4096];
        int tailRead = volume.Read(volume.Length - 100, tail);
        Assert.Equal(100, tailRead);
    }

    [Fact]
    public void ZeroFillsPrefixBeforeGameData()
    {
        using TempDir work = new();
        string header = GodHeaderTests.CreateGodHeader(
            work.Path, "prefix-game", startingBlock: 4, dataBlockCount: 1);
        CreateDataFile(header, "Data0000");

        using GodVolume volume = GodVolume.Open(header);
        byte[] buffer = new byte[2048];
        Assert.Equal(2048, volume.Read(0, buffer));
        Assert.All(buffer, static b => Assert.Equal(0, b));
        Assert.Equal(2048, volume.Read(2048, buffer));
        Assert.All(buffer, static b => Assert.Equal(0, b));
    }

    [Fact]
    public void EnhancedGdfUsesTheSecuritySectorArea()
    {
        using TempDir work = new();
        string header = GodHeaderTests.CreateGodHeader(
            work.Path, "enhanced", dataBlockCount: 0x22, enhancedGdf: true);
        string dataPath = CreateDataFile(header, "Data0000");

        byte[] gameMarker = Pattern(0x11);
        byte[] securityMarker = Pattern(0x22);
        using (FileStream data = new(dataPath, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            WriteAt(data, 0x2000, securityMarker);
            WriteAt(data, 0x3000, gameMarker);
        }

        using GodVolume volume = GodVolume.Open(header);
        byte[] buffer = new byte[2048];
        Assert.Equal(2048, volume.Read(0, buffer));
        Assert.Equal(gameMarker, buffer);
        Assert.Equal(2048, volume.Read(0x20L * 2048, buffer));
        Assert.Equal(securityMarker, buffer);
    }

    [Fact]
    public void MapsSecondFragment()
    {
        using TempDir work = new();
        const uint blocksPerFragment = 0xA1C4;
        string header = GodHeaderTests.CreateGodHeader(
            work.Path, "two-fragment", dataBlockCount: blocksPerFragment + 1,
            dataFileCount: 2);
        string dataDirectory = header + ".data";
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllBytes(Path.Combine(dataDirectory, "Data0000"), []);

        byte[] marker = Pattern(0x5A);
        string second = Path.Combine(dataDirectory, "Data0001");
        using (FileStream stream = new(second, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            WriteAt(stream, 0x2000, marker);
        }

        using GodVolume volume = GodVolume.Open(header);
        byte[] actual = new byte[2048];
        Assert.Equal(2048, volume.Read(blocksPerFragment * 2L * 2048, actual));
        Assert.Equal(marker, actual);
    }

    [Fact]
    public void Stream_SupportsSeekAndRead()
    {
        using TempDir work = new();
        string header = GodHeaderTests.CreateGodHeader(
            work.Path, "stream", dataBlockCount: 0x22);
        string dataPath = CreateDataFile(header, "Data0000");
        byte[] sector1 = Pattern(7);
        using (FileStream data = new(dataPath, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            WriteAt(data, 0x2800, sector1);
        }

        using GodVolume volume = GodVolume.Open(header);
        using GodVolumeStream stream = volume.OpenStream();
        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Equal(volume.Length, stream.Length);
        Assert.Equal(0, stream.Position);

        Assert.Equal(0x800, stream.Seek(0x800, SeekOrigin.Begin));
        byte[] buffer = new byte[2048];
        Assert.Equal(2048, stream.Read(buffer, 0, buffer.Length));
        Assert.Equal(sector1, buffer);
        Assert.Equal(0x1000, stream.Position);

        Assert.Equal(volume.Length - 1, stream.Seek(-1, SeekOrigin.End));
        Assert.Equal(0, stream.ReadByte());
        Assert.Equal(volume.Length, stream.Position);

        Assert.Throws<NotSupportedException>(() => stream.Write([1], 0, 1));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(1));
    }

    [Fact]
    public void TryOpen_RejectsInvalidContainers()
    {
        using TempDir work = new();
        string noVolume = GodHeaderTests.CreateGodHeader(
            work.Path, "stfs-volume", volumeType: 0);
        Directory.CreateDirectory(noVolume + ".data");
        File.WriteAllBytes(Path.Combine(noVolume + ".data", "Data0000"), []);
        Assert.False(GodVolume.TryOpen(noVolume, out _));

        string missingFragment = GodHeaderTests.CreateGodHeader(
            work.Path, "missing-fragment", dataFileCount: 2);
        Directory.CreateDirectory(missingFragment + ".data");
        File.WriteAllBytes(Path.Combine(missingFragment + ".data", "Data0000"), []);
        Assert.False(GodVolume.TryOpen(missingFragment, out _));

        Assert.False(GodVolume.TryOpen(work.File("does-not-exist"), out _));
        Assert.Throws<FileNotFoundException>(() =>
            GodVolume.Open(work.File("does-not-exist")));
    }

    [Fact]
    public void StfsFile_DetectsGodContainers()
    {
        using TempDir work = new();
        string header = GodHeaderTests.CreateGodHeader(work.Path, "container");
        Directory.CreateDirectory(header + ".data");
        File.WriteAllBytes(Path.Combine(header + ".data", "Data0000"), []);

        Assert.True(StfsFile.IsContainer(header));
        Assert.True(StfsFile.IsContainer(header + ".data"));
        using GodVolume volume = StfsFile.OpenGodVolume(header);
        Assert.Equal(header, volume.HeaderPath);
    }

    private static string CreateDataFile(string header, string name)
    {
        string directory = header + ".data";
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        using (FileStream stream = File.Create(path))
        {
            // Pre-size the fragment so reads anywhere in the synthetic
            // logical volume find backing bytes; tests overwrite the offsets
            // they assert on.
            stream.SetLength(1 << 20);
        }

        return path;
    }

    private static byte[] Pattern(byte value)
    {
        byte[] data = new byte[2048];
        for (int index = 0; index < data.Length; index++)
        {
            data[index] = (byte)(value + index);
        }

        return data;
    }

    private static void WriteAt(FileStream stream, long offset, byte[] data)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        stream.Write(data);
    }
}
