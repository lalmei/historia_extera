# Historia Extera — Design Notes

A Dwarf Fortress-style world history generator for Vintage Story, plus a Legends-mode
viewer. This file is the running decision log: what was chosen, and why, so that a
decision can be revisited on its merits rather than rediscovered.

**Status:** Milestones 0–4 complete. Real naming languages, and a settlement
lifecycle that runs its full course rather than only ever growing.

---

## Phasing

| Phase | Terrain backing | State |
|---|---|---|
| 1 | Noise-based placeholder | **current** |
| 2 | Open-source 2D terrain generator | designed for, not built |
| 3 | Vintage Story worldgen | designed for, not built |

The architectural consequence that drives everything: the simulation runs against an
abstract terrain interface, and the backend is swapped without touching simulation
code. Phase 2 is the proving ground — if the abstraction is wrong, that surfaces on
terrain we control rather than inside the game.

---

## Decisions

### Solution layout

```
src/HistoryEngine/         class library, net7.0;net10.0 — pure simulation, zero deps
src/HistoryEngine.Cli/     console runner (`legends`)
src/HistoryEngine.Tests/   xunit — determinism, discipline, serialization
viewer/                    Astro + React + TS + Tailwind
```

`HistoryEngine` has **no NuGet dependencies at all**. Everything it needs is in the
BCL on both targets. That keeps the assembly that eventually loads into Vintage Story
free of version conflicts with the game or other mods.

### Target frameworks: multi-target `net7.0;net10.0`

net7.0 is the framework Vintage Story mods load. It is also out of support upstream,
so `CheckEolTargetFramework` is disabled deliberately rather than accidentally.

Multi-targeting rather than net7-only means the net7 build breaks *immediately* if a
net10-only API creeps into the engine, instead of years from now when the mod is
assembled. Tests run on net10 against the net10 build, so they execute on the same
runtime they compile against.

> **Phase 3 note:** re-confirm the TFM against the Vintage Story version actually
> being targeted. VS may well have moved off net7 by then; treat this as a
> decision to revisit, not a fact.

### Determinism

The contract: **identical seed + config produces an identical history**, byte for
byte, across processes and machines.

- **Forked RNG substreams.** `Pcg32.Fork(purpose, discriminator)` derives a child
  stream from the parent's immutable *seed*, never its position. A single global RNG
  would be deterministic but brittle in the way this project will actually be
  stressed: adding one die roll to the war system would shift every name, birth and
  battle downstream of it, so every golden test would fail on every unrelated change
  — which trains people to regenerate goldens without reading them. Convention is one
  fork per system per year.
- **No `Dictionary`/`HashSet` iteration.** Enumeration order depends on insertion
  history and capacity. It is usually stable within a process, which is what makes it
  dangerous. `DetMap` (sorted, binary-searched) and `EntityTable` (dense, id-indexed)
  are the ordered alternatives.
- **Ordinal comparers, always.** `Comparer<string>.Default` is culture-sensitive, so a
  sorted structure keyed by string would order differently under a different locale
  and change the export's bytes. `DetMap` special-cases string keys to
  `StringComparer.Ordinal` for exactly this reason.
- **No transcendentals on decision paths.** IEEE 754 guarantees correct rounding for
  `+ - * /` and `sqrt` only. `Sin`/`Cos`/`Pow`/`Exp`/`Log` may differ by an ULP across
  runtimes, and one ULP next to a `Chance(score)` comparison forks the entire history.
  `DetMath` provides polynomial equivalents; the mortality curve is quadratic rather
  than Gompertz for this reason alone.
- **No `string.GetHashCode()`.** Randomised per process. `Hash.OfString` (FNV-1a) is
  the only string hash the engine uses.
- **Strictly sequential tick loop.** No parallelism. At this scale (a few centuries in
  well under a second) the cost of making parallel work deterministic exceeds the
  benefit. If a future milestone needs it: collect-then-apply in a fixed order, never
  parallel mutation.
- **System order is part of the run's identity.** Swapping two systems changes the
  history as much as changing the seed, so `SystemOrder` is hashed and exported.

