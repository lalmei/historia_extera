# Architecture

## Boundaries

| Piece | Role |
|---|---|
| `HistoryEngine` | Simulation only. No NuGet packages. No viewer or game references. |
| `HistoryEngine.Cli` | Builds a `WorldConfig`, runs `HistoryRun.Execute`, writes JSON. |
| Viewer | Reads the export; never drives the simulation. |
| Vintage Story (Phase 3) | Will supply terrain via `ITerrainSampler`; engine stays free of VS types. |

CLI and tests share `HistoryRun` so they cannot drift into different assembly
sequences.

## Yearly systems

Order is part of the run's identity (hashed into the export). As built:

1. `population`
2. `plague`
3. `disaster`
4. `settlement-lifecycle`
5. `specialization`
6. `expansion`
7. `religion`
8. `diplomacy`
9. `war`
10. `trade-routes`
11. `figure-lifecycle`
12. `succession`
13. `houses`
14. `artifacts`

Causal chain: harvest → pestilence and the land taking their share → settlement change →
character → pressure → borders → faiths across them → opinion of the neighbours by both
→ the wars that follow → commerce responding to the resulting peace → deaths → thrones filled
→ marriage / heirs against the line as it now stands → what the survivors made.

Diplomacy follows expansion so opinions are formed about the frontier that exists rather
than last year's, and religion precedes diplomacy for the same reason. War precedes
`figure-lifecycle` for the same reason deaths precede `succession`: a ruler killed at a
siege must be dead before the throne is filled, or the realm spends a year vacant for no
reason the chronicle can explain.

Plague and disaster follow `population` rather than preceding it, so a year's growth is
applied before its mortality — the other order lets a town regrow inside the tick that
emptied it. They precede the lifecycle so a settlement gutted this year is judged this
year, which is what lets a plague finish a place.

## Trade routes and future roads

`TradeRoute` is a persistent, undirected connection between two settlements. It records its
founding and closure, preferred transport (`Overland`, `River`, or `Coastal`), current traffic,
peak traffic, and economic status. Closed routes remain entities, so reopening the same pair
later creates new history rather than rewriting the old route.

The route is **topology, not geometry**. River and coastal modes say both endpoints have that
access; an overland route records demand between its endpoints. A later road network can attach a
physical path to the route, prioritize construction by peak traffic, and preserve the route's
identity across rerouting or road upgrades.

Tome circulation and plague spread consume active routes. This keeps the engine's different
notions of ordinary travel on one shared network instead of letting each system invent a fresh
distance heuristic.

## Terrain: `ITerrainSampler` and `TerrainAtlas`

The simulation never talks to a backend directly.

- **`ITerrainSampler`** — dumb point queries + capabilities. No caching.
- **`TerrainAtlas`** — coarse lattice, refined rectangles, memoised exact samples.

Phase 1 uses noise (`ProceduralTerrainSampler`). Phase 2/3 swap the sampler without
touching systems. Every run wraps the sampler in `CountingTerrainSampler` so tests can
budget sample counts.

### Backends

| Backend | Source | Declares |
|---|---|---|
| `ProceduralTerrainSampler` | Value noise, from the seed | `Standard` — every field |
| `RasterTerrainSampler` | PGM planes + a JSON manifest | Only the layers actually supplied |

`RasterTerrainSampler` requires a height layer and nothing else; absent fields are
modelled from elevation and latitude and **deliberately excluded from
`TerrainCapabilities`**, so a world built on a bare heightmap reports which of its
measurements were measured. `TerrainRasterBake` writes the format from any sampler,
which is how the round trip is tested (`RasterTerrainTests`).

A raster set's content digest goes into `WorldConfig.TerrainSource` and from there into
the config hash — a file path is not the pixels, and the determinism contract has to keep
covering the terrain. It contributes only when set, so procedural worlds hash unchanged.

## Naming

Markov models over language corpora under `Naming/Corpora/`. Licenses for corpus text
are recorded in that folder's `LICENSES.md`.

## Serialization

`WorldExporter` produces the viewer JSON and the fingerprint digest. Canonical form is
compact (not `--pretty`). Fingerprints pin simulation behaviour in golden tests.
