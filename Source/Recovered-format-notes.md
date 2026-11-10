# AlpineQuest 2.3.3b: recovered AQM/PGD format boundaries

Analysis date: 2026-10-12. Input: `C:\Users\vasil\Downloads\AyuGram Desktop\base (1) (2).apk`.

## Status and confidence

AQM container, metadata, and tile lookup behavior can be recovered from this APK sufficiently to implement a new, native reader. The relevant classes are obfuscated but several small methods decompile cleanly. This is **not** recovery of the original project or a complete Windows port. No user-created AQM or PGD sample was supplied, and none is embedded in the APK, so successful compatibility with real user map files has not yet been demonstrated.

PGD is a substantially more complex, mutable tree archive. Its initial header and raster channel layout were recovered, but a complete parser and projection serializer have not been reconstructed or tested. It must remain explicitly unsupported in the initial Windows baseline.

Selected decompilation is in `work/decompiler/selected/`. Official JADX 1.5.6 with its bundled local Java runtime was obtained from `https://github.com/skylot/jadx/releases/download/v1.5.6/jadx-gui-1.5.6-with-jre-win.zip` (99,122,202 bytes). No global Java installation or APK modifications were made. No licensing, authentication, signature, or purchase checks were changed.

## Locating the implementation

`work/apk-analysis/format_refs.py` scans DEX methods for string references and records matches in `format-string-refs.json`. It is a candidate locator; the selected Java decompilation confirms the relevant methods. Merely matching the string `data_magic_number` was misleading: `aqp2.aqp` is actually a QCT reader, and is not evidence for the PGD format.

| Concern | Obfuscated class | Evidence |
|---|---|---|
| AQM container directory | `aqp2.te` | FLATPACK1, count and name/offset parsing |
| AQM payload stream | `aqp2.td`, `tc` | offset origin, length-prefix parsing |
| AQM header dispatch | `aqp2.apk` | INF or V2HEADER |
| AQM v1 metadata | `aqp2.apf` | key=value INF parser |
| AQM v2 metadata | `aqp2.apg` | V2HEADER and V2LEVEL |
| AQM v1/v2 tile indexes | `aqp2.apb`, `apc` | X_Y names, level markers |
| AQM index sidecar | `aqp2.aoz`, `ape` | Java big-endian int/long arrays |
| AQM tile decoder | `aqp2.ezh` | Android BitmapFactory.decodeStream |
| PGD file opener | `aqp2.aqn` | archive magic 0x5047443A |
| PGD archive | `aqp2.tp`, `ui` | version, nodes, data blocks |
| PGD channels/levels | `aqp2.aqj`, `aql`, `aqm` | RASTER, projection, bounds |
| PGD tile lookup | `aqp2.aqk` | signed quadrants and decimal digit tree |
| PGD tile decoder | `aqp2.fhs` | BitmapFactory.decodeByteArray |

## AQM FLATPACK1 layout

All integer text described here is ASCII decimal, NUL terminated. The directory reader caps strings at fewer than 30 bytes. The APK skips rather than verifies the nine-byte signature, but a native reader should validate it.

1. At offset 0: nine ASCII bytes `FLATPACK1`.
2. NUL-terminated decimal **directory size / payload base**, called `H` here.
3. The file position immediately after that terminator is **origin O**.
4. NUL-terminated decimal entry count `N`.
5. Repeat `N` times: NUL-terminated entry name, NUL-terminated decimal relative payload offset `R`.
6. Entry payload record begins at the **absolute file offset `O + H + R`**.
7. At that address: NUL-terminated decimal payload byte length `L`, followed by exactly `L` bytes.

The offset expression follows two separate implementations: `te.c()` returns `H+R`, and `td.a()` reopens the file, skips the signature and H text, then skips the returned offset. Confusing H+R with an absolute offset would shift every entry.

Raster payloads are ordinary BitmapFactory-readable image bytes. There is no extra compression or image encryption in the AQM tile decoder shown here. Typical JPEG/PNG contents are plausible, but the actual decoder is format-agnostic. Native code should inspect image headers and bound allocations.

### Version 1

First directory entry is `INF`. Its payload is a line-based key=value header. One zoom level is described. `version` must be 1, `id`, `name`, and `projection` must be present for normal operation.

Tile directory names are `X_Y`, where both are signed decimal integers. Metadata gives inclusive `xtmin..xtmax`, `ytmin..ytmax`. Out-of-range tile entries are ignored by the APK. The optional `.IDX` file is an acceleration index, not required to read the map.

### Version 2

First entry is `V2HEADER` with global `id`, `name`, `desc`, `creator`, `software`, `version`, and `date`. This parser accepts any nonzero metadata version; that is not proof of compatibility with future versions.

`V2LEVEL` entries describe each zoom level, before tile names. The map index builder looks for directory names starting with `@` to delimit tile groups. A group is consumed until another `@` marker; the next group is found by searching again. This suggests paired start/end markers; **do not collapse every `@` into a new level without a real sample or a reference writer confirming the marker order**.

### Metadata and georeferencing

