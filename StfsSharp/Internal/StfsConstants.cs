namespace StfsSharp.Internal;

/// <summary>Internal constants shared by the STFS and GOD/SVOD readers.</summary>
internal static class StfsConstants
{
    /// <summary>Size of one logical block and one disc sector in bytes (2 KB).</summary>
    internal const int SectorSize = 2048;

    /// <summary>Backstop against corrupt file-table entry counts.</summary>
    internal const int MaxEntries = 65536;

    /// <summary>Maximum accepted STFS directory nesting depth.</summary>
    internal const int MaxDirectoryDepth = 64;
}
