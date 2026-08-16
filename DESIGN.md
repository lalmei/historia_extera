# Historia Extera — Design

Historia Extera is a deterministic world-history generator and a Legends-style viewer.
It builds centuries of settlements, peoples, rulers, faiths, wars, trade, disasters,
and artifacts from a seed and a configuration, then exports a history that can be read
without running the simulation again.

This is the living design: the current boundaries, contracts, and direction of the
project. The [decision log](docs/dev/decision-log.md) keeps the detailed arguments,
measurements, rejected alternatives, and milestone retrospectives.

## At a glance

| Concern | Decision |
|---|---|
| Simulation | Pure C# library with no NuGet dependencies |
| Reproducibility | Identical seed and config produce identical exported history |
| Space | Expensive terrain is accessed through a budgeted, three-tier atlas |
| Time | Years remain the spine; seasons and scheduled days are used only where earned |
| History | Flat events plus persistent entities and sampled series |
| Presentation | The viewer reads exports and never drives the simulation |
| Game integration | Vintage Story is a future terrain host, not an engine dependency |

## Product boundary

The engine generates a completed history. It is not a live grand-strategy simulation
and it does not currently run alongside a Vintage Story server. Its output should make
the shape of a world legible: why a city grew, how a dynasty continued, what moved a
border, which routes carried trade and disease, and why a realm disappeared.

The viewer is a separate consumer of that output. It provides maps, timelines, entity
pages, filters, and narration, but it does not own simulation rules or hidden state.

## Project structure

| Path | Responsibility |
|---|---|
| `src/HistoryEngine/` | Deterministic simulation library, multi-targeted to `net7.0;net10.0` |
| `src/HistoryEngine.Cli/` | Builds a config, runs the engine, and writes an export |
| `src/HistoryEngine.Tests/` | Determinism, model, terrain-discipline, and serialization tests |
| `viewer/` | Astro shell with a React client for browsing exported worlds |
| `docs/` | User and developer documentation |

`HistoryEngine` has no third-party runtime dependency. Keeping the eventual mod-facing
assembly on the BCL avoids dependency conflicts with Vintage Story and other mods.
The `net7.0` target protects the current game-integration boundary; `net10.0` is used
by the CLI and test suite. The game target must be rechecked when Phase 3 begins.

## Core contracts

### Determinism

The contract is:

> Identical seed + simulation-affecting config = identical history, byte for byte.

That contract is enforced by a few non-negotiable rules:

- Randomness comes from forked `Pcg32` streams. A system or episode gets a stable
  substream based on its purpose and subject, never on unrelated draw position.
- Simulation iteration is ordered. Use `DetMap`, `EntityTable`, or an explicitly
  sorted sequence; do not depend on `Dictionary` or `HashSet` enumeration.
- Strings use ordinal comparison and `Hash.OfString`; process-randomized hashes and
  culture-sensitive ordering must not reach a decision path.
- Transcendental functions do not reach decision paths. `DetMath` supplies stable
  approximations where the model needs them.
- The tick loop is sequential. If concurrency ever becomes necessary, work must be
  collected and applied in a fixed order.
- System order and cadence are exported as part of the run identity.
- Every simulation-affecting config field participates in `ConfigHash`.

The fingerprint deliberately describes the history rather than release metadata.
Schema and narration version changes do not move it by themselves; new exported facts
or changed behavior do.

### Terrain

Systems never talk to a terrain backend. They read `TerrainAtlas`, which owns caching,
interpolation, refinement, and the sample budget. `ITerrainSampler` remains a small,
dumb point-query interface with an honest `TerrainCapabilities` declaration.

The atlas buys spatial resolution in three tiers:

1. `SampleCoarse` reads a primed lattice for ordinary regional questions.
2. `Refine` and `RefinedPoints` pay for a bounded grid around a real decision.
3. `SampleExact` memoizes permanent coordinates such as settlements and battlefields.

Hydrology is derived once from elevation on its own finer grid. Rivers therefore have
the same meaning across terrain backends and do not require a river-aware source.
Simulation sampling must scale with decisions, not simulated years.

| Phase | Terrain source | State |
|---|---|---|
| 1 | Procedural value noise | Current default |
| 2 | PGM rasters plus JSON manifest | Supported; external end-to-end trial remains |
| 3 | Vintage Story world generation | Designed for, not implemented |

Raster content, not its file path, contributes to terrain provenance and the config
hash. Only height is required; absent climate fields are modeled and are not falsely
declared as measured capabilities.

### Time

Time follows the same rule as terrain: buy resolution only where a decision needs it.

- The **year** is the unit of growth, harvest, aging, and slow accumulators.
- The **season** is a standing cadence for systems with a real seasonal rhythm.
- The **day** is a stamp or a scheduled due date. The engine never loops over days.

`Stamp` keeps year and day directly addressable. `Calendar` defines the length of a
year and its seasons. `Docket` holds scheduled work in a deterministic total order and
dispatches due entries to their declared owners. Cost therefore scales with active
episodes, not with calendar length.

Most systems remain annual. `expansion` and `war` are seasonal in the current checkout;
`plague` still performs its annual ignition work and also handles scheduled outbreak
steps and arrivals.

### Entities and ownership

Every entity has a typed `EntityId`. Dense entity tables provide stable storage and
iteration. Relationships use ids rather than object references so state can be exported,
replayed, and tested without rebuilding an object graph.

Ownership is explicit and historical facts are not silently rewritten:

- Settlements retain founding, promotion, decline, abandonment, site character, and
  specialization.
- Civilizations own regions at a point in time; territorial history is reconstructed
  from recorded transfers rather than final ownership.