Enforced by `DeterminismGuardTests`, which scans engine source for these constructs.
Escape hatch: a trailing `// det:ok` comment. The point is to make using one a
deliberate, annotated decision.

### Terrain: `ITerrainSampler` and `TerrainAtlas`

The single most important boundary in the codebase.

**`ITerrainSampler` stays dumb.** Answer the point asked about, declare what you can
measure via `TerrainCapabilities`, offer `SampleBatch` if you can amortise setup. No
caching, no interpolation, no cleverness.

**`TerrainAtlas` owns everything else**, and the simulation talks only to it — three
tiers:

1. `SampleCoarse` — bilinear interpolation of a primed sparse lattice. **Costs
   nothing.** The overwhelming majority of queries: is this region habitable, which
   way is warmer.
2. `Refine` / `RefinedPoints` — batch-samples a small rectangle at a finer stride,
   for comparing candidate sites. Bounded, per-decision.
3. `SampleExact` — one true sample, memoised forever. Only for coordinates that become
   permanent: a settlement's position, a battlefield.

`RefinedPoints` exists as one call because refining and then scoring at coordinates
off the refined grid would miss the cache on every point — the exact mistake the
tiering prevents, made while apparently using it correctly.

**Why this is Milestone 1 work and not Phase 3 work.** Phase 1's sampler is free to
call, so nothing about the code's behaviour reveals a query pattern that would be
catastrophic at 1–2ms per sample. `CountingTerrainSampler` wraps every run — including
every test run — and `TerrainDisciplineTests` asserts:

- a full 300-year run stays under a **12,000 sample budget** (currently ~5,700, ≈8.5s
  of in-game worldgen);
- sampling scales with **decisions, not years** — a 10× longer run may found more
  settlements, but must not cost per-year;
- nothing under `Systems/` so much as mentions `ITerrainSampler`. World construction
  legitimately needs the raw sampler to build the atlas, which is why `WorldBuilder`
  lives under `World/`.

### Rivers are derived, on their own grid

A point sampler cannot answer "is there a river here" — the answer depends on the
whole upstream catchment. Vintage Story's sampler does not report rivers at all unless
the Watersheds sampler is installed. So hydrology is **derived from elevation** by D8
flow accumulation, which means rivers exist identically in all three phases and are
guaranteed consistent with the terrain they cut through.

**Corrected during M1:** hydrology was first built on the simulation lattice, which
made it free. It also made it useless — at the lattice's 256-unit stride a 4096-unit
world is 17×17 cells, and flow accumulation over that produced **four disconnected
fragments**. Drainage is a finer-grained phenomenon than the regional scoring the
lattice exists for, so it gets its own grid at `HydrologyStride` (default 64 → 65×65,
~4,200 samples). One bulk batch at world creation, memoised. Phase 3 can lower it
deliberately.

Rivers are exported as **line segments following the flow graph**, not as a raster
plane: a per-cell flag rasterises to a block the size of the grid stride, which renders
as a scatter of squares that read as lakes.

### Settlement lifecycle: what made decline possible

The M4 deliverable was specialization and abandonment. Specialization was easy; making
abandonment *reachable* took five wrong answers, and each one is worth recording because
each looked correct in isolation and each passed its own tests.

**The chain of bugs, in the order they were found:**

1. **Fixed carrying capacity.** Capacity was a function of regional fertility alone, so
   headroom was always positive and every settlement grew monotonically to its ceiling.
   Fixed by making capacity move with the harvest.
2. **Fertility saturated at 1.0.** The measure was a product of three clamped ramps that
   each read 1.0 across their whole comfortable range, so every temperate lowland scored
   exactly 1.0. Settled regions clustered at fertility 1.000 and capacity was effectively
   uniform. Fixed with `DetMath.Bump`, a unimodal curve with one optimum rather than a
   plateau of them.
3. **The world was uniformly hospitable.** Rainfall added a large constant to every warm
   region, putting a floor under temperate rainfall, so median land fertility was 0.85 and
   marginal land did not exist. Fixed by making temperature *scale* rainfall rather than
   offset it, and adding a broad arid-belt noise field. Median land fertility is now 0.26.
