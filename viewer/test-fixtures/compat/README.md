# Historical compatibility exports

These are complete, unedited JSON exports from the historical engine revisions in
`manifest.json`, compressed with gzip (`mtime=0`). SHA-256 hashes cover the original
uncompressed bytes. Tests decompress them in memory; no engine, network, or user world
library is needed. Do not regenerate them with the current engine or change their
schema numbers to imitate old output.

All five runs use seed 11, 150 years, eight civilizations, default world size and
periodicity, and a 16-pixel map raster. The smaller raster limits fixture size without
removing history records. Each export retains its original metadata, narration and
indices. Together the compressed fixtures occupy about 2.8 MiB.

| Schema | Boundary exercised |
| --- | --- |
| 28 | Oldest retained supported writer; journeys and campaigns, no bonds or residence history |
| 34 | Bonds, memories and undertakings; no residence history or friendships |
| 42 | Residence history and comet claims; no friendships or generic claim subjects |
| 48 | Populated friendship records and service history; original comet-only claim shape |
| 52 | Generic claim subjects and quantities; no claim-transmission root collection |

The suite pins this list separately from the manifest. Missing files, changed bytes,
a changed floor, empty histories, or absent boundary features fail. It exercises the
real asynchronous loader using a data URL, normalization, all event narration, sampled
political/map replay, final region ownership against the export, and biography
derivations for up to 300 figures per file. This is representative schema coverage,
not exhaustive version coverage or rendered-component testing.

The previous minimum of 21 had no retained export. The inspected published source
history moves from schema 18 to 28, and the available local exports supplied no 21–27
sample. The supported minimum is therefore 28. Supporting an earlier version again
requires retaining an authentic export and exercising it, not relabelling a newer one.

## Provenance and reproduction

Generated on macOS arm64 with .NET SDK 10.0.301 from GitHub source archives addressed
by the full commit hashes in the manifest. No source changes were made. Download and
extract the chosen archive into a separate directory, then resolve its physical path
before building (on macOS `/tmp` is a symlink to `/private/tmp`). From that source root:

```sh
dotnet run --project src/HistoryEngine.Cli -c Release -- \
  --seed 11 --years 150 --civs 8 --raster 16 --out export.json
```

The manifest records those arguments and the expected export hash. Reproduction is an
explicit maintenance check, never part of normal tests. Preserve existing fixtures
when adding a new boundary; update the mandatory version list and record why the new
sample matters. See `docs/dev/testing.md` for optional local-world smoke coverage.