- Figures belong to houses, can hold offices, follow faiths, marry, inherit, and die
  from ordinary or exceptional causes.
- Trade routes and holy sites persist after closure or abandonment so the past remains
  visible.
- Wars, battles, artifacts, tomes, outbreaks, and offices are records with provenance,
  not strings embedded in narration.

### Events and export

`HistoryEvent` is a flat fact record: kind, stamp, subject, optional object and location,
plus a small deterministic data map. Narration templates turn those facts into prose.
The facts remain filterable and replayable even when wording changes.

The chronicle is ordered by `(year, day)` and resolves same-step ties deterministically.
The export contains persistent entities, the event log, selected time series, terrain
presentation data, and the seed/config/system provenance needed to identify the run.

Territory is replayed from events instead of storing one map per year. The same rule
applies wherever a past answer can be derived cheaply from the chronicle: preserve the
transition, not hundreds of redundant snapshots.

## Simulation pipeline

The current system order is part of the run identity:

1. `crown`
2. `population`
3. `plague`
4. `disaster`
5. `settlement-lifecycle`
6. `specialization`
7. `expansion`
8. `religion`
9. `diplomacy`
10. `war`
11. `trade-routes`
12. `figure-incidents`
13. `figure-lifecycle`
14. `succession`
15. `houses`
16. `offices`
17. `artifacts`

The order is causal, not merely organizational. Mortality precedes succession; succession
precedes household and office decisions; diplomacy reads the frontier and faiths already
established that step; trade responds to the peace or war that survives campaigning.
Changing the order is a behavior change and must be reviewed as one.

### Population and settlements

Carrying capacity combines the settlement's site, its contested hinterland, live trade,
harvest, culture, political distance, capital status, and fortification. Good soil alone
does not make a city: large settlements need connection or administration. Decline and
abandonment are real outcomes, and founding records both the ground chosen and the need
that sent a party there.

Site selection uses measurements that vary at the scale of the choice: slope, water
distance, shelter, confluences, estuaries, passes, and the reason for founding. A term
that cannot distinguish candidates or makes siting worse is removed rather than kept
for flavor.

### Politics and people

Cultures provide a people's baseline values. A ruler's disposition bends those values
within bounded latitude, while realm fortunes carry the recent consequences of war,
calamity, triumph, and grievance. Systems read one settled answer for the current step.

Succession follows explicit laws and family relationships. Offices elevate existing
figures from the simulated population instead of inventing disconnected names. Notable
households give those figures family continuity without attempting to simulate every
person in the world.

### Connection and conflict

Diplomacy is based on reach, contact, memory, faith, kinship, and recent events rather
than a static border table. War records declarations, campaigns, battles, sackings,
territorial settlements, and conclusions.

Trade routes are persistent economic topology, not road geometry. Routes can open,
decline, close, and later be replaced. Trade, tome circulation, carrying capacity, and
plague movement consume the same network. Physical roads and paths remain future work.

## Viewer boundary

The viewer reads a world export and derives presentation state client-side. It replays
territory and settlement existence for a selected year, then layers maps, timelines,
filters, summaries, and entity histories over the same facts.

Development may invoke the CLI to generate a world for the viewer, but that is tooling,
not a runtime architecture. A deployed viewer remains a reader of exported histories.

## Roadmap

| Milestone | Deliverable | State |
|---|---|---|
| M0–M9 | Engine foundation through raster terrain and viewer depth | Done |
| M10 | Site selection driven by meaningful ground | Done |
| M11 | Offices, appointments, governors, and founding parties | Done |
| M12 | Ruler dispositions and realm fortunes | Done |
| M13 | Seasons, dated events, and scheduled episodes | In progress |
| M14 | Notable households and office succession | Done |

The immediate active design is M13. It is being staged so mechanical clock changes,
export changes, cadence changes, and episode scheduling can be reviewed and calibrated
separately.

Beyond the numbered milestones:

- Run a real external terrain generator through the Phase 2 raster route end to end.
- Close the loop opened by ruler dispositions with unrest and gradual cultural drift.
- Add road geometry only after trade topology provides stable demand for it.
- Build the Phase 3 Vintage Story terrain adapter and revalidate framework, calendar,
  hydrology cost, and map-raster cost against the game version actually targeted.

These are directions, not promises of order. The decision log records the detailed open
questions and the evidence behind each proposal.

## Explicit non-goals

- No daily or hourly global tick.
- No parallel mutation in the simulation loop.
- No per-day weather model.
- No positional armies moving along paths yet.
- No claim that a logical trade route is already a physical road.
- No direct Vintage Story types or packages in `HistoryEngine`.
- No real-time coupling to a running game server in the current phases.
- No simulation of every household or every person.

## Documentation map

- [Architecture](docs/dev/architecture.md) describes the implemented boundaries.
- [Determinism](docs/dev/determinism.md) explains the reproducibility rules.
- [Testing](docs/dev/testing.md) covers the suite and golden fingerprints.
- [CLI guide](docs/guide/cli.md) documents generation and raster interchange.
- [Viewer guide](docs/guide/viewer.md) documents the Legends interface.
- [Decision log](docs/dev/decision-log.md) preserves full rationale, measurements,
  rejected designs, and milestone retrospectives.

## Keeping this document clean

This file answers **what the current design is**. Put operational commands in the guides,
implementation detail in code and developer docs, and long-form evidence or retrospective
material in the decision log.

When a decision changes, update the relevant statement here and add the reasoning to the
decision log. Do not append a second, contradictory version to this file. For implemented
behavior, the code and tests remain the final authority; a disagreement is documentation
drift to fix, not ambiguity to preserve.