4. **The decline counter measured the wrong thing.** It counted consecutive shrinking
   years, but collapse and recovery are wildly asymmetric — a settlement sheds people at
   ~17% a year and regrows at under 2%, so it spends five years falling and forty slowly
   climbing back. The counter peaked at five and then decayed. Replaced with
   `YearsDepressed`: consecutive years below half the peak, which accumulates *during* the
   long recovery instead of being erased by it.
5. **Populations never reached their ceiling.** The one that mattered most. At 1.6% annual
   growth a settlement founded with seventy people needed 269 years to approach a capacity
   of five thousand — so for essentially the whole run every settlement sat far below its
   ceiling with positive headroom, and a failed harvest merely slowed growth. Raising
   growth to 3.8% brings settlements to their ceiling in ~110 years, after which they live
   at the mercy of the harvest for two centuries. That is the regime the whole lifecycle
   was designed around, and nothing else worked until it held.

The lesson worth keeping: every one of these was a *calibration* fault presenting as a
missing feature. `SettlementLifecycleTests` now asserts the outcomes appear — promotions,
specializations, famines, tier declines — rather than that the code paths exist, because
the code paths existed the whole time.

**Harvest** is two noise fields, not one: weather on a ~9-year period and regional in
extent, plus climate on a ~70-year period covering a large part of the map. Weather alone
could not kill anything, because a settlement always recovered before a drought moved on.
Historical abandonments follow multi-decade climate shifts, so the slow component is the
one a village cannot outlast.

**Specialization** is chosen once, when a settlement outgrows a hamlet, from terrain with
culture as a thumb on the scale. Terrain proposes and culture disposes: a pious realm
founds more shrines, but nothing can put a fishing village inland. Each specialization then
carries its own fertility weight, flat capacity, harvest sensitivity and supply dependence —
which is what makes a bad decade empty the farming villages and leave the mining town
standing.

**Culture traits are now load-bearing.** Aggression drove fortification and Expansionism
drove expansion already; M4 adds Mercantile (town capacity, trade siting), Piety (shrine
siting and shrine capacity) and Tradition (how long a people clings to a dying town).

### Events: flat records plus narration templates

```csharp
record HistoryEvent(int Id, int Year, EventKind Kind,
                    EntityId Subject, EntityId Object, EntityId Location,
                    IReadOnlyList<EntityId>? Extra, DetMap<string,string>? Data);
```

Flat rather than a class-per-kind hierarchy. A hierarchy is the more natural object
model and the wrong shape here: it needs discriminated JSON and a viewer that knows
every subtype. Flat means a uniform array, trivial indexing, no deserialisation
ceremony at either end.

Three *named* slots rather than a participant list, because fixed names are what let
each kind have one prose template. **Templates ship inside the export**, so the viewer
renders event kinds it has never heard of — M6's wars and M8's plagues will appear
correctly with no viewer change. The alternative is a per-kind switch kept in sync
across a language boundary, which it would not be.

Template grammar (`Narration.SyntaxVersion`, mirrored in `viewer/src/app/narrate.ts`):

- `{subject}` `{object}` `{location}` — entity slots, become cross-links
- `{data:key}` — plain text from the payload
- `[ ... ]` — optional segment, dropped whole if any placeholder inside is absent

The optional segment is what keeps prose grammatical: `"{subject} was born[ in
{location}]."` renders "Aeda was born." rather than "Aeda was born in ." An earlier
attempt inferred optionality from comma positions; it worked until an event had two
absent slots in one clause.

**Append-only** is a real constraint, not a description. A war's outcome is a new event,
never an edit to the declaration. And events are appended in non-decreasing year order
— asserted, because the timeline and year index both depend on it, and back-dating a
figure's birth would silently break both.

### Entity ids

