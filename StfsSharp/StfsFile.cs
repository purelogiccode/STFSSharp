namespace StfsSharp;

/// <summary>
/// Detects and opens the two read-only XContent container families supported by
/// StfsSharp: single-file STFS packages (XBLA/XBLIG/Community Games) and
/// GOD/SVOD fragment sets. This is a convenience factory; use
/// <see cref="StfsPackage"/> or <see cref="GodVolume"/> directly when the
/// container kind is already known.
/// </summary>
public static class StfsFile
{
    /// <summary>
    /// Returns true when <paramref name="path"/> is a GOD/SVOD container or a
    /// readable single-file XBLA/XBLIG/Community Games STFS package.
    /// </summary>
    public static bool IsContainer(string path) =>
        GodHeader.IsGodContainer(path) || StfsPackage.IsStfsPackage(path);

    /// <summary>Opens a GOD/SVOD container.</summary>
    /// <param name="path">GOD header path or the matching <c>.data</c> directory.</param>
    /// <param name="share">File sharing mode for the header and fragments.</param>
    public static GodVolume OpenGodVolume(string path, FileShare share = FileShare.Read) =>
        GodVolume.Open(path, share);

    /// <summary>Opens a single-file XBLA/XBLIG/Community Games STFS package.</summary>
    /// <param name="path">Path to the package file.</param>
    /// <param name="share">File sharing mode for the package file.</param>
    public static StfsPackage OpenPackage(string path, FileShare share = FileShare.Read) =>
        StfsPackage.Open(path, share);
}
