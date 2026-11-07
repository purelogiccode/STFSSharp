# 1.0.0

_Release date: 2026-11-07._

Initial extraction of the XISOSharp GOD/STFS readers into a standalone package.

- Read-only XBLA/XBLIG/Community Games STFS package support: file table,
  contiguous and hash-chain block lists, `ReadAllBytes`, `CopyTo`,
  `ReadFileChunks` and `ExtractTo`.
- Deep STFS validation: per-block SHA-1, level-0/1/2 hash tables, top hash and
  header hash.
- GOD/SVOD container support: XContent title metadata, SVOD fragment mapping,
  random-access reads and a seekable `GodVolumeStream`.
- Safe extraction policies (reject/sanitize/skip), progress and warning
  channels.
- Synthetic fixture builder, xUnit suite, BenchmarkDotNet harness and a
  deterministic mutation fuzzer.
- Trimmable, AOT-compatible, zero runtime dependencies.
