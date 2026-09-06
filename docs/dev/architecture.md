# Architecture

## Execution path

```text
CLI or library caller
        │
        ▼
   WorldConfig ── optional ITerrainSampler
        │
        ▼
 HistoryRun.Execute
        │
        ├── WorldBuilder.Create: terrain atlas, regions, cosmology, founding peoples
        ├── Simulator.Run: ordered systems, seasons, scheduled episodes
        └── WorldExporter.Build: map raster, entities, events, series, indices
                                      │
                                      ▼
                               one JSON export
                                      │
                                      ▼
                          viewer load, replay, and rendering
```

`HistoryRun.Execute` is the normal library entry point. The CLI and tests use it so their
assembly sequence cannot drift. It validates `WorldConfig`, creates or wraps the terrain
sampler in `CountingTerrainSampler`, builds year-zero state, runs the simulator, and returns
the mutable world plus run metadata. `ToExport` performs presentation raster sampling and
builds the immutable transfer document.

## Engine and host boundaries

| Component | Owns | Does not own |
|---|---|---|
| `HistoryEngine` | world creation, simulation decisions, factual events, cosmology, export construction, raster loading helpers | host file lifecycle, command-line parsing, browser UI, Vintage Story types |
| `HistoryEngine.Cli` | arguments, raster-manifest loading, progress, summaries, JSON output | simulation policy |
| Astro development middleware | local world library, CLI process launch, cancellation, previews, file management | simulation or export interpretation |
| React viewer | compatibility checks, event replay, filtering, narration rendering, maps and pages | generation or hidden mutable state |
| SwiftUI app | writable paths, packaged runtime cache, local server lifecycle, `WKWebView` | simulation and viewer logic |

A production Astro build is static and read-only. The generator endpoints exist only under
`astro dev`; the packaged app intentionally runs that local server as a private application
process. There is no remote application server or network protocol between the engine and
viewer.

Vintage Story is a planned terrain host. No current assembly references its API, registers a
mod, adds commands, or installs handbook entries.

## World creation and terrain

`WorldBuilder.Create` creates `WorldState` in this order:

1. validate configuration;
2. construct the selected sampler and `TerrainAtlas`;
3. derive the world's name, cosmology, random root, and naming service;
4. build the region grid (128-unit default); the atlas has already derived hydrology;
5. record the beginning of civic history;
6. choose habitable, separated homelands and found the requested civilizations where
   possible.

Simulation systems never hold an `ITerrainSampler`. They ask `TerrainAtlas`, which owns a
primed coarse lattice, bounded refinement, exact-sample memoization, landforms, and hydrology.
`TerrainDisciplineTests` enforce that boundary and sample budgets.

`ProceduralTerrainSampler` supplies height, temperature, rainfall, geologic activity,
vegetation, and lakes from deterministic noise. `RasterTerrainSampler` requires height and
can read optional climate, ecology, geology, and lake planes. Fields missing from a raster are
modeled where possible but not reported as measured capabilities.

Hydrology is derived once on the 64-unit grid. A priority-flood pass creates a drainage-only
spill surface so land depressions reach an outlet. The actual height samples are unchanged.
D8 flow accumulation selects the highest-flow land cells for vector river segments. Raster
inputs cannot currently provide measured flow or declare their own topology.

## Coordinates and topology

The world is a square plane with abstract `(x, z)` units and origin `(0, 0)`. Region,
terrain, hydrology, and road grids have separate configured strides.

When `EastWestPeriodic` is false, all four edges are bounded. When true, `x` wraps and `z`
remains bounded. Terrain noise, sampling, region adjacency, hydrology, road search, simulation
distance, export metadata, and viewer lines all honor the seam. The size must align with every
grid used at the seam.

Generated climate treats the center row as the equator and the north and south edges as
poles. This is a model convention, not an exported latitude/longitude coordinate system.
There is no local time or observer position in map coordinates.

## Time

The default civic calendar contains 360 days and four 90-day seasons. The simulator does not
run a daily loop.

- Annual systems run on day 0, the opening season.
- Seasonal systems run at the start of all four seasons.
- The docket stores individual due days for work that needs finer timing.
- Due entries are resolved before the systems on the next reached season boundary.
- Series are sampled once after the final season of each year.

The docket order is `(absolute day, kind, subject index, sequence)`. `PlagueSystem` handles
outbreak steps and arrivals; `WarSystem` handles siege resolutions. Docket state is mutable
run state and is not exported. Its consequences survive as events and entity changes.

## System order

Names, order, and cadence are part of the run identity. The current default order is:

