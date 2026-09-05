# Testing

Historia Extera has separate engine, viewer, documentation, and packaging checks. No one
command proves every surface.

## Engine suite

```bash
make test
make test FILTER=Affinity
make test-quiet
```

The xUnit suite runs against the .NET 10 build. `make test` prints individual progress because
multi-seed, multi-century calibration tests can take long enough that silent output looks
stalled. `test-quiet` runs the same suite with summary output.

The suite covers:

| Area | Contract |
|---|---|
| determinism and config identity | repeated and split runs agree; hashed fields and system order stay complete |
| deterministic-source guards | unordered iteration, unstable hashing, and unreviewed numerical calls stay off decision paths |
| terrain and topology | sampling budgets, raster round trips, sea-level mapping, capabilities, periodic seams, hydrology, roads, and landforms |
| world lifecycle | founding, settlement growth and decline, carrying capacity, specialization, faith, plague, disaster, and collapse |
| politics and war | succession, offices, unrest, diplomacy, campaigns, sieges, territory transfer, and truce behavior |
| travel and trade | route identity, road geometry, journey purpose, duration, mishaps, residence changes, and plague/tome movement |
| people | households, upbringing, ranks, bonds, memories, quarrels, plots, friendships, betrayal, and mortality |
| cosmology and sky records | generated-system checks, coordinate rotations, comet schedules, observations, and claims |
| events and narration | chronological facts, known placeholders, balanced optional segments, typed references, and wording stability |
| serialization | schema shape, indices, canonical JSON, replayable territory, and standard fingerprint |

Calibration assertions describe the tested seed panel and model, not real-world accuracy.
Changing a threshold may require a fresh measurement rather than widening a test until it
passes.

## Viewer checks

```bash
npm test --prefix viewer
npm run astro --prefix viewer -- check
npm run build --prefix viewer
```

The Node test runner covers compatibility, biography, and figure discovery, including selected
map and narration derivations on available exports. It is not comprehensive coverage of
generation, maps, or timeline replay. `astro check` catches type and component errors that
the Node tests do not compile. `astro build` verifies the
static production bundle and confirms that development-only generation code stays outside it.

Retained exports under `viewer/public/worlds/` are compatibility fixtures. Both current
fixtures use schema 50; accepting schemas 21–51 is not evidence that every version has been
tested. Add representative older exports before claiming backward-compatibility coverage.

## Documentation

```bash
uv sync
make docs-build
```

The strict ProperDocs build checks navigation, Markdown parsing, and internal links. It does
not confirm that commands, UI text, defaults, or scientific claims match the implementation;
those require the source checks described in `docs/README.md`.

## Native app and release package

```bash
make macos-app
make macos-release
```

These build the checkout-dependent app and self-contained distribution respectively. A
successful build does not establish first-launch behavior, process shutdown, world generation,
or Gatekeeper behavior. Check those in the actual app when release-facing documentation
changes.

Local release artifacts are ad-hoc signed. Public distribution also needs Developer ID
signing and notarization, which the local build does not prove.

## Regenerating the golden

The standard fingerprint uses seed 42, 300 years, 8 civilizations, size 4096, and raster 64:

```bash
make fingerprint
```

The target writes through a temporary file so a failed run does not damage the committed
golden. Only update it after explaining which system behavior or exported facts changed.

The raster resolution affects export bytes but not the simulation config hash, which is why
the golden command fixes it explicitly.
