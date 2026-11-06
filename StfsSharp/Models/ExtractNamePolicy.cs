namespace StfsSharp.Models;

/// <summary>
/// How extraction handles a package entry name that is unsafe on the
/// destination host: path separators, drive-relative colons, trailing dots or
/// spaces, and DOS device names. The default (<see cref="Reject"/>) preserves
/// the historical fail-fast behavior.
/// </summary>
public enum ExtractNamePolicy
{
    /// <summary>
    /// Abort with an <see cref="InvalidDataException"/> when an entry name
    /// cannot be represented safely on the host. This is the default.
    /// </summary>
    Reject = 0,

    /// <summary>
    /// Replace host-unsafe characters with <c>'_'</c> and append a <c>~N</c>
    /// suffix when the sanitized name collides with another entry in the same
    /// directory. The extracted tree remains complete, but names may differ
    /// from the package.
    /// </summary>
    Sanitize = 1,

    /// <summary>
    /// Skip the unsafe entry and continue with its siblings. A skipped
    /// directory also skips its whole subtree.
    /// </summary>
    Skip = 2,
}