`EntityId` is a `(kind, index)` pair serialising as `"civ:3"` / `"fig:1204"`. Costs a
few bytes per reference and buys three things worth more: the JSON stays greppable when
a history looks wrong, viewer URLs are readable (`#/fig:1204` — the id *is* the route),
and a mistyped cross-reference fails loudly as a bad kind instead of resolving to some
unrelated entity.

**Ids are never recycled.** An entity that leaves history — a ruler dies, a settlement
is abandoned, a civilization falls — keeps its slot and is marked inactive. Every event
that referenced it still resolves. A chronicle whose references decay is not a
chronicle.

### Export format

A single self-contained `world.json`. This file is the **entire** contract between
engine and viewer: no shared code, no server, no schema negotiation.

- **No timestamp anywhere.** The export is a pure function of seed and config, so
  identical inputs produce byte-identical files — which is what makes the golden-hash
  test possible. Provenance is carried by `seed` + `configHash` instead.
- **Denormalised indices** (`eventsByEntity`, `eventsByYear`) computed once by the
  engine. Without them every entity page scans the whole event list on each
  navigation — fine at a thousand events, visibly slow at the 50k target. Values are
  integer indices into `events`, and event ids *are* their indices (asserted).
- **Raster as raw byte planes**, base64, not PNG. A PNG would bake in a colour ramp;
  the viewer wants its own, themed light and dark, with height/biome/rivers as
  composable layers. The height range ships alongside so metres are recoverable.
- **`schemaVersion`** is checked by the viewer, which refuses politely rather than
  misrendering a file it does not understand.
- Enums serialise as **strings** — numeric values would silently change meaning the
  first time someone inserted a value mid-enum.

Property declaration order in the DTOs *is* the file's byte layout (System.Text.Json
writes POCOs in declaration order).

### Naming: per-culture Markov chains over public-domain corpora

Chosen over procedural phonology, and built in M3.

**Corpora are public-domain and self-authored only.** Eight families — celtic, finnic,
hellenic, latin, norse, semitic, slavic, turkic — authored for this repository after
the public-domain historical record, all CC0. Zero attribution or share-alike
obligations, so `Naming/Corpora/` stays clean to redistribute inside a mod
indefinitely. Wiktionary's CC BY-SA lists were the faster route to broad coverage and
were rejected for exactly that reason: share-alike is awkward to unwind once it is
inside a published mod. They are embedded resources, so the assembly carries its own
training data.

**A family is not a culture.** No generated world contains "the Norse civilization".
Two mechanisms keep it that way:

- **Blending.** Each culture draws on 1–3 families with weights, blended at the level
  of transition *counts* rather than by alternating outputs — so the model learns that
  `-us` and `-vik` are both endings for the same name-shape and invents forms neither
  corpus contains. Weighted toward two-family blends: one stays recognisably a real
  tradition, three averages out bland. The first family dominates so a blend has a
  clear primary character. Eight families give 92 distinct palettes.
- **Sound shifts.** 1–3 curated substitutions (`th→v`, `s→sh`, `b→p`) applied to every
  name the culture ever produces. That consistency is what reads as a language: a
  culture that turns every `th` into `v` does so for its kings, its cities and its
  dynasties alike.

**Order 3, specifically.** Order 1–2 produces mush; order 4+ on corpora this size
mostly reproduces the training data, because each context has one or two continuations
and generation degenerates into recall.

**Novelty is enforced.** `MarkovNameModel` rejects any candidate present in its
training set. Not a nicety — the corpora are modelled on the historical record, so a
reproduced training name can be a real person's name.

**Names depend only on their entity id**, never on the order names are requested in.
So adding a system, or founding one more settlement earlier in a run, cannot alter any
existing name. The cost is that names are not guaranteed unique: deduplicating would
make settlement 40's name depend on what settlements 1–39 took, reintroducing exactly
the order-dependence the rest of the design removes. Collisions are rare across a
culture's few dozen settlements, real geography repeats place names freely, and the
viewer keys on ids regardless.

**Regions are named in a world-level language** derived from the world seed, not by
whoever claims them. Also the more truthful model: a river valley has a name older than
the realm that holds it. It shows in the chronicle — a Slavic-Semitic civilization
expands into `Bergajarvi` and `Ormsholmadal`.

