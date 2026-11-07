# API reference

All types live in the `StfsSharp` or `StfsSharp.Models` namespaces.

## Detection and factory

### `StfsFile`

| Member | Description |
|---|---|
| `IsContainer(string path)` | True for a GOD container or a readable XBLA/XBLIG/Community Games package. |
| `OpenGodVolume(string path, FileShare share = FileShare.Read)` | Opens a GOD/SVOD container. |
| `OpenPackage(string path, FileShare share = FileShare.Read)` | Opens a single-file STFS package. |

## STFS packages

### `StfsPackage : IDisposable`

| Member | Description |
|---|---|
| `static IsStfsPackage(string? path)` | Cheap header probe. |
| `static Open(string path, FileShare share = FileShare.Read)` | Opens a package; throws `InvalidDataException` for unsupported or corrupt files. |
| `static TryOpen(string path, out StfsPackage? package)` | Non-throwing open. |
| `Info` | `StfsPackageInfo` with title metadata and volume layout. |
| `Entries` | File-table entries in package order. |
| `Find(string? path)` | Case-insensitive lookup with `/` or `\` separators. |
| `ReadAllBytes(StfsEntry entry)` | Whole-file read (rejects files over `int.MaxValue`). |
| `CopyTo(StfsEntry entry, Stream destination)` | Streams one file. |
| `ReadFileChunks<TState>(entry, state, sink)` | Chunked read for hashing/checksums. |
| `ExtractTo(...)` | Extracts the tree with optional progress, warning and name policy. |
| `Validate(CancellationToken ct = default)` | Deep SHA-1 hash-tree validation. |

`StfsEntry` exposes `Path`, `Name`, `IsDirectory`, `Size`, `StartBlock`,
`BlockCount`, `IsContiguous`, `Created` and `Modified`.

`StfsPackageInfo` exposes the title fields (`Kind`, `TitleName`, `TitleId`,
`TitleIdHex`, `DiscNumber`, `DiscCount`, `ContentType`) and the volume layout
(`PackageLength`, `HeaderSize`, `FirstBlockOffset`, `TotalBlockCount`,
`FreeBlockCount`, `FileTableBlockCount`, `FileTableBlockNumber`,
`IsReadOnlyFormat`, `FileCount`, `DirectoryCount`, `TotalFileSize`).

### Extraction

```csharp
int ExtractTo(
    string outputDirectory,
    bool skipExisting = false,
    IProgress<StfsProgress>? progress = null,
    CancellationToken ct = default);

int ExtractTo(
    string outputDirectory,
    ExtractNamePolicy namePolicy,
    bool skipExisting = false,
    IProgress<StfsProgress>? progress = null,
    Action<string>? warning = null,
    CancellationToken ct = default);
```

`ExtractNamePolicy`:

| Value | Behaviour |
|---|---|
| `Reject` (default) | Throw `InvalidDataException` on an unsafe name. |
| `Sanitize` | Replace unsafe characters with `_` and disambiguate collisions with `~N`. |
| `Skip` | Skip the entry and, for a directory, its subtree. |

`StfsProgress` carries `Type` (`DirectoryAdded`/`FileAdded`), `Path`, `Sector`
and `Size`.

### Title metadata (`StfsHeader`)

| Member | Description |
|---|---|
| `static IsStfsPackage(string path)` | XContent + STFS volume type + XBLA/XBLIG/Community content type. |
| `static TryReadTitle(string path, out StfsTitleInfo? info)` | Reads the plaintext title fields. |
| `static ReadTitle(string path)` | Nullable convenience wrapper. |

## GOD/SVOD

### `GodHeader`

| Member | Description |
|---|---|
| `IsGodContainer(string path)` | Header + `.data` directory + GOD/NXE content type. |
| `TryReadVolume(string path, out GodVolumeInfo? info)` | Reads the SVOD mapping and checks every fragment exists. |
| `TryReadVolume(string path, FileShare share, out ...)` | Same with a caller-chosen share mode. |
| `TryReadTitle(string path, out GodTitleInfo? info)` | Reads the title fields. |
| `ReadTitle(string path)` | Nullable convenience wrapper. |

### `GodVolume : IDisposable`

| Member | Description |
|---|---|
| `static Open(string path)` / `Open(path, share)` | Opens the header and every fragment. |
| `static TryOpen(string path, out GodVolume? volume)` | Non-throwing open. |
| `Info` | `GodVolumeInfo` with the fragment list and mapping. |
| `HeaderPath`, `DataDirectory`, `Length` | Paths and logical length. |
| `Read(long offset, Span<byte> buffer)` | Random-access logical read. |
| `Read(long offset, byte[] buffer, int index, int count)` | Array overload. |
| `OpenStream()` | Returns a seekable read-only `GodVolumeStream`. |

### `GodVolumeStream : Stream`

`CanRead`/`CanSeek` are true; `CanWrite` is false; `Length`, `Position`,
`Read`, `ReadByte` and `Seek` are supported. `Write` and `SetLength` throw
`NotSupportedException`.

## Models

| Type | Purpose |
|---|---|
| `GodTitleInfo` | GOD title name, title ID, disc number/count. |
| `GodVolumeInfo` | Header/data paths, fragment count, start block, data block count, enhanced-GDF flag, length, parts. |
| `StfsTitleInfo` | XBLA/XBLIG title name, title ID, disc number/count, content type. |
| `StfsPackageKind` | `Xbla` or `Xblig`. |
| `StfsPackageInfo` | Package title and volume summary. |
| `StfsEntry` | One file-table entry. |
| `StfsValidationIssue` | `Area` + `Reason`. |
| `StfsValidationReport` | `IsValid`, counts and the issue list. |
| `StfsProgress`, `StfsProgressKind` | Extraction progress. |
| `ExtractNamePolicy` | Reject/sanitize/skip. |

## Exceptions

| Operation | Throws |
|---|---|
| `StfsPackage.Open` | `ArgumentNullException`, `ArgumentException`, `FileNotFoundException`, `InvalidDataException`, `UnauthorizedAccessException`, `IOException`. |
| `GodVolume.Open` | Same shape as `StfsPackage.Open`. |
| `ReadAllBytes` | `ArgumentException` for directories, `IOException` for oversized files, `InvalidDataException` for truncated data. |
| `ExtractTo` | `InvalidDataException` for unsafe names/truncation, `IOException` for destination errors, `OperationCanceledException` for cancellation. |
| `Validate` | `ObjectDisposedException`, `OperationCanceledException`. |

`TryOpen` / `TryRead*` return `false` instead of throwing for expected format
and I/O failures.
