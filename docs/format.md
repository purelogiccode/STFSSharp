# STFS and GOD/SVOD format notes

This document records the on-disk facts StfsSharp implements. It is not a
complete STFS specification; it covers the read-only subset the library
supports.

All multi-byte integers are big-endian unless noted. Sectors and logical STFS
blocks are 2048 bytes (`0x800`); a physical STFS block (hash table or data) is
4096 bytes (`0x1000`).

## 1. XContent header

Both single-file STFS packages and GOD/SVOD headers start with the plaintext
XContent header.

| Offset | Size | Field |
|---:|---:|---|
| `0x000` | 4 | Signature: `"CON "`, `"LIVE"` or `"PIRS"` |
| `0x32C` | 20 | SHA-1 of the header body (`0x344` … first block) |
| `0x340` | 4 | Header size |
| `0x344` | 4 | Content type |
| `0x360` | 4 | Title ID |
| `0x366` | 1 | Disc number |
| `0x367` | 1 | Disc count |
| `0x379` | `0x24` | STFS/SVOD volume descriptor |
| `0x39D` | 4 | Data file count |
| `0x3A9` | 4 | Volume type (`0` = STFS, `1` = SVOD) |
| `0x411` | `0x80` | UTF-16BE title name |

Content types recognized by the readers:

| Value | Meaning |
|---:|---|
| `0x00000002` | XBLIG |
| `0x000D0000` | XBLA |
| `0x02000000` | Community Games (treated as XBLIG) |
| `0x00007000` | GOD |
| `0x00004000` | NXE |

Title metadata is decoded from the header only. Non-ASCII and control
characters are removed; path-unsafe characters are replaced with `_`, and a
missing name falls back to the hex title ID.

## 2. STFS volume descriptor

The volume descriptor begins at `0x379`:

| Offset | Size | Field |
|---:|---:|---|
| `0x379` | 1 | Descriptor size (`0x24`) |
| `0x37A` | 1 | Version |
| `0x37B` | 1 | Flags: bit 0 = read-only format, bit 1 = root active index |
| `0x37C` | 2 | File table block count (little-endian) |
| `0x37E` | 3 | File table first block (little-endian) |
| `0x381` | 20 | Top hash (SHA-1 of the top-level hash table) |
| `0x395` | 4 | Total allocated logical blocks |
| `0x399` | 4 | Free logical blocks |
| `0x39D` | 4 | Data file count (must be `0` or `1` for a single-file package) |

The first physical block starts at `round_up(header_size, 0x1000)`.

### Hash-tree interleave

Logical block `N` maps to a physical block with the standard level-0
(`0xAA` entries) and level-1 (`0x70E4 = 0xAA * 0xAA` entries) interleave:

```text
mapped = N
base   = 0xAA
for level in 0..2:
    mapped += ((N + base) / base) * tablesPerLevel
    if N < base: break
    base *= 0xAA

offset = firstBlockOffset + mapped * 0x1000
```

`tablesPerLevel` is `1` for read-only packages and `2` for writable-format
packages (primary/secondary hash-table copies). For writable packages the
active copy is selected by the volume-descriptor root bit and the parent hash
entry's bit `0x40000000`.

Each hash entry is `0x18` bytes:

| Offset | Size | Field |
|---:|---:|---|
| `0x00` | 20 | SHA-1 of the child block (data block or lower hash table) |
| `0x14` | 4 | Next logical block, low 24 bits (`0xFFFFFF` = end of chain) |

Every logical data block is hashed as a full 4096-byte physical block,
including the padding after the final 2048-byte sector.

## 3. STFS file table

The file table is a chain of logical blocks starting at the descriptor's file
table block number. Each block holds `0x40` entries of 64 bytes:

| Offset | Size | Field |
|---:|---:|---|
| `0x00` | `0x28` | Name (Latin1, no terminator) |
| `0x28` | 1 | Name length in bits 0–5; `0x40` contiguous; `0x80` directory |
| `0x29` | 3 | Valid data blocks (little-endian) |
| `0x2C` | 3 | Allocated data blocks (little-endian) |
| `0x2F` | 3 | First logical block (little-endian) |
| `0x32` | 2 | Parent entry index (`0xFFFF` = root) |
| `0x34` | 4 | File size |
| `0x38` | 2 | Creation date (FAT) |
| `0x3A` | 2 | Creation time (FAT) |
| `0x3C` | 2 | Modified date (FAT) |
| `0x3E` | 2 | Modified time (FAT) |

A zero name length terminates the table. Parent entries must precede children;
the reader rejects non-directory parents, invalid parent indices, duplicate
paths and directory depth above 64.

## 4. GOD/SVOD containers

A GOD container is a header file plus a sibling `<header>.data` directory:

```text
<content hash>            XContent header (volume type 1)
<content hash>.data/
    Data0000
    Data0001
    ...
```

The SVOD volume descriptor fields used by the reader:

| Offset | Size | Field |
|---:|---:|---|
| `0x391` | 1 | Device features; bit `0x40` = enhanced GDF |
| `0x392` | 3 | Data block count (little-endian) |
| `0x395` | 3 | Starting data block (little-endian) |

The logical image length is:

```text
length = (dataBlockCount + startingBlock) * 2 * 2048
```

### Sector mapping

For a logical sector `S` (2048-byte sectors), the reader computes:

```text
if S == 0x20 or S == 0x21:            # security sector pair
    offset = (enhanced ? 0x2000 : 0x12000) + (S - 0x20) * 0x800
    fragment = 0
else:
    if S < startingBlock * 2:         # zero-filled prefix
        return zeros
    trueBlock = S - startingBlock * 2
    if enhanced: trueBlock += 2

    fragment  = trueBlock / 0x14388
    fileBlock = trueBlock % 0x14388
    level0    = (fileBlock / 0x198) + 1
    level1    = (level0 / 0xA1C4) + 1
    offset    = fileBlock * 0x800 + (level0 + level1) * 0x1000

    if offset >= 0xA290000:           # fragment roll-over
        fragment++
        offset = (offset % 0xA290000) + 0x2000
```

The reader never exposes hash tables as image data.

## 5. Validation

`StfsPackage.Validate()` performs these checks:

1. every logical data block against its level-0 SHA-1 entry;
2. every file block chain for truncation, cycles and out-of-range blocks;
3. every level-0 table against its level-1 parent (and level 1 against
   level 2 when the package is large enough);
4. the top hash at `0x381` against the top-level hash table;
5. the header hash at `0x32C` against the header body from `0x344` to the
   first physical block.

Signatures are not verified. A package can pass validation and still be an
unsigned or re-signed package.

## 6. Hardening bounds

| Guard | Limit |
|---|---|
| GOD fragment count | 4096 |
| STFS logical blocks | `0xFFFFFF` |
| STFS file-table entries | 65536 |
| STFS directory depth | 64 |
| Validation block walk | capped at the physical block count |
| Hash-table cache | 16 blocks |
| Unsafe entry names | rejected / sanitized / skipped by policy |
