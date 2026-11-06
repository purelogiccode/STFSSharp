# StfsSharp

[![CI](https://github.com/purelogiccode/STFSSharp/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/purelogiccode/STFSSharp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/StfsSharp.svg)](https://www.nuget.org/packages/StfsSharp)
[![NuGet Downloads](https://img.shields.io/nuget/dt/StfsSharp.svg)](https://www.nuget.org/packages/StfsSharp)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512bd4.svg)](https://dotnet.microsoft.com/download)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-0078d7.svg)](https://github.com/purelogiccode/STFSSharp)
![Trimmable](https://img.shields.io/badge/trimming-compatible-success)
![AOT](https://img.shields.io/badge/AOT-compatible-success)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**StfsSharp** is a pure-managed, read-only C# library for Xbox 360 **STFS**
packages and **GOD/SVOD** Games-on-Demand containers. It is an independent MIT
implementation of the on-disk structures used by Xbox Live Arcade (XBLA),
Xbox Live Indie Games (XBLIG) and Community Games packages, plus the SVOD
fragment layout used by Games-on-Demand installs.

It has **zero runtime dependencies**, is trimmable, AOT-compatible, and targets
`net8.0`, `net9.0` and `net10.0`.

## Features

- **Single-file STFS packages** — open XBLA/XBLIG/Community Games packages,
  list the file table, read files, stream file data and extract the whole tree.
- **GOD/SVOD containers** — open a header file or its `.data` directory,
  map `DataNNNN` fragments, and read the logical disc image with random access
  or through a seekable `Stream`.
- **Title metadata** — read the plaintext XContent header fields (title name,
  title ID, disc number/count and content type) from STFS packages and GOD
  containers without decoding data sectors.
- **Deep validation** — verify every logical block against its level-0 SHA-1
  hash, walk every file block chain, verify the level-0/1/2 hash tables, the
  top hash and the header hash.
- **Safe extraction** — reject, sanitize or skip host-unsafe entry names, with
  collision disambiguation and optional progress/warning channels.
- **No game data required** — the test suite and fuzzer build deterministic
  synthetic containers; optional real-corpus tests run only when enabled.

## Requirements

| Requirement | Value |
|---|---|
| Target frameworks | `net8.0`, `net9.0`, `net10.0` |
| Runtime | .NET 8, .NET 9 or .NET 10 |
| Platforms | Windows, Linux, macOS |
| Dependencies | none |

## Installation

```sh
dotnet add package StfsSharp
```

## Quick start

### List and extract an XBLA/XBLIG package

```csharp
using StfsSharp;
using StfsSharp.Models;

using StfsPackage package = StfsPackage.Open(@"C:\games\5841083C\000D0000\ABC...");

Console.WriteLine($"{package.Info.TitleName} ({package.Info.TitleIdHex})");
foreach (StfsEntry entry in package.Entries)
{
    Console.WriteLine($"{(entry.IsDirectory ? "dir " : "file")} {entry.Path} {entry.Size}");
}

byte[] defaultXex = package.ReadAllBytes(package.Find("default.xex")!);
package.ExtractTo(@"C:\games\extracted");
```

### Read title metadata

```csharp
if (StfsHeader.TryReadTitle(packagePath, out StfsTitleInfo? title))
{
    Console.WriteLine($"{title.TitleName} — {title.TitleIdHex}");
}

if (GodHeader.TryReadTitle(godHeaderPath, out GodTitleInfo? god))
{
    Console.WriteLine($"{god.TitleName} disc {god.DiscNumber}/{god.DiscCount}");
}
```

### Open a GOD/SVOD container

```csharp
using GodVolume volume = GodVolume.Open(@"D:\content\4D5307E6");
Console.WriteLine($"{volume.Length} bytes across {volume.Info.DataFileCount} fragment(s)");

byte[] sector = new byte[2048];
volume.Read(0x10000, sector);

using GodVolumeStream stream = volume.OpenStream();
stream.Seek(0x10000, SeekOrigin.Begin);
```

### Validate the STFS hash tree

```csharp
using StfsPackage package = StfsPackage.Open(packagePath);
StfsValidationReport report = package.Validate();
if (!report.IsValid)
{
    foreach (StfsValidationIssue issue in report.Issues)
    {
        Console.WriteLine($"{issue.Area}: {issue.Reason}");
    }
}
```

### Detect either container family

```csharp
if (StfsFile.IsContainer(path))
{
    if (GodHeader.IsGodContainer(path))
    {
        using GodVolume volume = StfsFile.OpenGodVolume(path);
    }
    else
    {
        using StfsPackage package = StfsFile.OpenPackage(path);
    }
}
```

## Public API

| Area | Types |
|---|---|
| STFS packages | `StfsPackage`, `StfsEntry`, `StfsPackageInfo`, `StfsPackageKind`, `StfsTitleInfo`, `StfsHeader` |
| GOD/SVOD | `GodHeader`, `GodVolume`, `GodVolumeStream`, `GodVolumeInfo`, `GodTitleInfo` |
| Detection | `StfsFile` |
| Validation | `StfsValidationReport`, `StfsValidationIssue` |
| Extraction | `ExtractNamePolicy`, `StfsProgress`, `StfsProgressKind` |

See [`docs/api.md`](docs/api.md) for the full surface.

## Supported content

| Content | Supported |
|---|---|
| XBLA packages (`0x000D0000`) | ✅ read-only |
| XBLIG packages (`0x00000002`) | ✅ read-only |
| Community Games (`0x02000000`) | ✅ read-only, treated as XBLIG |
| GOD/SVOD containers (`0x7000`, NXE `0x4000`) | ✅ read-only |
| STFS saves/profiles/DLC | ❌ out of scope |
| STFS writing, rebuild, re-signing | ❌ out of scope |
| Signature verification | ❌ out of scope (`Validate` is structural/hash-tree only) |

## Build and test

```sh
dotnet build StfsSharp.sln -c Release
dotnet test StfsSharp.Tests/StfsSharp.Tests.csproj -c Release
dotnet format whitespace StfsSharp.sln --verify-no-changes --no-restore
dotnet pack StfsSharp/StfsSharp.csproj -c Release -o artifacts
```

Optional real-corpus validation (paths are local and never committed):

```powershell
$env:STFSSHARP_REAL_CORPUS = '1'
dotnet test StfsSharp.Tests/StfsSharp.Tests.csproj -c Release `
  --filter "FullyQualifiedName~RealCorpusTests"
```

The fuzzer runs from a synthetic valid container with no game data:

```sh
dotnet run --project StfsSharp.Fuzz -c Release -- --seconds 60
```

## Related projects

- [XISOSharp](https://github.com/purelogiccode/XISOSharp) — Xbox disc-image
  creation, extraction and conversion. Consumes StfsSharp for GOD/STFS input.
- [CCISharp](https://github.com/purelogiccode/CCISharp) — CCI/CCIM images.
- [ZArchiveSharp](https://github.com/purelogiccode/ZArchiveSharp) — ZAR archives.

## License

MIT. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