| # | Name | Cadence | Main responsibility |
|---:|---|---|---|
| 1 | `crown` | annual | fade realm memories and settle effective governing values |
| 2 | `population` | annual | harvest, carrying capacity, population change, and famine |
| 3 | `plague` | annual plus docket | ignite and progress outbreaks and travel |
| 4 | `disaster` | annual | terrain-sensitive calamities and mortality |
| 5 | `settlement-lifecycle` | annual | promotion, decline, and abandonment |
| 6 | `specialization` | annual | settlement economic character |
| 7 | `expansion` | seasonal | claims and founding movement |
| 8 | `religion` | annual | faith creation, adoption, schism, and holy sites |
| 9 | `diplomacy` | annual | relations, alliances, and truces |
| 10 | `war` | seasonal plus docket | campaigns, battles, peace, and siege resolution |
| 11 | `unrest` | annual | revolt, secession, usurpation, and brigandage |
| 12 | `trade-routes` | annual | route opening, traffic, closure, and road construction |
| 13 | `cultural-drift` | annual | move realm baselines through contact and pressure |
| 14 | `travel` | annual | journeys, returns, mishaps, and residence changes |
| 15 | `hardship` | annual | memories of famine, plague, sacks, and disasters |
| 16 | `figure-incidents` | annual | personal wounds, quarrels, plots, and undertakings |
| 17 | `figure-lifecycle` | annual | aging and biological mortality |
| 18 | `succession` | annual | fill vacant thrones |
| 19 | `houses` | annual | marriage, birth, and household continuity |
| 20 | `offices` | annual | appointments and court careers |
| 21 | `ranks` | annual | military service and promotion |
| 22 | `artifacts` | seasonal | artifacts, circulation, sky records, and unresolved estates |

Later systems read state left by earlier systems in the same step. Reordering a pair can
change causality even if both implementations stay untouched. `SystemOrderHash` therefore
folds in each name and every non-annual cadence.

## State, events, and derived history

`WorldState` is the mutable owner of entity tables, chronicle, yearly series, active
outbreaks, docket, terrain atlas, and deterministic random root. Entities use typed,
dense `EntityId` values rather than object-reference identity.

The chronicle records flat facts. References remain ids, optional `data` remains structured,
and prose is produced later from exported templates. Systems must record every change that a
historical consumer needs. Final entity fields alone cannot answer who owned a region in year
120 or which ruler held a seat then.

The viewer builds spans from the event list for ownership, settlement tiers, capitals,
reigns, and faith adherence. This keeps export size proportional to changes instead of
number of years times number of entities. Tests compare replayed territory with final state.

## Cosmology and sky records

`WorldCosmology.From(seed)` runs during world creation. It produces the host galaxy, star,
habitable body, companions, moons, comets, orientation, and deep-time chronology. The model
is deterministic and independent of run length or civilization count.

Most cosmology is descriptive and has no effect on terrain or political decisions. Comet
return schedules are the exception: `ArtifactSystem` calls `Skywatch.Record` and
`Skywatch.Answer` in the opening season, allowing simulated figures to observe returns and
make claims. The viewer's rendered night sky uses the galaxy model directly and is separate
from those historical observations.

See [Cosmology and scientific scope](cosmology.md) before changing physical formulas,
coordinate language, or sky rendering.

## Catalogs and names

Historia Extera has no runtime star, planet, comet, constellation, or content catalog
loader. System bodies are generated from the seed. The word “catalog” in the viewer means a
saved-world listing, not an astronomical catalog.

Naming uses Markov models over text corpora embedded in `HistoryEngine.dll`. Each name is a
function of entity identity and a culture language seed, so request order cannot rename later
entities. A custom `INameGenerator` can be supplied to `WorldBuilder.Create`, but the standard
`HistoryRun.Execute` path does not expose that parameter.

## Caches and derived state

The engine's main cache is `TerrainAtlas`; it belongs to one `WorldState` and is populated
during creation and bounded refinement. No public cache-invalidation API exists because run
inputs are treated as immutable. Replacing terrain after a world is built is unsupported.

The viewer fetches an export once, normalizes it, builds id maps and replay spans, then renders
from that in-memory `World`. It does not refresh a file automatically after generation; the
development flow reloads the page when it overwrites the export already open.

The packaged app copies its bundled viewer runtime and dependencies to a versioned writable
cache. Generated worlds remain in Application Support rather than in that cache or the signed
bundle.

## Where to modify a subsystem

| Change | Start here | Contract to check |
|---|---|---|
| generation inputs | `WorldConfig.cs`, CLI parser, `viewer/src/app/generate.ts` | config hash, all three default surfaces |
| terrain backend | `ITerrainSampler.cs`, `TerrainAtlas.cs` | capabilities, topology, sampling budget, provenance |
| river or road geometry | `Hydrology.cs`, `Roads.cs` | deterministic tie-breaking and periodic seam |
| simulation behavior | relevant file in `Systems/` and `Simulator.cs` | order, cadence, RNG fork, chronicle facts, golden |
| person-level history | figure entities, bonds/memories, relevant systems | year cutoff and source-event links |
| cosmology | `Cosmology.cs`, `Galaxy.cs`, `CelestialOrientation.cs` | approximation labels and deterministic math |
| comet records | `Skywatch.cs`, `SkyClaims.cs`, `ArtifactSystem.cs` | simulated observer eligibility and return schedule |
| claim transmission | `ClaimTransmission.cs`, `Tomes.cs` | derived from surviving carriers; draws no randomness |
| export field | `WorldExport.cs`, `WorldExporter.cs`, `viewer/src/app/types.ts` | schema bump, compatibility, fingerprint |
| historical rendering | `viewer/src/app/timeline.ts`, relevant view | event replay and final-state distinction |
| native lifecycle | `ViewerServer.swift`, build script | writable paths, cache identity, packaged tools |