`INameGenerator` is the seam. `PlaceholderNameGenerator` is retained after M3 because
numbered labels make simulation tests far easier to read, and skip Markov training.

Three defects found by reading generated output rather than by tests:

- `s→sh` fired on text already containing `sh`, emitting `shh` — "Vladishhovovo".
  A shift now skips text already in its target form.
- Ethnonyms built from place roots ran to 18 characters ("Lundfjordalilaiset"), and a
  civilization's name is the most-repeated string in the chronicle. Roots are now
  capped, cutting at a vowel.
- Root/suffix seams produced stutters ("Ilibalimim") and consonant pile-ups. `Join`
  drops repeated letters, resolves hiatus, and inserts a linking vowel.

**Lexicons are exported** per culture — the corpus blend, the sound shifts, and six
sample names each for people and places. The brief asks for per-culture name lexicons;
shipping the trained tables would be large and unreadable, whereas the recipe plus
sample output is what actually answers "why do this culture's names look like that".
The viewer renders it, so `slavic + semitic, b→p` sits next to `Ekallatograd`.

### Viewer: Astro shell, React island, client routing

Astro **could** read `world.json` at build time and statically emit a page per entity —
faster first paint, real URLs. Rejected: it would couple viewer builds to world
generation, so every regenerated history would need a site rebuild before it could be
looked at. The export is meant to be the whole contract, so the viewer is built once
and any world file drops into `viewer/public/worlds/`.

One Astro route hosts a React app with **hash-based routing**. Hash rather than History
API so the built bundle works straight off disk with no server rewrites. Navigation
never reloads the document, so cross-links never re-fetch and re-parse a file that can
run to tens of megabytes — and cross-linking is the product.

Map rendering is canvas (terrain raster via `ImageData`) plus SVG overlays (rivers,
territory, settlements), deliberately ignorant of what produced the terrain.

---

## Milestones

| # | Deliverable | State |
|---|---|---|
| M0 | Repo, solution skeleton, DESIGN.md | done |
| M1 | Vertical slice + terrain discipline | done |
| M2 | Determinism hardening | done (landed with M1) |
| M3 | Markov naming languages | done |
| M4 | Culture traits + settlement lifecycle depth | done |
| M5 | Figures: dynasties, succession, marriages | next |
| M6 | Diplomacy & war: named battles, territory transfer | |
| M7 | Viewer depth: territory rendering, richer filters | |
| M8 | Flavour: religions, artifacts, plagues, disasters | |
| M9 | Phase 2 spike: raster-backed `ITerrainSampler` | |

### M1 as built

Four yearly systems, in order (the order is hashed): `population` →
`settlement-lifecycle` → `expansion` → `figure-lifecycle`. Reads as a causal chain:
populations change, that changes what settlements are, pressure from that drives
expansion, rulers live and die independently.

Measured on seed 42, 300 years, 8 civilizations, 4096-unit world:

| | |
|---|---|
| Wall clock | ~65 ms |
| Events | 950 |
| Settlements | 96 (15 cities), 1 abandoned |
| Simulation samples | 6,050 (≈9.1s in Vintage Story) |
| Raster samples | 59,904 (≈90s — presentation only, budgeted separately) |
| Export size | 0.73 MB |
| Tests | 100 |

**Event volume** went from 359 to 950 with M4, and to ~4,000 over 800 years with 15
civilizations. Still short of the brief's 50k target, which arrives with M5's dynasties,
M6's wars and M8's flavour systems. The viewer is built for 50k regardless.

**Civilizations still do not fall** at this scale, and that is the honest outcome rather
than a gap to tune away. Capitals sit on the best-scored land and carry a capacity bonus,
so climate alone cannot finish one — a realm loses its marginal holdings and keeps its
seat. Collapse properly requires conquest, which is M6. One civilization does fall in an
800-year run, when its last settlements are lost.

**Abandonment is rare by design** — one settlement in 300 years, more over longer runs.
Marginal settlements are only founded once a civilization has run out of good land nearby,
and on a 1024-region world that takes centuries.

