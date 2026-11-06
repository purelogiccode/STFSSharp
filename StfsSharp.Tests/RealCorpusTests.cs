using System.IO.Compression;
using System.Text;
using StfsSharp.Models;

namespace StfsSharp.Tests;

/// <summary>
/// Optional validation against real retail packages. Disabled by default so CI
/// never needs game data; set <c>STFSSHARP_REAL_CORPUS=1</c> and point the
/// three corpus roots at local archives to run it. The archives are extracted
/// to a temp directory and never committed.
/// </summary>
public sealed class RealCorpusTests
{
    private const string DigitalRoot = @"I:\XBOX 360 Digital";
    private const string XblaRoot = @"I:\Xbox 360 XBLA";
    private const string XbligRoot = @"I:\Xbox 360 XBLIG";

    [Fact]
    public void XblaPackage_ListsReadsAndValidates()
    {
        if (!TryExtractArchive(XblaRoot, "Aegis Wing (World) (XBLA).zip", "*XBLA*.zip", out string root))
        {
            return;
        }

        using TempDir work = new();
        string? packagePath = FindStfsPackage(root);
        Assert.NotNull(packagePath);

        using StfsPackage package = StfsPackage.Open(packagePath);
        Assert.Equal(StfsPackageKind.Xbla, package.Info.Kind);
        Assert.True(package.Entries.Count > 0);
        Assert.True(File.Exists(packagePath));

        StfsEntry? file = package.Entries.FirstOrDefault(
            static entry => !entry.IsDirectory && entry.Size is > 0 and < 1_000_000);
        if (file is not null)
        {
            byte[] data = package.ReadAllBytes(file);
            Assert.Equal(file.Size, data.Length);
        }

        StfsValidationReport report = package.Validate();
        Assert.True(
            report.IsValid,
            string.Join("; ", report.Issues.Take(10).Select(static issue => $"{issue.Area}: {issue.Reason}")));
    }

    [Fact]
    public void XbligPackage_ListsReadsAndValidates()
    {
        if (!TryExtractArchive(XbligRoot, "1on1 Soccer (World) (XBLIG).zip", "*XBLIG*.zip", out string root))
        {
            return;
        }

        string? packagePath = FindStfsPackage(root);
        Assert.NotNull(packagePath);

        using StfsPackage package = StfsPackage.Open(packagePath);
        Assert.Equal(StfsPackageKind.Xblig, package.Info.Kind);
        Assert.True(package.Entries.Count > 0);

        StfsValidationReport report = package.Validate();
        Assert.True(
            report.IsValid,
            string.Join("; ", report.Issues.Take(10).Select(static issue => $"{issue.Area}: {issue.Reason}")));
    }

    [Fact]
    public void GodContainer_MapsRealFragments()
    {
        if (!TryExtractArchive(DigitalRoot, "Hulu Plus (World) (GOD).zip", "*GOD*.zip", out string root))
        {
            return;
        }

        string? headerPath = FindGodContainer(root);
        Assert.NotNull(headerPath);

        Assert.True(GodHeader.TryReadTitle(headerPath, out GodTitleInfo? title));
        Assert.NotNull(title);
        Assert.False(string.IsNullOrWhiteSpace(title.TitleName));

        Assert.True(GodHeader.TryReadVolume(headerPath, out GodVolumeInfo? info));
        Assert.NotNull(info);
        Assert.True(info.DataFileCount >= 1);
        Assert.True(info.Length > 0);

        using GodVolume volume = GodVolume.Open(headerPath);
        Assert.Equal(info.Length, volume.Length);

        // The logical image starts with the zero-filled region before the
        // game data; the XISO volume descriptor lives at 0x10000.
        byte[] sector = new byte[2048];
        Assert.Equal(2048, volume.Read(0, sector));
        Assert.All(sector, static b => Assert.Equal(0, b));

        byte[] descriptor = new byte[20];
        Assert.Equal(20, volume.Read(0x10000, descriptor));
        Assert.Equal("MICROSOFT*XBOX*MEDIA"u8.ToArray(), descriptor);

        // Random access must be stable across repeated reads.
        byte[] first = new byte[4096];
        byte[] second = new byte[4096];
        Assert.Equal(4096, volume.Read(0x20000, first));
        Assert.Equal(4096, volume.Read(0x20000, second));
        Assert.Equal(first, second);

        using GodVolumeStream stream = volume.OpenStream();
        stream.Seek(0x10000, SeekOrigin.Begin);
        byte[] viaStream = new byte[20];
        Assert.Equal(20, stream.Read(viaStream, 0, viaStream.Length));
        Assert.Equal(descriptor, viaStream);
    }

    private static bool TryExtractArchive(
        string root, string preferredName, string pattern, out string extracted)
    {
        extracted = string.Empty;
        if (!string.Equals(Environment.GetEnvironmentVariable("STFSSHARP_REAL_CORPUS"), "1",
                StringComparison.Ordinal) ||
            !Directory.Exists(root))
        {
            return false;
        }

        string preferred = Path.Combine(root, preferredName);
        string? archive = File.Exists(preferred)
            ? preferred
            : Directory.EnumerateFiles(root, pattern)
                .OrderBy(static path => new FileInfo(path).Length)
                .FirstOrDefault();
        if (archive is null)
        {
            return false;
        }

        extracted = Path.Combine(
            Path.GetTempPath(), "stfssharp-corpus", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extracted);
        ZipFile.ExtractToDirectory(archive, extracted);
        return true;
    }

    private static string? FindStfsPackage(string root)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (StfsPackage.IsStfsPackage(file))
            {
                return file;
            }
        }

        return null;
    }

    private static string? FindGodContainer(string root)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (GodHeader.TryReadVolume(file, out _))
            {
                return file;
            }
        }

        return null;
    }
}
