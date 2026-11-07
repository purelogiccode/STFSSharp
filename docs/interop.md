# Interoperability and validation

## Synthetic fixtures

`StfsSharp.Tests/Fixtures/SyntheticStfsPackage.cs` builds deterministic
XBLA/XBLIG packages with:

- a valid XContent header;
- real level-0 (and level-1 for packages over 170 logical blocks) SHA-1 hash
  tables;
- contiguous and non-contiguous block chains;
- read-only and writable-format hash-table layouts, including the secondary
  active index;
- empty files, empty directories, nested directories and FAT timestamps.

The same builder is linked into `StfsSharp.Benchmarks` and `StfsSharp.Fuzz`.
No copyrighted game data is stored in the repository.

## Real-corpus checks

Optional tests validate retail packages when `STFSSHARP_REAL_CORPUS=1` is set.
They extract one archive per family into a temp directory, then exercise the
readers. The 2026-11-07 run used:

| Family | Archive | Result |
|---|---|---|
| XBLA | `Aegis Wing (World) (XBLA).zip` | Open, list, read a file and deep-validate passed. |
| XBLIG | `1on1 Soccer (World) (XBLIG).zip` | Open, list and deep-validate passed. |
| GOD | `Hulu Plus (World) (GOD).zip` | Volume mapping, title metadata, zero prefix, `MICROSOFT*XBOX*MEDIA` at `0x10000`, repeated reads and stream reads passed. |

The test roots are `I:\Xbox 360 XBLA`, `I:\Xbox 360 XBLIG` and
`I:\XBOX 360 Digital` on the development machine. They are not part of the
repository or CI.

A small "Scramble" XBLA archive in the same corpus is a truncated/sparse
package: it declares 5953 logical blocks but stores only 337. `Validate`
correctly reports the mismatch; the real-corpus XBLA test pins a complete
package instead.

## XISOSharp integration

XISOSharp consumes StfsSharp and keeps its historical public types as facades:

- `XISOSharp.GodHeader`
- `XISOSharp.BlockDevice.GodBlockDevice`
- `XISOSharp.StfsHeader`
- `XISOSharp.StfsPackage`
- `XISOSharp.Models.*`

The facades map StfsSharp models to the existing XISOSharp records, so
`title-info`, `list`, `extract`, `checksum` and `-V` output stays compatible.
The facades are scheduled for removal in XISOSharp 2.0.0.

## Known limitations

- Read-only. There is no STFS writer, rebuild or re-signing support.
- Saves, profiles and DLC packages are out of scope; only XBLA, XBLIG and
  Community Games packages are accepted.
- `Validate` verifies the hash tree, not the RSA signature chain.
- GOD/SVOD reading exposes the logical disc image; it does not parse the FATX
  filesystem inside it.
- The reader does not cache fragment handles across volumes; each `GodVolume`
  owns its open `FileStream` instances.
