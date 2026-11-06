namespace StfsSharp.Models;

/// <summary>Type of a structured progress event emitted during STFS extraction.</summary>
public enum StfsProgressKind
{
    /// <summary>A directory was created. Payload: <see cref="StfsProgress.Path"/> and <see cref="StfsProgress.Sector"/>.</summary>
    DirectoryAdded,

    /// <summary>A file was written. Payload: <see cref="StfsProgress.Path"/>, <see cref="StfsProgress.Sector"/> and <see cref="StfsProgress.Size"/>.</summary>
    FileAdded,
}

/// <summary>
/// A structured progress event emitted during STFS package extraction.
/// Delivered through <see cref="IProgress{T}"/> so consumers can drive progress
/// bars and tree views from one channel.
/// </summary>
/// <param name="Type">The kind of event; determines which payload fields are populated.</param>
/// <param name="Path">Package-relative path with a leading <c>/</c> (for example <c>/data/file.bin</c>).</param>
/// <param name="Sector">First logical STFS data block of the entry.</param>
/// <param name="Size">Byte size written for <see cref="StfsProgressKind.FileAdded"/>; 0 for directories.</param>
public readonly record struct StfsProgress(
    StfsProgressKind Type,
    string? Path = null,
    long Sector = 0,
    long Size = 0);
