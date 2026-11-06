using System.Buffers.Binary;
using System.Text;
using StfsSharp.Models;

namespace StfsSharp.Tests;

/// <summary>
/// GOD (SVOD) header title metadata. A GOD container is a header file beside a
/// <c>&lt;header&gt;.data</c> directory; the plaintext XContent header carries
/// the title name, title ID and disc number/count.
/// </summary>
public sealed class GodHeaderTests
{
    private const uint SignatureLive = 0x4C495645;
    private const uint ContentTypeGod = 0x7000;

    [Fact]
    public void TryReadTitle_ReadsHeaderPathAndDataFolder()
    {
        using TempDir work = new();
        (string header, string data) = CreateGodContainer(
            work.Path, "4D5307E6", BuildGodHeader("Halo 3"));

        Assert.True(GodHeader.IsGodContainer(header));
        Assert.True(GodHeader.IsGodContainer(data));
        Assert.True(GodHeader.TryReadTitle(header, out GodTitleInfo? info));
        Assert.NotNull(info);
        Assert.Equal(header, info.HeaderPath);
        Assert.Equal("Halo 3", info.TitleName);
        Assert.Equal(0x4D5307E6u, info.TitleId);
        Assert.Equal("0x4D5307E6", info.TitleIdHex);
        Assert.Equal(1, info.DiscNumber);
        Assert.Equal(2, info.DiscCount);

        Assert.True(GodHeader.TryReadTitle(data, out GodTitleInfo? fromData));
        Assert.Equal(info, fromData);
        Assert.Equal(info, GodHeader.ReadTitle(header));
    }

    [Fact]
    public void TryReadTitle_FallsBackToTitleIdWhenNameEmpty()
    {
        using TempDir work = new();
        (string header, _) = CreateGodContainer(
            work.Path, "game", BuildGodHeader(string.Empty, 0x12345678, discNumber: 1, discCount: 1));

        Assert.True(GodHeader.TryReadTitle(header, out GodTitleInfo? info));
        Assert.NotNull(info);
        Assert.Equal("12345678", info.TitleName);
    }

    [Fact]
    public void TryReadTitle_SanitizesFatxUnsafeName()
    {
        using TempDir work = new();
        (string header, _) = CreateGodContainer(
            work.Path, "game", BuildGodHeader("Halo: Combat / Evolved\u00AE. "));

        Assert.True(GodHeader.TryReadTitle(header, out GodTitleInfo? info));
        Assert.NotNull(info);
        Assert.Equal("Halo_ Combat _ Evolved", info.TitleName);
    }

    [Fact]
    public void IsGodContainer_RejectsNonGodFiles()
    {
        using TempDir work = new();
        string notGod = work.File("notgod");
        File.WriteAllBytes(notGod, new byte[GodHeader.MinimumHeaderLength]);
        Directory.CreateDirectory(notGod + ".data");

        Assert.False(GodHeader.IsGodContainer(notGod));
        Assert.False(GodHeader.TryReadTitle(notGod, out _));
        Assert.Null(GodHeader.ReadTitle(notGod));

        string noData = work.File("nodata");
        File.WriteAllBytes(noData, BuildGodHeader("No Data"));
        Assert.False(GodHeader.IsGodContainer(noData));

        string truncated = work.File("truncated");
        File.WriteAllBytes(truncated, BuildGodHeader("Short")[..0x100]);
        Directory.CreateDirectory(truncated + ".data");
        Assert.False(GodHeader.IsGodContainer(truncated));
        Assert.False(GodHeader.TryReadTitle(truncated, out _));
    }