A sample of what M3 produces, seed 42:

```
  1  Zvonigyane was founded, with its seat at Shche.
  1  Ladimil became King of Zvonigyane at Shche.
 27  Ladimil died at the age of 63, of old age.
 56  Ascula grew into a town.
 79  Zvonigyane extended its reach into Bergajarvi.
180  Walls were raised around Shche.
216  Vladishov suffered a catastrophic failure of the harvest, losing 177 people.
230  Koprivnikice came to be known for farming.
255  Sandomice was abandoned after 254 years, its people lost to years of decline.
```

---

## Notes for Phase 2

Evaluate, in rough order of expected fit:

1. **Raster exports consumed via `ITerrainSampler`** — heightmap/climate/river rasters
   from any generator (Azgaar's Fantasy Map Generator among them). Makes almost any
   generator usable without binding to its codebase. Probably the pragmatic winner.
2. **Custom pipeline on FastNoiseLite** — C#-native, MIT, no interop.
3. **WorldEngine-style plate tectonics + climate**, ported or adapted.

What `ITerrainSampler` needs from whichever wins: height in metres relative to sea
level (sea level is exactly 0 by contract — no backend defines its own datum),
temperature, rainfall, and an honest `TerrainCapabilities` declaration. Rivers are
*not* required; hydrology derives them. If a backend does supply real rivers, declare
`TerrainCapabilities.Rivers` and hydrology becomes the fallback rather than the only
path.

Phase 2 is also where site selection should grow teeth — river confluences, harbour
quality, mountain passes, defensibility from real slope. All of it belongs in
`SiteSelection`, which is one function precisely so this can happen in one place.

## Notes for Phase 3

Primary candidate: **Algernon's Terrain Sampler Lib**
(<https://mods.vintagestory.at/algernonsterrainsampler>,
source <https://github.com/Algernon733/AlgernonsTerrainSampler>). Samples height
anywhere including ungenerated chunks, and as of v1.3.0 also rainfall, temperature,
geologic activity, forest density and shrub density — which maps closely onto
`TerrainSample`.

Constraints to design around, and how M1 already addresses them:

| Constraint | Handled by |
|---|---|
| ~1–2ms per sample; first sample in a fresh region slower | `TerrainAtlas` three-tier access, primed in bulk; asserted by sample-budget tests |
| Server-side, accessed by assembly reflection | Backend detail behind `ITerrainSampler`; engine has no VS reference |
| Tracks specific major game versions (terrain gen changes each update) | Pin the version; `configHash` records the inputs a world was built from |
| No rivers unless the Watersheds sampler is present | Hydrology derives rivers from elevation in every phase |
| Does not reflect terrain-modifying mods | `TerrainCapabilities` declares what is genuinely measured |

Two open questions for Phase 3:

- **Map raster cost.** ~60k samples is ≈90s in game. It is presentation-only and
  budgeted separately from the simulation for exactly this reason: lower the
  resolution, build it off-thread, or skip it and let the game render its own map.
- **Hydrology grid cost.** ~4,200 samples ≈6s. Acceptable as one-off worldgen, but it
  is the one place where the simulation's own sampling is not negligible, so it is the
  first dial to turn if worldgen time becomes a problem.

---

## Working notes

Generate a world (writes where the viewer reads it):

```bash
dotnet run --project src/HistoryEngine.Cli -- --seed 42 --years 300 --civs 8
```

Run the viewer:

```bash
npm run dev --prefix viewer
```

Tests:

```bash
dotnet test
```

When the golden fingerprint test fails, it means the history for seed 42 changed. That
is not automatically a bug — deliberately altering a growth rate, a system order or a
scoring curve changes every history, and the golden must be regenerated:

```bash
dotnet run --project src/HistoryEngine.Cli -- --seed 42 --years 300 --civs 8 --size 4096 --raster 64 --fingerprint > src/HistoryEngine.Tests/Goldens/standard-seed42.sha256
```

If it changes when you did *not* intend to change simulation behaviour, that is the bug
the test exists to find.
