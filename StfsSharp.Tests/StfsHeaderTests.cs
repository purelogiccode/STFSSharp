using System.Buffers.Binary;
using System.Text;
using StfsSharp.Models;

namespace StfsSharp.Tests;

/// <summary>
/// Read-only title metadata from single-file XBLA/XBLIG STFS packages. These
/// packages have no <c>.data</c> directory; the package file itself starts
/// with the same XContent header used by GOD containers.
/// </summary>
public sealed class StfsHeaderTests
{
    private const uint SignatureLive = 0x4C495645;
    private const uint ContentTypeXbla = 0x000D0000;
    private const uint ContentTypeXblig = 0x00000002;

    [Fact]
    public void TryReadTitle_ReadsXblaAndXbligPackages()
    {
        using TempDir work = new();
        string xbla = CreatePackage(work.Path, "xbla", BuildHeader(
            "0 day Attack on Earth", ContentTypeXbla, 0x584109B7));
        string xblig = CreatePackage(work.Path, "xblig", BuildHeader(
            "0 Gravity Y3030", ContentTypeXblig, 0x584E07D2));

        Assert.True(StfsHeader.IsStfsPackage(xbla));
        Assert.True(StfsHeader.TryReadTitle(xbla, out StfsTitleInfo? xblaInfo));
        Assert.NotNull(xblaInfo);
        Assert.Equal(Path.GetFullPath(xbla), xblaInfo.PackagePath);
        Assert.Equal(StfsPackageKind.Xbla, xblaInfo.Kind);
        Assert.Equal("0 day Attack on Earth", xblaInfo.TitleName);
        Assert.Equal(0x584109B7u, xblaInfo.TitleId);
        Assert.Equal("0x584109B7", xblaInfo.TitleIdHex);
        Assert.Equal(ContentTypeXbla, xblaInfo.ContentType);
        Assert.Equal(0, xblaInfo.DiscNumber);
        Assert.Equal(0, xblaInfo.DiscCount);
        Assert.Equal(xblaInfo, StfsHeader.ReadTitle(xbla));

        Assert.True(StfsHeader.TryReadTitle(xblig, out StfsTitleInfo? xbligInfo));
        Assert.NotNull(xbligInfo);
        Assert.Equal(StfsPackageKind.Xblig, xbligInfo.Kind);
        Assert.Equal("0 Gravity Y3030", xbligInfo.TitleName);
        Assert.Equal(0x584E07D2u, xbligInfo.TitleId);
        Assert.Equal(ContentTypeXblig, xbligInfo.ContentType);

        // XBLA/XBLIG packages are not GOD containers.
        Assert.False(GodHeader.IsGodContainer(xbla));
        Assert.False(GodHeader.TryReadTitle(xbla, out _));
    }

    [Fact]
    public void TryReadTitle_FallsBackToTitleIdWhenNameEmpty()
    {
        using TempDir work = new();
        string package = CreatePackage(work.Path, "empty", BuildHeader(
            string.Empty, ContentTypeXbla, 0x12345678));

        Assert.True(StfsHeader.TryReadTitle(package, out StfsTitleInfo? info));
        Assert.NotNull(info);
        Assert.Equal("12345678", info.TitleName);
    }

    [Fact]
    public void TryReadTitle_RejectsGodAndUnknownContentTypes()
    {
        using TempDir work = new();
        string god = CreatePackage(work.Path, "god", BuildHeader(
            "GOD Title", 0x7000, 0x4D5307E6));
        string unknown = CreatePackage(work.Path, "unknown", BuildHeader(
            "Unknown", 0x00000001, 0x4D5307E6));
        string notXContent = work.File("notxcontent");
        File.WriteAllBytes(notXContent, new byte[GodHeader.MinimumHeaderLength]);

        Assert.False(StfsHeader.IsStfsPackage(god));
        Assert.False(StfsHeader.TryReadTitle(god, out _));
        Assert.False(StfsHeader.IsStfsPackage(unknown));
        Assert.False(StfsHeader.TryReadTitle(unknown, out _));
        Assert.False(StfsHeader.IsStfsPackage(notXContent));
        Assert.False(StfsHeader.TryReadTitle(notXContent, out _));
    }

    private static byte[] BuildHeader(
        string titleName,
        uint contentType,
        uint titleId,
        byte discNumber = 0,
        byte discCount = 0)
    {
        byte[] header = new byte[GodHeader.MinimumHeaderLength];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), SignatureLive);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x344), contentType);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(GodHeader.TitleIdOffset), titleId);
        header[GodHeader.DiscNumberOffset] = discNumber;
        header[GodHeader.DiscCountOffset] = discCount;

        byte[] name = Encoding.BigEndianUnicode.GetBytes(titleName);
        name.AsSpan(0, Math.Min(name.Length, GodHeader.TitleNameLength))
            .CopyTo(header.AsSpan(GodHeader.TitleNameOffset));
        return header;
    }

    private static string CreatePackage(string work, string name, byte[] header)
    {
        string path = Path.Combine(work, name);
        File.WriteAllBytes(path, header);
        return path;
    }
}
