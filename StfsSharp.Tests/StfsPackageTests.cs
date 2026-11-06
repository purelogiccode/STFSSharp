using System.Buffers.Binary;
using System.Text;
using StfsSharp.Models;
using StfsSharp.TestDataGenerator;

namespace StfsSharp.Tests;

/// <summary>
/// Read-only XBLA/XBLIG single-file STFS package support: file table parsing,
/// hash-table block mapping, file reads, extraction and hash-tree validation.
/// Fixtures are synthetic packages built by <see cref="SyntheticStfsPackage"/>;
/// no game data is used.
/// </summary>
public sealed class StfsPackageTests
{
    [Fact]
    public void Open_ReadsPackageInfoAndEntries()
    {
        using TempDir work = new();
        byte[] payload = CreatePayload(5000);
        string packagePath = work.File("game");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("default.xex", [1, 2, 3]),
            new SyntheticStfsFile("data/large.bin", payload),
            new SyntheticStfsFile("data/empty.bin", []),
            new SyntheticStfsFile("readme.txt", Encoding.UTF8.GetBytes("hi")),
        ], extraDirectories: ["empty-dir"]);

        Assert.True(StfsPackage.IsStfsPackage(packagePath));
        Assert.True(StfsPackage.TryOpen(packagePath, out StfsPackage? package));
        using (package)
        {
            Assert.NotNull(package);
            Assert.Equal(StfsPackageKind.Xbla, package.Info.Kind);
            Assert.Equal("Synthetic STFS", package.Info.TitleName);
            Assert.Equal(0x584109B7u, package.Info.TitleId);
            Assert.Equal(4, package.Info.FileCount);
            Assert.Equal(2, package.Info.DirectoryCount);
            Assert.Equal(5000 + 3 + 2, package.Info.TotalFileSize);
            Assert.True(package.Info.IsReadOnlyFormat);

            string[] paths = package.Entries.Select(static entry => entry.Path).ToArray();
            Array.Sort(paths, StringComparer.Ordinal);
            Assert.Equal(
                ["data", "data/empty.bin", "data/large.bin", "default.xex", "empty-dir", "readme.txt"],
                paths);
            StfsEntry large = Assert.IsType<StfsEntry>(package.Find("data/large.bin"));
            Assert.Equal(5000L, large.Size);
            Assert.Equal(2u, large.BlockCount);
            Assert.True(large.IsContiguous);
            Assert.NotNull(large.Created);
            Assert.NotNull(large.Modified);
            Assert.Equal(payload, package.ReadAllBytes(large));

            Assert.Empty(package.ReadAllBytes(package.Find("data/empty.bin")!));
            Assert.Equal([1, 2, 3], package.ReadAllBytes(package.Find("default.xex")!));
            Assert.Equal(package.Find("data/large.bin"), package.Find("\\DATA\\LARGE.BIN"));
            Assert.Null(package.Find("missing.bin"));
        }
    }

    [Fact]
    public void ReadAllBytes_FollowsNonContiguousBlockChain()
    {
        using TempDir work = new();
        byte[] first = CreatePayload(0x1800);
        byte[] second = CreatePayload(0x2400);
        string packagePath = work.File("game");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("a.bin", first),
            new SyntheticStfsFile("b.bin", second),
        ], nonContiguousPath: "a.bin");

        using StfsPackage package = StfsPackage.Open(packagePath);
        StfsEntry a = package.Find("a.bin")!;
        Assert.False(a.IsContiguous);
        Assert.Equal(2u, a.BlockCount);
        Assert.Equal(first, package.ReadAllBytes(a));
        Assert.Equal(second, package.ReadAllBytes(package.Find("b.bin")!));
    }

    [Fact]
    public void Open_ReadsWritableFormatPrimaryAndSecondaryHashTables()
    {
        using TempDir work = new();
        byte[] payload = CreatePayload(0x2200);
        foreach (bool rootActive in new[] { false, true })
        {
            string packagePath = work.File($"writable-{rootActive}");
            SyntheticStfsPackage.Create(packagePath, [
                new SyntheticStfsFile("payload.bin", payload),
            ], readOnlyFormat: false, rootActiveIndex: rootActive);

            using StfsPackage package = StfsPackage.Open(packagePath);
            Assert.False(package.Info.IsReadOnlyFormat);
            Assert.Equal(payload, package.ReadAllBytes(package.Find("payload.bin")!));
        }
    }

    [Fact]
    public void Open_ReadsXbligPackage()
    {
        using TempDir work = new();
        string packagePath = work.File("indie");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("default.xex", [0x58, 0x45, 0x58, 0x32]),
        ], xblig: true, titleName: "Indie Game");

        using StfsPackage package = StfsPackage.Open(packagePath);
        Assert.Equal(StfsPackageKind.Xblig, package.Info.Kind);
        Assert.Equal("Indie Game", package.Info.TitleName);
        Assert.Equal(0x00000002u, package.Info.ContentType);

        byte[] bytes = File.ReadAllBytes(packagePath);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x344), 0x02000000);
        string community = WriteVariant(work.Path, "community", bytes);
        using StfsPackage communityPackage = StfsPackage.Open(community);
        Assert.Equal(StfsPackageKind.Xblig, communityPackage.Info.Kind);
        Assert.Equal(0x02000000u, communityPackage.Info.ContentType);
        Assert.True(StfsHeader.TryReadTitle(community, out StfsTitleInfo? communityTitle));
        Assert.NotNull(communityTitle);
        Assert.Equal(StfsPackageKind.Xblig, communityTitle.Kind);
    }

    [Fact]
    public void ExtractTo_WritesTreeAndSkipsExistingFiles()
    {
        using TempDir work = new();
        byte[] payload = CreatePayload(9000);
        string packagePath = work.File("game");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("default.xex", [1, 2, 3]),
            new SyntheticStfsFile("data/large.bin", payload),
            new SyntheticStfsFile("data/empty.bin", []),
        ]);

        using StfsPackage package = StfsPackage.Open(packagePath);
        string output = work.File("out");
        Assert.Equal(3, package.ExtractTo(output));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(output, "default.xex")));
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(output, "data", "large.bin")));
        Assert.Empty(File.ReadAllBytes(Path.Combine(output, "data", "empty.bin")));

        File.WriteAllText(Path.Combine(output, "default.xex"), "keep");
        File.Delete(Path.Combine(output, "data", "large.bin"));
        Assert.Equal(1, package.ExtractTo(output, skipExisting: true));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(output, "default.xex")));
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(output, "data", "large.bin")));
    }

    [Fact]
    public void ExtractTo_ReportsProgressAndWarnings()
    {
        using TempDir work = new();
        string packagePath = work.File("game");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("data/large.bin", CreatePayload(3000)),
        ], extraDirectories: ["empty-dir"]);

        using StfsPackage package = StfsPackage.Open(packagePath);
        List<StfsProgress> events = [];
        Assert.Equal(1, package.ExtractTo(work.File("out"), progress: new CaptureProgress(events)));
        Assert.Contains(events, static e => e.Type == StfsProgressKind.DirectoryAdded && e.Path == "/data");
        Assert.Contains(events, static e => e.Type == StfsProgressKind.FileAdded &&
                                            e.Path == "/data/large.bin" && e.Size == 3000);
    }

    [Fact]
    public void Open_RejectsCorruptPackages()
    {
        using TempDir work = new();
        string packagePath = work.File("game");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("a.bin", CreatePayload(3000)),
        ]);

        byte[] valid = File.ReadAllBytes(packagePath);

        byte[] badMagic = (byte[])valid.Clone();
        badMagic[0] = (byte)'X';
        Assert.False(StfsPackage.IsStfsPackage(WriteVariant(work.Path, "bad-magic", badMagic)));
        Assert.Throws<InvalidDataException>(() => StfsPackage.Open(WriteVariant(work.Path, "bad-magic", badMagic)));

        byte[] svod = (byte[])valid.Clone();
        svod[0x3A9] = 1;
        Assert.False(StfsPackage.IsStfsPackage(WriteVariant(work.Path, "svod", svod)));

        byte[] badTable = (byte[])valid.Clone();
        badTable[0x37E] = 0xFF;
        badTable[0x37F] = 0xFF;
        badTable[0x380] = 0xFF;
        Assert.Throws<InvalidDataException>(() => StfsPackage.Open(WriteVariant(work.Path, "bad-table", badTable)));

        byte[] truncated = valid[..0x9000];
        Assert.Throws<InvalidDataException>(() => StfsPackage.Open(WriteVariant(work.Path, "truncated", truncated)));

        byte[] multiFile = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(multiFile.AsSpan(0x39D), 2);
        Assert.Throws<InvalidDataException>(() => StfsPackage.Open(WriteVariant(work.Path, "multi-file", multiFile)));
    }

    [Fact]
    public void Open_RejectsUnsafeEntryNames()
    {
        using TempDir work = new();
        string packagePath = work.File("game");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("safe.bin", [1, 2, 3]),
        ]);

        byte[] package = File.ReadAllBytes(packagePath);
        int entryNameOffset = 0xB000 + 0x1000; // file table is data block 0
        Encoding.ASCII.GetBytes("..\\evil.bin").CopyTo(package.AsSpan(entryNameOffset));
        package[entryNameOffset + 0x28] = 0x0B;
        string unsafePackage = WriteVariant(work.Path, "unsafe", package);

        Assert.Throws<InvalidDataException>(() => StfsPackage.Open(unsafePackage));
    }

    [Fact]
    public void Open_ReadsAcrossHashLevelBoundary()
    {
        using TempDir work = new();
        byte[] payload = CreatePayload((171 * 0x1000) + 123);
        foreach (bool readOnly in new[] { true, false })
        {
            foreach (bool rootActive in readOnly ? new[] { false } : new[] { false, true })
            {
                string packagePath = work.File($"boundary-{readOnly}-{rootActive}");
                SyntheticStfsPackage.Create(packagePath, [
                    new SyntheticStfsFile("boundary.bin", payload),
                    new SyntheticStfsFile("tail.bin", CreatePayload(0x2000)),
                ], readOnlyFormat: readOnly, rootActiveIndex: rootActive);

                using StfsPackage package = StfsPackage.Open(packagePath);
                Assert.True(package.Info.TotalBlockCount > 170);
                Assert.Equal(payload, package.ReadAllBytes(package.Find("boundary.bin")!));
                Assert.Equal(
                    CreatePayload(0x2000),
                    package.ReadAllBytes(package.Find("tail.bin")!));
            }
        }
    }

    [Fact]
    public void ReadFileChunks_StreamsTheFile()
    {
        using TempDir work = new();
        byte[] payload = CreatePayload(9000);
        string packagePath = work.File("game");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("payload.bin", payload),
        ]);

        using StfsPackage package = StfsPackage.Open(packagePath);
        using MemoryStream destination = new();
        package.ReadFileChunks(package.Find("payload.bin")!, destination,
            static (state, buffer, offset, count) => state.Write(buffer, offset, count));
        Assert.Equal(payload, destination.ToArray());
    }

    [Fact]
    public void Validate_ReportsValidPackage()
    {
        using TempDir work = new();
        byte[] payload = CreatePayload((171 * 0x1000) + 123);
        foreach (bool readOnly in new[] { true, false })
        {
            foreach (bool rootActive in readOnly ? new[] { false } : new[] { false, true })
            {
                string packagePath = work.File($"valid-{readOnly}-{rootActive}");
                SyntheticStfsPackage.Create(packagePath, [
                    new SyntheticStfsFile("default.xex", [1, 2, 3]),
                    new SyntheticStfsFile("data/big.bin", payload),
                ], readOnlyFormat: readOnly, rootActiveIndex: rootActive);

                using StfsPackage package = StfsPackage.Open(packagePath);
                StfsValidationReport report = package.Validate();

                Assert.True(
                    report.IsValid,
                    string.Join("; ", report.Issues.Select(static issue => $"{issue.Area}: {issue.Reason}")));
                Assert.Equal(2, report.FilesChecked);
                Assert.Equal((int)package.Info.TotalBlockCount, report.BlocksChecked);
                Assert.True(report.HashTablesChecked >= 1);
                Assert.Equal(package.Info.TotalBlockCount * 0x1000L, report.BytesChecked);
            }
        }
    }

    [Fact]
    public void Validate_DetectsCorruptDataBlock()
    {
        using TempDir work = new();
        string packagePath = work.File("game");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("payload.bin", CreatePayload(5000)),
        ]);

        StfsPackageInfo info;
        using (StfsPackage package = StfsPackage.Open(packagePath))
        {
            info = package.Info;
        }

        byte[] bytes = File.ReadAllBytes(packagePath);
        int dataOffset = DataBlockOffset(info, 1); // block 0 is the file table
        bytes[dataOffset + 17] ^= 0x5A;

        using StfsPackage corrupt = StfsPackage.Open(WriteVariant(work.Path, "corrupt-data", bytes));
        StfsValidationReport report = corrupt.Validate();

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, static issue =>
            string.Equals(issue.Area, "block 1", StringComparison.Ordinal) &&
            issue.Reason.Contains("SHA-1", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DetectsCorruptHashTableAndTopHash()
    {
        using TempDir work = new();
        string packagePath = work.File("game");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("payload.bin", CreatePayload(3000)),
        ]);

        StfsPackageInfo info;
        using (StfsPackage package = StfsPackage.Open(packagePath))
        {
            info = package.Info;
        }

        byte[] corruptTable = File.ReadAllBytes(packagePath);
        corruptTable[(int)info.FirstBlockOffset] ^= 0xFF; // first level-0 hash byte
        using (StfsPackage corrupt = StfsPackage.Open(
                   WriteVariant(work.Path, "corrupt-table", corruptTable)))
        {
            StfsValidationReport report = corrupt.Validate();
            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, static issue =>
                string.Equals(issue.Area, "block 0", StringComparison.Ordinal) &&
                issue.Reason.Contains("SHA-1", StringComparison.Ordinal));
        }

        byte[] corruptTop = File.ReadAllBytes(packagePath);
        corruptTop[0x381] ^= 0xFF;
        using (StfsPackage corrupt = StfsPackage.Open(
                   WriteVariant(work.Path, "corrupt-top", corruptTop)))
        {
            StfsValidationReport report = corrupt.Validate();
            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, static issue =>
                string.Equals(issue.Area, "top hash", StringComparison.Ordinal));
        }

        byte[] corruptHeader = File.ReadAllBytes(packagePath);
        corruptHeader[0x32C] ^= 0xFF;
        using (StfsPackage corrupt = StfsPackage.Open(
                   WriteVariant(work.Path, "corrupt-header", corruptHeader)))
        {
            StfsValidationReport report = corrupt.Validate();
            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, static issue =>
                string.Equals(issue.Area, "header hash", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Validate_DetectsCorruptLevel0TableInLevel1Hash()
    {
        using TempDir work = new();
        string packagePath = work.File("game");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("payload.bin", CreatePayload((171 * 0x1000) + 123)),
        ]);

        StfsPackageInfo info;
        using (StfsPackage package = StfsPackage.Open(packagePath))
        {
            info = package.Info;
        }

        byte[] bytes = File.ReadAllBytes(packagePath);
        bytes[(int)info.FirstBlockOffset] ^= 0xFF; // level-0 table entry 0 hash

        using StfsPackage corrupt = StfsPackage.Open(WriteVariant(work.Path, "corrupt-level0", bytes));
        StfsValidationReport report = corrupt.Validate();

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, static issue =>
            string.Equals(issue.Area, "block 0", StringComparison.Ordinal));
        Assert.Contains(report.Issues, static issue =>
            issue.Area.StartsWith("hash table level 0", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DetectsBrokenBlockChain()
    {
        using TempDir work = new();
        string packagePath = work.File("game");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("payload.bin", CreatePayload(5000)),
        ]);

        StfsPackageInfo info;
        using (StfsPackage package = StfsPackage.Open(packagePath))
        {
            info = package.Info;
        }

        // The file starts at logical block 1 and spans two blocks. Point its
        // level-0 next-block field at EndOfChain so the declared chain is short.
        int entryOffset = checked((int)info.FirstBlockOffset) + ((1 % 170) * 0x18) + 0x14;
        byte[] bytes = File.ReadAllBytes(packagePath);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(entryOffset), 0x00FFFFFF);

        using StfsPackage corrupt = StfsPackage.Open(WriteVariant(work.Path, "short-chain", bytes));
        StfsValidationReport report = corrupt.Validate();

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, static issue =>
            string.Equals(issue.Area, "payload.bin", StringComparison.Ordinal) &&
            issue.Reason.Contains("block chain ends after 1 of 2", StringComparison.Ordinal));

        // Point the same entry at itself to exercise the cycle guard.
        byte[] cycle = File.ReadAllBytes(packagePath);
        BinaryPrimitives.WriteUInt32BigEndian(cycle.AsSpan(entryOffset), 0x00000001);
        using StfsPackage cyclic = StfsPackage.Open(WriteVariant(work.Path, "cycle", cycle));
        StfsValidationReport cycleReport = cyclic.Validate();

        Assert.False(cycleReport.IsValid);
        Assert.Contains(cycleReport.Issues, static issue =>
            string.Equals(issue.Area, "payload.bin", StringComparison.Ordinal) &&
            issue.Reason.Contains("cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsImplausibleBlockCount()
    {
        using TempDir work = new();
        string packagePath = work.File("game");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("payload.bin", CreatePayload(3000)),
        ]);

        byte[] bytes = File.ReadAllBytes(packagePath);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x395), 0xFFFFFF);

        using StfsPackage corrupt = StfsPackage.Open(WriteVariant(work.Path, "huge-block-count", bytes));
        StfsValidationReport report = corrupt.Validate();

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, static issue =>
            string.Equals(issue.Area, "volume", StringComparison.Ordinal));
    }

    private static int DataBlockOffset(StfsPackageInfo info, uint logicalBlock)
    {
        int tablesPerLevel = info.IsReadOnlyFormat ? 1 : 2;
        long baseSize = 170;
        long mapped = logicalBlock;
        for (int level = 0; level < 3; level++)
        {
            mapped += ((logicalBlock + baseSize) / baseSize) * tablesPerLevel;
            if (logicalBlock < baseSize)
            {
                break;
            }

            baseSize *= 170;
        }

        return checked((int)(info.FirstBlockOffset + (mapped * 0x1000)));
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

    private static string WriteVariant(string work, string name, byte[] data)
    {
        string path = Path.Combine(work, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    private sealed class CaptureProgress(List<StfsProgress> events) : IProgress<StfsProgress>
    {
        public void Report(StfsProgress value) => events.Add(value);
    }
}