Level keys: `id`, `name`, `copyright`, `scale`, `datasource`, `background`, `projection`, `geoid`, `xtoffset`, `ytoffset`, `xtratio`, `ytratio`, `xtsize`, `ytsize`, `xtmin`, `xtmax`, `ytmin`, `ytmax`.

`xtsize` and `ytsize` describe tile pixel dimensions. Projection resolution and offsets operate in tile coordinates. `acr.d(projection,geoid)` creates a projection, scales its native projected units by `57.29577951308232 / spheroidRadius`, and then `apf`/`apg` apply a further affine transform with multipliers `(xtratio, -ytratio)` and offsets `(xtoffset, -ytoffset)`. `ada` confirms that these multipliers and offsets are applied **after** the underlying projection for forward conversion and reversed before inverse conversion. `ezh` negates its rendering Y argument to look up the AQM tile.

Thus for a spherical MERCATOR AQM, normalized longitude is in degrees and projected vertical coordinate is degrees of the Mercator logarithm. The stored X tile coordinate derives from `longitudeDegrees * xtratio + xtoffset`. The displayed Y tile coordinate derives from `-mercatorDegrees * ytratio - ytoffset`; the stored tile name uses the opposite Y sign. This relationship must be validated against a real map before a reader is advertised as geographically correct.

The projection factory also handles WGS84 ellipsoidal Mercator and many other projections via a separate CRS subsystem. A first reader must reject unsupported projections or geoid values clearly; silently treating every file as Web Mercator would misplace tracks and waypoints.

### Optional AQM sidecar .IDX

Written using Java DataOutputStream (big endian): signed 32-bit level count; per level: width, height, minX, minY, outer-array length; per outer-array row: signed 32-bit row length, then signed 64-bit payload logical offsets H+R. Zero row length represents an absent row. This format does not bind the index to a file checksum, so rebuilding an index from the directory is safer than trusting a potentially stale sidecar.

## PGD partial design

`aqn` opens `tp` with magic decimal 1346847802 = **0x5047443A**, ASCII `PGD:` if stored big endian. `ui` reads a 32-bit magic, 32-bit version (versions above 1 are rejected), then a 64-bit root-node pointer. Its writer reserves five 64-bit fields after the initial two ints; the root pointer is at offset 8. Endianness is strongly suggested by Java-style stream infrastructure but should be confirmed in the `sw`/`tb` implementation before writing a full reader.

Node/data tags used by `ui`:

| Tag | Value |
|---|---:|
| Node | 0x00015555 |
| Node entry list | 0x00025555 |
| Node entry table | 0x00045555 |
| Primary data block | 0x00105555 |
| Additional data block | 0x00205555 |

A node begins with tag, flags, metadata pointer, entries pointer. The shown implementation expects an entry structure directly after the 24-byte node header, despite reading the entries pointer. List/table structures contain 12-byte pointer/id entries, capacities, and mutable bookkeeping. Data can span blocks; treating a node pointer as a contiguous JPEG offset is incorrect.

Root children are channels. `PGD_CT=RASTER` identifies raster channels. Channel children are zoom levels. Level metadata `PPTX`/`PPTY` default to 256 pixels. `PGD_CO` supplies geographic outline, `PGD_CB` optional projected bounds. Level data entry id 1 serializes a projection, with its own nested typed binary format (`acr.a`).

Tile storage chooses quadrant child 0, 1, 2, or 3 by the signs of X/Y, takes absolute values, and traverses least-significant-first decimal digits of X, then Y. X terminal digits are stored under digit+10; nonterminal X digits under digit. Y terminal digits are data entries; nonterminal digits are nodes. The reader strips a two-byte header plus an optional prefix before sending the remaining bytes to BitmapFactory. The decompiled prefix expression is `(firstByte << 1) + secondByte`; it must not be assumed to mean an unsigned 16-bit length without examining DEX instructions/reference files.

## Porting implications

- AQM reading can be reimplemented without Android. A scoped native reader can stream the file directory and lazily decode requested tiles. Real AQM v1/v2 fixtures are needed to verify offset rules, group markers, coordinates, missing tiles, and different producers.
- PGD reading needs further work on list/table traversal, block chains, metadata types, and projections. The partial findings are insufficient for a safe general reader.
- Decompiled methods still contain inference errors and fallback blocks, e.g. the AQM index error path and optional PGD outline parser. They cannot simply be compiled as original Java source.
- The raster drawing paths are explicitly Android Canvas/Bitmap implementations. Source recovery does not turn them into Win32 code; the Windows renderer and input handling must be reimplemented.
- A native baseline with image map tiles and GPX/KML is useful engineering progress, but is not full AlpineQuest feature parity. Do not label it a complete APK rebuild.

Primary reference context: AlpineQuest documents AQM as a format created by Mobile Atlas Creator/MAPC2MAPC and PGD as its imported raster format: https://www.alpinequest.net/en/help/v2/maps/file-based-select . The MOBAC project has an existing AQM reader and writer, but source licensing must be preserved if code is reused: https://sourceforge.net/p/mobac/patches/44/ and https://sourceforge.net/p/mobac/git/ .
