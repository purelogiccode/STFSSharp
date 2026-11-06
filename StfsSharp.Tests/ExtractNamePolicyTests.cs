using System.Text;
using StfsSharp.Models;
using StfsSharp.TestDataGenerator;

namespace StfsSharp.Tests;

/// <summary>Extraction name safety for STFS package entries.</summary>
public sealed class ExtractNamePolicyTests
{
    [WindowsFact]
    public void SanitizeAndSkipPolicies_HandleUnsafeNames()
    {
        using TempDir work = new();
        string packagePath = work.File("game");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("safe.bin", [1, 2, 3]),
        ]);

        // Rename the file-table entry in place: a colon is a valid STFS name
        // (the parser only rejects structural separators) but drive-relative
        // on Windows, so extraction needs the policy.
        byte[] bytes = File.ReadAllBytes(packagePath);
        int entryNameOffset = 0xB000 + 0x1000; // file table is data block 0
        Encoding.ASCII.GetBytes("a:b.bin").CopyTo(bytes.AsSpan(entryNameOffset));
        bytes[entryNameOffset + 0x28] = 7;
        File.WriteAllBytes(packagePath, bytes);

        using StfsPackage package = StfsPackage.Open(packagePath);

        string sanitized = work.File("sanitized");
        Assert.Equal(1, package.ExtractTo(sanitized, ExtractNamePolicy.Sanitize));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(sanitized, "a_b.bin")));

        string skipped = work.File("skipped");
        Assert.Equal(0, package.ExtractTo(skipped, ExtractNamePolicy.Skip));
        Assert.False(File.Exists(Path.Combine(skipped, "a_b.bin")));

        Assert.Throws<InvalidDataException>(() =>
            package.ExtractTo(work.File("rejected")));
    }

    [WindowsFact]
    public void SanitizePolicy_DisambiguatesCollidingNames()
    {
        using TempDir work = new();
        string packagePath = work.File("game");
        SyntheticStfsPackage.Create(packagePath, [
            new SyntheticStfsFile("first.bin", [1]),
            new SyntheticStfsFile("a_b.bin", [2]),
        ]);
        // The fixture rejects drive-relative colons, so patch the second
        // entry's name bytes in place after building it.
        byte[] bytes = File.ReadAllBytes(packagePath);
        int secondEntry = 0xB000 + 0x1000 + 0x40;
        Encoding.ASCII.GetBytes("a:b.bin").CopyTo(bytes.AsSpan(secondEntry));
        bytes[secondEntry + 0x28] = 7;
        File.WriteAllBytes(packagePath, bytes);

        using StfsPackage package = StfsPackage.Open(packagePath);
        string output = work.File("out");
        Assert.Equal(2, package.ExtractTo(output, ExtractNamePolicy.Sanitize));
        Assert.Equal([2], File.ReadAllBytes(Path.Combine(output, "a_b.bin")));
        Assert.Equal([1], File.ReadAllBytes(Path.Combine(output, "a_b~1.bin")));
    }
}