    [Fact]
    public void TryReadVolume_RejectsInvalidHeadersAndMissingFragments()
    {
        using TempDir work = new();

        string noVolume = CreateGodHeader(work.Path, "stfs-volume", volumeType: 0);
        Directory.CreateDirectory(noVolume + ".data");
        Assert.False(GodHeader.TryReadVolume(noVolume, out _));

        string missing = CreateGodHeader(work.Path, "missing-fragment", dataFileCount: 2);
        Directory.CreateDirectory(missing + ".data");
        File.WriteAllBytes(Path.Combine(missing + ".data", "Data0000"), []);
        Assert.False(GodHeader.TryReadVolume(missing, out _));
        Assert.True(GodHeader.IsGodContainer(missing));

        string zeroBlocks = CreateGodHeader(work.Path, "zero-blocks", dataBlockCount: 0);
        Directory.CreateDirectory(zeroBlocks + ".data");
        File.WriteAllBytes(Path.Combine(zeroBlocks + ".data", "Data0000"), []);
        Assert.False(GodHeader.TryReadVolume(zeroBlocks, out _));

        string notGod = work.File("plain");
        File.WriteAllBytes(notGod, new byte[GodHeader.MinimumHeaderLength]);
        Directory.CreateDirectory(notGod + ".data");
        Assert.False(GodHeader.TryReadVolume(notGod, out _));
    }

    [Fact]
    public void TryReadVolume_ReportsFragmentMapping()
    {
        using TempDir work = new();
        string header = CreateGodHeader(work.Path, "volume", dataBlockCount: 3, startingBlock: 4);
        string dataDirectory = header + ".data";
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllBytes(Path.Combine(dataDirectory, "Data0000"), []);

        Assert.True(GodHeader.TryReadVolume(header, out GodVolumeInfo? info));
        Assert.NotNull(info);
        Assert.Equal(header, info.HeaderPath);
        Assert.Equal(dataDirectory, info.DataDirectory);
        Assert.Equal(1u, info.DataFileCount);
        Assert.Equal(4u, info.StartingBlock);
        Assert.Equal(3u, info.DataBlockCount);
        Assert.False(info.EnhancedGdf);
        Assert.Equal((3L + 4) * 2 * 2048, info.Length);
        Assert.Equal([header, Path.Combine(dataDirectory, "Data0000")], info.Parts);
    }

    internal static byte[] BuildGodHeader(
        string titleName,
        uint titleId = 0x4D5307E6,
        byte discNumber = 1,
        byte discCount = 2,
        uint dataBlockCount = 1,
        uint startingBlock = 0,
        uint dataFileCount = 1,
        uint volumeType = 1,
        bool enhancedGdf = false)
    {
        byte[] header = new byte[GodHeader.MinimumHeaderLength];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), SignatureLive);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x344), ContentTypeGod);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(GodHeader.TitleIdOffset), titleId);
        header[GodHeader.DiscNumberOffset] = discNumber;
        header[GodHeader.DiscCountOffset] = discCount;

        byte[] name = Encoding.BigEndianUnicode.GetBytes(titleName);
        name.AsSpan(0, Math.Min(name.Length, GodHeader.TitleNameLength))
            .CopyTo(header.AsSpan(GodHeader.TitleNameOffset));

        header[0x391] = enhancedGdf ? (byte)0x40 : (byte)0;
        WriteUInt24LittleEndian(header, 0x392, dataBlockCount);
        WriteUInt24LittleEndian(header, 0x395, startingBlock);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x39D), dataFileCount);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x3A9), volumeType);
        return header;
    }

    internal static string CreateGodHeader(
        string work,
        string name,
        string title = "GOD Test",
        uint startingBlock = 0,
        uint dataBlockCount = 1,
        uint dataFileCount = 1,
        uint volumeType = 1,
        bool enhancedGdf = false)
    {
        string headerPath = Path.Combine(work, name);
        File.WriteAllBytes(headerPath, BuildGodHeader(
            title, dataBlockCount: dataBlockCount, startingBlock: startingBlock,
            dataFileCount: dataFileCount, volumeType: volumeType, enhancedGdf: enhancedGdf));
        return headerPath;
    }

    private static (string Header, string Data) CreateGodContainer(
        string work, string name, byte[] header)
    {
        string headerPath = Path.Combine(work, name);
        File.WriteAllBytes(headerPath, header);
        string dataPath = headerPath + ".data";
        Directory.CreateDirectory(dataPath);
        return (headerPath, dataPath);
    }

    private static void WriteUInt24LittleEndian(Span<byte> buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
    }
}
