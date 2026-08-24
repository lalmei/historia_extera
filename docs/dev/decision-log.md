# Historia Extera — Decision Log

A Dwarf Fortress-style world history generator for Vintage Story, plus a Legends-mode
viewer. This is the detailed record of what was chosen, what was measured, and why.
It preserves milestone-era assumptions and implementation retrospectives; the concise,
current design lives in the repository root as `DESIGN.md`.

**Status at the time of this snapshot:** Milestones 0–17 complete. Real naming languages, a settlement lifecycle that
runs its full course rather than only ever growing, rulers who inherit from a family
tree instead of appearing from nowhere, realms that fall to conquest as well as to the
weather, faiths and pestilence that cross the borders those realms draw, a map that
can be scrubbed to any year of the run to watch all of it happen, a world that can be
built on terrain the engine did not generate, and — since M12 — realms whose decisions
answer to whoever is governing them and to what has lately happened to them, rather
than to a culture fixed at worldgen and never revisited. Since then: grievance that
finally bites — a town that rises, breaks away, or whose governor marches on the seat
(M15); people the chronicle follows given a trade, journeys they return from, and the
wars they stood in (M16); and a host star — and, later, a host galaxy — rolled from the
seed that the world's own tomes can describe (M17).

The four most recent milestones — M15 unrest, M16 figure lives, M17 cosmology and tomes
— are recorded below as design and contract. Their measurement sweeps are marked pending
where they have not yet been run, in the same spirit as the "Numbers still to sweep" notes
the earlier sections carry.

---

## Phasing

| Phase | Terrain backing | State |
|---|---|---|
| 1 | Noise-based placeholder | **current default** |
| 2 | Open-source 2D terrain generator | reachable — raster route built in M9 |
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
- **Versioning the file is not part of the run's identity.** The fingerprint clears the
  engine release, the schema version and the narration syntax version before hashing. The
  digest answers one question — did the history for this seed change? — and none of those
  three is a fact about a history. Only the engine release was excluded at first, and the
  omission cost exactly what the config-hash asymmetry above predicts: four consecutive
  milestones each added exported fields and each bumped the schema, so the golden moved
  five times for four changes in behaviour and no reviewer could tell by looking which
  move was which. Adding a field still moves the digest, and should — a world carrying new
  facts is a new export. Renumbering the contract that describes those facts does not.

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

### Terrain raster interchange

Phase 2's backend reads terrain from rasters rather than binding to a generator's code,
so any generator that can export a heightmap is usable for the cost of one conversion.

**PGM (netpbm), not PNG.** The engine has no NuGet dependencies by design and the BCL
decodes no image format at all, so the interchange format has to be one that is a hundred
lines to parse. PGM is that, both encodings are accepted on read, and 16-bit is written —
eight bits over a 3,300-metre range quantises to 13-metre steps, which flattens the
coastal gradient that decides where a town goes.

**A JSON manifest carries everything a raster cannot.** A PGM knows its samples run
0..65535 and nothing else, so the manifest declares, per layer, what the extremes mean in
the field's own units, and for height additionally the normalised value that is the
shoreline:

```json
{
  "worldSize": 4096,
  "height":      { "file": "height.pgm", "min": -900, "max": 2400, "seaLevel": 0.2 },
  "temperature": { "file": "temperature.pgm", "min": -25, "max": 32 },
  "rainfall":    { "file": "rainfall.pgm" },
  "water":       { "file": "water.pgm" }
}
```

Only `height` is required, and it is the only field that cannot be inferred from the
others. `seaLevel` is what makes the datum contract hold: the range below it maps onto
`[min, 0]` and the range above onto `[0, max]`, two linear pieces meeting exactly at
zero, so a generator's own shoreline convention is honoured rather than approximated.
Layers with a natural [0, 1] range may omit `min`/`max`; temperature may not, because
0..1 °C is not a world.

**Absent layers are modelled, never claimed.** Missing fields are derived from latitude
and elevation and deliberately excluded from `TerrainCapabilities`, which is the first
time in this project that the flag set has had a backend declaring less than everything.

**The manifest and every plane it names are digested into `WorldConfig.TerrainSource`**,
because a file path is not the pixels and the determinism contract has to keep covering
the terrain. It contributes to `ConfigHash` only when set — an empty source means the
procedural backend, whose inputs are hashed already.

`TerrainRasterBake` writes this format from any `ITerrainSampler`, which is how the
round trip is tested and how a reference set is produced for comparison.

### Site selection: a score that describes the site, not the region

> **Built** as M10. The measurements below are of the world before it landed; *M10: the ground
> decides* in the milestones records what the build produced and the five places it disagreed
> with this design.

`SiteSelection.Best` refines an 8×8 grid of candidates inside a 128-unit region and ranks them
on fertility, a river flag, a coast flag and a height penalty. Measured over eight seeds and
542 settlements, here is what those terms are worth *across the 64 candidates of a single
decision* — which is the only place they do any work, since the choice is between them:

| Term | Spread within one decision |
|---|---|
| fertility and the height penalty, together | 0.071 |
| the river and coast premiums | 0.184 |

**The score has almost nothing to say about a site.** Fertility is built from climate
interpolated off a 256-unit lattice, so within a 128-unit region it is very nearly constant —
median spread 0.068 over a whole region. What is left is one boolean, quantised to the 64-unit
hydrology grid, worth two and a half times everything else. So a siting decision reads: *if a
river cell falls inside this region, stand on it; otherwise take the largest of 64 numbers that
differ in the third decimal place.* A quarter of decisions have no water variation at all and
are settled entirely by that noise. The premiums are not too small. They are the only thing
there, and they describe the region rather than the site.

**Nothing in the score knows whether the ground can be built on.** Those same eight seeds put
**19.6% of settlements on a grade steeper than 1-in-2, and the steepest on 2:1** — a cliff face.
That is not a mistuned weight, because there is no slope term: the question is never asked. And
the ground is not mute. A median land region spans 33 m of relief, and half of all land regions
contain a 16-unit step steeper than 1-in-2. The information is there and unread.

So M10 is not "raise the river premium". It is: **give the score terms that vary at the scale of
the decision it is making.**

#### Everything the new terms need is already paid for

`Hydrology.Build` calls `TerrainAtlas.SampleGrid(64)`, which memoises every point into the same
cache the three access tiers use. A full-world height grid at 64 units therefore already exists
before any settlement is sited, along with the flow graph, the drainage accumulation and the
submerged mask derived from it. This is the quiet consequence of hydrology getting its own grid
in M1, and M10 is the first thing to spend it on.

That fixes where each measure belongs. Anything varying at the scale of a *landscape* is derived
once on the 64-unit grid at world creation and costs nothing. Anything that has to distinguish
two candidates 16 units apart comes from the refinement the siting decision already performs, and
also costs nothing. **No new terrain samples.**

| Measure | Where derived | From | Read by |
|---|---|---|---|
| `IsConfluence` | 64-unit grid | ≥2 river cells draining into one | siting, specialization |
| `IsEstuary` | 64-unit grid | a river cell meeting the submerged mask | siting, specialization |
| `Shelter` | 64-unit grid | how enclosed by land the adjacent water is | siting, specialization, trade |
| `RiverDistance`, `CoastDistance` | 64-unit grid | integer chamfer transform over the grid | siting, habitability |
| `Ruggedness`, `IsPass` | 64-unit grid | neighbour height spread; saddle test on the 8-ring | siting, habitability |
| local grade, prominence | the decision's own 16-unit refinement | the refined candidates themselves | siting |

Two of these deserve their reasoning stated, because the obvious version of each is wrong.

**A coast cell is not a harbour.** Whether a place is worth landing at depends on whether the
water beside it is sheltered, which is a property of the water, not of the shore. So enclosure is
computed for each *water* cell — the fraction of its neighbours that are land — and a shore cell
takes the best enclosure among the water it touches. A headland and the bay behind it are both
"coastal" today and score identically; under this they separate, because the bay's water is
ringed by land and the headland's is not.

**Distance, not adjacency.** Every water term becomes a distance rather than a flag, because a
flag on a 64-unit grid cannot rank sixteen candidates that all fall inside one cell — which is
precisely the defect measured above. An integer 3-4 chamfer transform gives every point in the
world a continuous distance to fresh water and to the sea, relaxed with a FIFO worklist so it is
correct across the east/west seam and free of floating point in the propagation.

#### The character a site was chosen for

The chosen site keeps the reason it won, as one categorical value on the settlement:

```csharp
public enum SiteCharacter
{
    Plain = 0,      // unremarkable ground, taken for its soil
    Riverside = 1,
    Confluence = 2, // two rivers meet
    Estuary = 3,    // a river meets the sea
    Harbour = 4,    // sheltered water
    Coastal = 5,    // open shore
    Spur = 6,       // defensible high ground
    Pass = 7,       // the way through high country
}
```

A categorical value rather than the score vector, and for the same reason a chronicle records
that a city stands at a confluence rather than recording six weights: the numbers decide, the
category is what history refers to. It goes into the export at schema version 12 and gives the
viewer a sentence it could not previously write.

#### Standards this has to meet, or be cut

Held to the bar `Consort` was held to in M11 — a term nothing reads is decoration, and here a
term that changes no outcome is worse than decoration because it costs a golden regeneration.

- **The sample budget does not move.** Every new measure is derived from grids already sampled.
  If the budget rises, the design was wrong, not the budget.
- **Fertility keeps deciding *which region*.** These terms rank sites within a region; they must
  not become large enough to send a civilization to a barren defensible coast. `Region.Habitability`
  grading on real water access is in scope; inverting the relationship between soil and site is not.
- **Nothing may require a field only the noise sampler can produce.** Every measure here comes from
  height and the drainage derived from it, so a height-only raster backend gets all of it — which is
  what makes M9's "almost any generator" claim keep meaning something.
- **Each term must move an outcome.** Measured the same way the M8 faith-in-diplomacy coefficient
  was: run the seeds with the term zeroed and with it, and report the difference. A term that moves
  nothing gets deleted rather than kept for flavour.

**Rejected.** Floodplain modelling and river navigability by discharge class — both want a channel
model, and this grid resolves valleys, not channels. Defensibility from anything but ground: walls,
garrisons and fortification are a settlement's own doing and belong to whatever eventually reads
`IsFortified`, not to the choice of where to stand.

Every history changes, so the seed-42 golden is regenerated. That is the intended consequence of
altering a scoring curve, not a bug — see *Working notes*.

### Founding as a need: why a party was sent, not only where it stood

> **Built** for the ore, and only for the ore. The four other needs sketched below are designed and
> deliberately not built — see *What is not built, and why that is the point* at the end.

M10 gave the engine a score that could describe a site. It left untouched the question one step
above it: **which country a realm wants, and what for.** Expansion ranked unclaimed neighbours by
`Region.Habitability`, habitability is fertility with water and footing on it, so every settling
party ever sent out was a farming party. Nothing failed. The map simply had no reason in it — ore
was a thing settlements were retrospectively found to be *near*, never a thing anybody went
anywhere *for*.

**The shape is need first, then search, with farming as the usual need.** The crown's effective
values and fortunes pick the need; the search then looks for what that need wants, at a distance
that need justifies. The named founding leader is who gets *sent*, not who decided the realm was
short of metal.

| Need | When the realm wants it | What it searches for | Distance |
|---|---|---|---|
| **Land** (default) | people packed, empty neighbour, ordinary expansion | fertility, water, buildable ground | adjacent |
| **Ore** | few mines, a mercantile crown, geology sitting unused | geologic activity, height, ruggedness | worth walking past a farm cell — 3 hops |
| *Quarry* | walls going up, little stone, mountains unused | height and ruggedness, not ore | as above |
| *Harbour* | mercantile realm with no port, coast in reach | shelter, estuary, confluence | adjacent |
| **Frontier post** | hostile neighbour, recent war, a bellicose crown | the region between the two realms, and broken ground in it | close to the threat — 2 hops |

Mining was built first because it is the case the habitability sort is most wrong about, the
geology is already on the region, and `SiteCharacter.Mine` was already reserved.

#### The need reaches the ground twice

Once in the **search**, which is the part a habitability sort cannot do: a breadth-first walk out to
three regions, ranking on the deposit rather than the soil, crossing its own ground and empty ground
but never a neighbour's. Adjacency-only would have made an ore need nothing but a re-ranking of the
same candidates.

Once in the **siting**, where four weights move and nothing else does. Soil drops to a third,
per-candidate geologic activity comes in at nearly what soil is worth to everybody else, and the
thin-air penalty is cut to a third because altitude is a thing mining country simply is. **Slope
stays a penalty and is never inverted** — relaxed from 0.90 to 0.55, because miners live on a
terrace and walk to the face, and a camp on a cliff is as impossible as a town on one. That is the
M10 finding held to: use slope as buildability, never as a mining magnet.

The geology the site is scored on comes from the refinement the decision already performs, so a camp
stands on the best of the ore rather than merely inside the patch containing it — and **the sample
budget does not move**, because both searches read region fields derived once from the primed
lattice. 8,766 samples per run before, 8,722 after, against a 12,000 ceiling.

#### What it produced

Eight seeds, 300 years, against the same eight with the need switched off:

| | before | after |
|---|---|---|
| settlements | 532 | 541 |
| founded for a stated purpose | 0 | **58 (10.7%)** |
| settlements known for mining | 17 (3.7% of specialised) | **51 (10.8%)** |
| median region habitability under a mine | — | **0.568**, against 0.688 elsewhere |
| median geologic activity under a settlement | 0.452 | 0.461 |
| settlements on a grade steeper than 1-in-2 | 0.9% | 2.6% |
| terrain samples per run | 8,766 | 8,722 |

The habitability row is the one that matters: mine sites stand on measurably worse land than
everything else, which is the whole claim of a purpose founding and the one thing a ranking could
never produce. The steepness row is the price, and it is the relaxed penalty doing exactly what it
was relaxed to do — 14% of mine camps sit on 1-in-2 ground against 1.3% of everything else, and the
world figure stays well inside the 12% ceiling `SiteSelectionTests` guards.

#### Where the build found something

**A camp founded to work ore was being recorded as a farming village, and nothing noticed.**
Specialization scores soil and geology and has no idea anybody was sent anywhere, and farming opens
at 0.30 plus three quarters of the region's fertility — a lead mining has to climb to from geology
alone. With no prior, **5.8%** of ore camps were later known for mining and **81%** became farms. The
map said *mine* and the chronicle said *farming*, which is worse than either alone.

A prior of 0.35 at the specialization decision takes that to 72% mining, 24% farming, 4% market
town. It is deliberately not larger: at 0.55 it reaches 96%, which is `SiteCharacter` dictating
`Specialization` under another name. **The two are kept apart on purpose** — one is why they stood
there, the other is what the place became known for — and a seam that ran out while the valley
turned out to grow wheat is a history worth being able to have.

**Every realm that becomes a state gets its first mine; the crown decides the rest.** The appetite
is a share of the realm's own settlements, so below one whole settlement's worth it compares against
zero and any realm past three settlements with ore in reach will plant one. What the crown actually
decides is the second and the third: realms of eight settlements or more holding two or more mines
have a median mercantile value of **0.75**, against **0.51** for those holding fewer. That is the
measurement `ColonisationTests` pins, because it is the difference between the need being the
crown's decision and being a rule of the map.

#### What is not built, and why that is the point

- **The farming search is untouched.** Adjacent is correct for it: a party walking to the next
  valley for the soil has no reason to pass good ground to reach other good ground. Skipping cells
  already served by a neighbour's hinterland is a real improvement and a separate measurement —
  it moves every ordinary founding in the world, where this moved one in ten.
- **No separate deposit map.** `Region.GeologicActivity` above `Specializations.OreThreshold` is
  what "there is ore here" means, and that constant is now shared by all three decisions that ask —
  the search, the site's character, and the trade. Three copies of 0.35 would eventually drift into
  camps founded to work ore that can never be known for it.
- **No single minimum distance for all needs.** Farms want unused hinterland, forts want the border,
  mines want the deposit even when it is awkward. What going far costs a mine is already paid by
  supply: a camp beyond the roads is fed by `ImportReliance` and fails when the routes do.
- **Quarry, harbour and frontier post wait** for evidence that purpose search actually moves the
  map. One in ten foundings is the whole budget for *all* purposes if ordinary colonisation is to
  stay ordinary, and mining has taken it.

**Numbers still to sweep.** These were set by argument and one round of measurement, not by a sweep,
and each is a candidate for tuning later without redesigning anything: the ore appetite band
(0.03–0.14 of a realm's settlements), the three-hop reach and its 0.10-per-hop falloff, the four ore
siting weights, and the 0.35 specialization prior. The measured effects above are what a sweep would
have to beat.

Every history changes, so the seed-42 golden is regenerated.

### Settlement lifecycle: what made decline possible

The M4 deliverable was specialization and abandonment. Specialization was easy; making
abandonment *reachable* took six wrong answers, and each one is worth recording because
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
6. **An absolute floor beside the relative test.** Found much later, from the opposite
   direction: a thousand-year world kept growing and never lost anything. `FatalDeclineFraction`
   deliberately measures a settlement against *its own* peak, for the reason recorded below —
   an absolute headcount cannot be calibrated against a carrying capacity that varies by an
   order of magnitude across the map. A `FatalDeclineCeiling` of 400 people then sat directly
   beside it, and reintroduced exactly that failure. The relative test fires at 45% of peak;
   the ceiling demanded very much less, so anything that had ever grown past roughly nine
   hundred people could not be abandoned however far it fell. Over one five-century run,
   twenty-one settlements met the decline criterion — several below half their peak for a
   hundred and forty years — and **all twenty-one were held back by that constant alone**.
   Replaced with a tolerance that scales on peak population: a hamlet is given up after
   fifteen years of decline, a great city after two or three generations, and both can die.

   Why it survived so long: abandonment was not *zero*, so no test caught it. It still fired
   once or twice a run on places that never grew large enough to be vetoed, and
   `OnlyDiminishedSettlementsAreAbandoned` asserted only that the count was above zero — true
   the entire time the feature was broken. What was false is that decline could ever finish a
   real town. `ACityCanBeGivenUpAndNotOnlyAHamlet` pins the distinction.

   The consequence reached much further than settlement counts. Plague, famine, disaster and
   war all reduce population and none of them removes a settlement directly — they hand that
   to the lifecycle, which could not act on any of it. Four systems' worth of destruction had
   nowhere to land, so a world's settlement count only ever went up, and the map at a thousand
   years was a thousand years of accumulation with no mortality in it.

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
siting and shrine capacity) and Tradition (how long a people clings to a dying town). M5
gives three of them a second job in the dynastic systems, where they are far more visible:
Tradition chooses among the monarchical succession laws, Aggression sets how often a
succession is contested and how the loser fares, and Mercantile decides how outward-looking
a people is about marriage — so a trading culture's family tree reaches across the map while
an insular one marries its neighbours.

### Settlement density: what actually sets it

Prompted by a thousand-year map that looked like an explosion of settlements. It was not an
explosion. Founding is close to linear at a fixed expansion chance — cumulatively 23, 63, 108,
154, 208, 311 and 433 settlements at years 100 through 1000 — and the apparent burst is simply
a straight line seen at five times the usual length of a run. **Nothing was being removed**,
for the reason recorded as bug 6 above.

Two separate quantities were being confused, and they have different controls:

- **Whether the stock has an equilibrium at all** is the abandonment gate. Without it the count
  is monotonic and no amount of rate tuning changes that shape; with it the curve decelerates and
  the world acquires ruins, dead cities and territory that changes hands. This is also what makes
  plague, famine, disaster and war matter to the map rather than only to the chronicle.
- **Where that equilibrium sits** is `ExpansionSystem.BaseChance`. Measured at 1000 years with
  abandonment working, settled land is ~52–55% of land regions at `0.10` and ~26–39% at `0.06`,
  across seeds 42, 7 and 99.

So the honest answer to "is the expansion rate too high" is that it was the wrong question on its
own — but not a wrong change. Lowering the rate without fixing the gate only rescales a line that
still never bends; fixing the gate without lowering the rate leaves a land-rich world at half its
regions settled. The two are complementary and both are kept.

The ceiling on all of this is **land**, not time: a settlement claims a region, so a world cannot
hold more settlements than it has habitable regions, and every seed saturates in proportion to how
much land it has. A world that feels crowded is usually a world with a lot of land in it, which is
why the density target is expressed against land regions rather than as an absolute count.

### Where the people go when a town is given up

Making abandonment reachable exposed a second fault immediately behind it. Abandonment set a year
and released a region, and left the population sitting on the dead settlement where nothing counts
it again. While only settlements under four hundred people could be abandoned this was a rounding
error. Once cities could be, it was not: over a thousand years it silently discarded **4% to 25% of
a world's living population**, in single steps of up to six and a half thousand people.

The argument for moving them rather than capping the loss is that **the dying has already
happened**. Population, plague, disaster and war take their toll across the decades that drive a
settlement to 45% of its peak; that toll is the reason abandonment fires at all. Whoever remains at
the end is a survivor, and survivors walk. Deleting them was killing the same people twice, and it
corrupted the realm population that war and diplomacy both read.

- **Sixty percent arrive.** Not a conservation law, which would be a false precision — the far
  larger loss is the unattributed slide from peak to abandonment, which goes nowhere at all. The
  remainder die on the road or scatter to steadings below the size this engine models.
- **Shares go by the size of the receiving settlement**, a gravity model in miniature. Splitting by
  rank or distance alone lets a hamlet of fifty absorb a city and become one overnight.
- **Arrivals may exceed the receiver's capacity, deliberately.** That is what a refugee influx is,
  and the logistic decline sheds the excess over the following years — which can push the receiver
  into its own depression and occasionally finish it too. Seed 99 shows exactly this: seven
  settlements given up between years 617 and 621, a regional collapse travelling through the places
  that took each other's people in. That cascade is the behaviour, not a bug to cap away.
- **Own realm first, a neighbour's only when nothing of the people's own is in reach.** The faith
  does not travel with them; whether arrivals convert their hosts is the religion system's question,
  and it already reads the map this leaves behind.

Measured at a thousand years, the unaccounted population falls from 25% to 9.3% (seed 42), 20% to
5.7% (seed 99) and 4% to 1.9% (seed 7), and living population rises correspondingly because the
survivors are now somebody's.

### Dynasties: one traversal, two questions

The M5 deliverable was replacing "a successor is a new adult figure from nowhere" with a
real line of descent. Almost all of it reduces to one function, `Succession`, and the two
questions asked of it.

**The traversal.** From the last ruler, walk their descendants depth-first with the eldest
line exhausted before the next child is considered — so a king's grandson by his eldest son
outranks his own second son, which is what primogeniture *means* and what a sorted list of
relatives gets wrong. Only when that line is spent does the walk climb to the ruler's parent
and descend again, picking up siblings and their lines, then the grandparent for uncles and
cousins, and so on to the founder.

**Climb through the house, descend through anyone.** The upward climb stops at the first
ancestor who is not blood of the ruling house, because a claim originates in a house. The
downward walk is not house-bounded, because a daughter's children belong to their father's
house and still inherit under every law but the agnatic one — which is exactly how a crown
passes between houses without anyone dying out. House-bounding the descent is the tempting
simplification and it silently converts every such succession into an extinction.

**Two consumers.** `Claimants` answers who may take the throne. `Kin` answers who is close
enough to it for the chronicle to keep following. Same walk, different filter.

**The second question is what makes the milestone tractable at all.** A house where everyone
marries and every couple has children grows exponentially — three children a generation over
ten generations is fifty thousand people per realm. Capping births bounds the count and
produces implausibly small families. What is capped instead is *proximity to the throne*: a
figure is married off, and a couple has children, only while they stand near the front of
their house's line. As the ruler's own children are born, cousins are pushed down and quietly
stop being written about. That is not a claim about who had children; it is a claim about
whose children a chronicle bothers to name, which is the right thing for this engine to
model, and it makes the figure table grow with the number of reigns rather than with the
number of generations.

**Four laws, one tree.** Agnatic, male-preference, absolute, seniority and election are five
traversals of the same structure, derived from government form with Tradition choosing among
the monarchical variants. Republics and oligarchies additionally hold office for a *term*,
which is the one place government form changes the rhythm of a chronicle rather than its
vocabulary — a republic electing a consul every eight years holds four times the successions
of a monarchy over the same span.

**Where the calibration fights back.** Two failures were the same shape as M4's, and both
looked like sound modelling right up until a long run was inspected:

- **A house out of power stopped existing.** Ranking only against thrones currently held
  meant a house that lost one could never marry, never have children, and so could never
  return — five of eight founding houses froze at one or two members. Dormant houses now get
  a much smaller budget: followed at the head of their line and nowhere else.
- **Per-court ranking is a runaway; per-house ranking starves.** Ranking a house once per
  throne it holds gives a house with five crowns five times the fertile couples, and since a
  larger house wins more elections, that compounds — three centuries ended with two families
  of two hundred and everyone else extinct. Ranking once per *house* fixes that and creates
  the opposite failure: houses consolidate, the number of houses falls, and with it the whole
  tracked population — eight centuries ended with two houses and fifty living people in the
  world. The budget has to be per court, so the world follows a number of people
  proportional to the number of realms; the compounding is dealt with where it actually
  happens, in the ballot, which passes over a house that already rules elsewhere. That is
  also why historical elective monarchies balanced against dominant houses.

**Extinction is detected at the graveside**, not when a throne next falls vacant. A house
that has already lost power is never asked for an heir again, so leaving the check to
succession means the houses that fade quietly — much the commonest way a family ends — are
never recorded as ending at all. Note the distinction the check turns on: a house with no
*eligible* claimant is not extinct. An agnatic house reduced to daughters has plenty of
living members and still cannot produce a king, and those daughters carry their father's
claim into another house's nursery.

**Mortality is now the young end of the curve as much as the old.** Rulers were the only
people who existed before M5, so the curve started at twenty. A flat curve through infancy
means every heir born survives to inherit, so no house ever fails and no throne passes
sideways — the succession machinery would be entirely correct and never exercised. Roughly a
fifth in the first year and a quarter before five, where pre-modern populations actually sat,
is what makes an heir predeceasing their father happen at a believable rate. The adult rate
sets reign length and through it the whole event volume of a chronicle.

### Figure deaths: provenance before variety

The first mortality model answered *when* figures died and barely answered *how*. Every
living figure faced the age curve every year; a death below fifty-five was labelled illness
and one above it old age. Plague used the same illness label, disasters touched anonymous
population only, and battles could kill only the current ruler. On seed 42 that produced
800 illness-or-age deaths out of 825, while assassination and accident existed as enum values
with no path that could ever produce them. The output was internally consistent and much less
varied than the world it described.

**A cause now belongs to the system that caused the death.** Plague records `Plague`, a
capital disaster records `Disaster`, a commander lost after a battle records `Battle`, and
personal incidents happen before the biological mortality pass. The lifecycle does not roll a
death and decorate some fraction of its results with colourful labels. That would change the
story without changing the model; provenance makes the story a report of what actually ran.

The categorical cause remains deliberately small and filterable. A separate optional detail
carries the particular form when the originating system knows it — the named plague, wildfire,
or a riding accident. This is why export schema 8 adds `deathDetail` rather than multiplying
the enum into one value per plague name and disaster kind.

**Exceptional deaths require exposure:**

- Figures have a realm residence, not a continuously simulated street address. A disaster
  therefore reaches them only when it strikes the capital, the one settlement where the court
  can honestly be placed. Its population severity becomes a much smaller per-courtier risk, and
  the disaster event links every named casualty so their page retains the event that killed them.
- Political violence considers the ruler, a regent and the strongest adult resident claimant,
  and only where a credible claimant exists. Aggressive and wartime courts are more dangerous.
  Poisoning is likelier at restrained courts and direct assassination at aggressive ones. A
  murder names a suspect only from people the court already had reason to fear — the claimant,
  a lately disgraced officer, or an enemy ruler in a war being fought — and even then some
  deaths stay as unknown hands. Living spouse, parents, children and siblings are indexed on
  the death so it appears in their chronicle; they carry a short blood-debt that keeps the
  same plot dangerous to them, and a named suspect at that court may later be executed for it.
  The realm takes it as a grievance, not a calamity: this was done inside the court.
- Adult figures face a very small accident risk, scaled by the culture's martial and travelling
  tendencies. Accident details are flavour attached to an event the system actually rolled.
- When a ruler does not command in person, the court may appoint an adult resident dynast.
  Commander fatality remains 14% for a defeated commander and 3% for a victor, so more figures
  can acquire military lives without every engagement becoming a dynastic decapitation.

The calibration target is not equal shares. Most people in a multi-century court record should
still die of illness or age; battles, disasters and murder are memorable partly because they
are exceptional. The test contract is that all supported causes are reachable across several
standard seeds, exceptional deaths are visible but bounded, and ordinary mortality remains the
majority.

Measured across seeds 2, 7, 11, 42 and 99 at 300 years: **4,363 deaths, 92.7% illness or
old age and 7.3% exceptional** — 82 in childbed, 70 by accident, 58 in battle, 37 by named
plagues, 36 executions, 20 assassinations, 9 poisonings and 8 in disasters. Of 604 named
battle commands, 216 went to figures who were not rulers at the time. Seed 42 itself records
every supported cause and remains close to the aggregate at 7.9% exceptional deaths.

### Regnal numbers, and what M5 did to naming

M3 accepted that names are not unique, reasoning about "a culture's few dozen settlements".
M5 took a world from eighty named people to over a thousand, and that reasoning no longer
covers the regime: **over half of all figures now share a name with someone else in their
culture**, and in an 800-year run **43% of reigns belonged to a realm that had already had
another ruler of the same name**. A line of succession reading *Stein, Gunn, Stein, Vella,
Stein* is unreadable.

The fix is the one every real chronicle uses. A ruler who shares a name with a predecessor of
the same realm is numbered at accession, and the first of the name is numbered retroactively
when the second appears — which costs nothing, because events carry ids and resolve names
when they are rendered. Numbering depends on who ruled rather than on the order names were
requested, so it keeps the property `INameGenerator` exists to protect.

It deliberately fixes only the case that matters. Two unrelated cousins sharing a name is
what real onomastics looks like, and the viewer distinguishes them by dates and house.

### Offices: what a court does with the people it already has

> **Built**, after M12. The measurements below were of the world before either landed, and the
> numbers attached to the proposal were estimates; *What it cost* at the end records what the
> build actually produced and the four places it disagreed with this design.

M5's attention budget breeds a court full of people and finds work for exactly one of them.
On seed 42 at 300 years the chronicle holds 1,107 figures, of whom 831 reach sixteen and
**658 — 79% of the adults — never hold an office of any kind**. Three hundred and eighty of
those are dynasts: the cadets the budget deliberately produces so that houses do not fail, and
then has nothing to do with. Between them they live 20,813 recorded years past sixteen in
which nothing happens but a marriage, some children, and a death. The entire title vocabulary
is six strings — Chief 59, Archon 51, Consul 41, King 23, Hierarch 16, Regent 8 — and every
one of them is a head of state or a stand-in for one.

So this is not a request for more people. It is a use for the people the world has already
paid for, and the reason to expect it to be cheap.

**An office has to change what some other system does.** The standard is the one
`CultureValues.Tradition` failed until M4: a field read by nothing is decoration, and
decoration in a chronicle reads as noise. Four offices earn a place, each because some system
is currently answering its question badly or has no way to ask it.

| Office | Held over | Read by | What it replaces |
|---|---|---|---|
| **Marshal** | a realm | `Warfare.Commander` | a fresh roll over kin at every battle, so nobody accumulates a military life |
| **Governor** | a settlement | disasters, plague, sackings, expansion | figures having no address finer than a realm, so only the capital can reach them |
| **High Priest** | a faith, within a realm | `ReligionSystem` | `Preacher` — "the oldest living adult not on the throne", which is a placeholder and reads as one |
| **Consort** | a realm | `Diplomacy.MarriedIntoTheHouseOf`, regency | a dynastic tie that counts the same whether it is the king's marriage or a third cousin's |

Rejected for exactly the reason they are tempting: steward, chancellor, spymaster, court
physician. Nothing would read them, and an office nothing reads is a title generator.

**Consort is the weakest of the four and should be held to a stated bar.** Two hooks justify
it, and if neither survives implementation it should be cut rather than kept for flavour.
First, a *crowned* consort's tie to their birth house counts for more than any other marriage
between the two houses, which turns today's all-or-nothing dynastic warmth into something that
tracks whose marriage it actually was. Second, a crowned consort who outlives the ruler is the
dowager, and a dowager regent is an *appointing authority* — the queen mother naming her own
kin marshal is the sort of thing chronicles are made of, and it falls out of this design
without an intrigue model to support it. Note also what the office explains by its absence: a
republic does not crown a consul's wife, so a republic's chronicle has no queens, and that is
content rather than an omission.

#### The record: an office, not a string

`TitleHolding(Title, CivilizationId, FromYear, ToYear)` is nearly the right shape and cannot
express the four things this needs: what kind of office it is, what body it is held over, who
granted it, and why it ended. It becomes:

```csharp
public sealed record OfficeHolding(
    OfficeKind Kind,        // what systems read
    string Title,           // what the chronicle says: Marshal, Strategos, Warlord
    EntityId CivilizationId,// the realm the office sits in — never None
    EntityId ScopeId,       // the settlement or faith, where the office is over one
    EntityId GrantedBy,     // the ruler or regent who named them, None if chosen internally
    string Claim,           // "by the king's mandate", "by the town's own council"
    int FromYear,
    int? ToYear);
```

The string title stays, because "Marshal" in one culture and "Warlord" in another is the
cheapest character this engine buys. The enum is what systems branch on, and the split is
load-bearing rather than tidy: `EntityPages.tsx:618` currently identifies a reign with
`titles.find((title) => title.title !== 'Regent')`, and that line silently starts returning
marshalships the day a second non-ruling office exists. `Lists.tsx:841` has the same problem
with its Ruled / Never ruled filter. Adding offices without adding a kind would not break the
viewer loudly; it would make it quietly wrong, which is worse.

`Claim` is borrowed from `Houses.Enthrone` deliberately. A coronation is far more readable for
saying which rule produced it, and an appointment is the same: "by the king's mandate" and "by
the town's own council" are the whole difference between two governorships that are otherwise
identical rows.

#### Three ways a seat is filled

This is the substance of the design, and the part that makes two realms' appointment histories
read unlike each other.

- **Mandated.** The ruler names a holder from the court. The appointment is personal to the
  ruler who made it and lapses when their reign does — new king, new marshal.
- **Internal.** The body chooses its own: a town's notables, a faith's clergy. The holder is
  usually not a dynast, and serves until death.
- **Customary.** The office runs in a family. The last holder's heir takes it and the crown's
  part is to acquiesce; it ends only on death or on that line failing.

Defaults by office, before circumstance moves them:

| Office | Leans | Because |
|---|---|---|
| Marshal | mandated | an army is the one thing no ruler delegates by custom |
| Governor | contested — this is where the interesting decision lives | see below |
| High Priest | internal, customary at high Tradition | a faith that lets the crown name its priests is a faith the crown has absorbed |
| Consort | mandated by definition | it is the ruler's own marriage being recognised |

**What decides a governorship** is four things the engine already knows and one it does not.
The crown's inclination to mandate rather than delegate rises with the ruler's own hand
(below), falls with the culture's Tradition — a people attached to its own customs resents an
appointee — rises while the realm is at war, falls with distance from the capital, and rises
sharply for a settlement taken in war within living memory. A city is worth a court
appointment and a village is not worth the journey, so the office is only filled at all above
a size threshold, plus wherever the crown has a specific reason. That threshold is the same
line `MaybeFortify` already draws at Town, which is not a coincidence worth hiding: it is the
size at which this engine starts treating a place as somewhere that has interests.

The capital is governed by whoever holds the throne, in person. It gets no governor.

#### One dial for the ruler, and not a personality system

"Depending on that leader's desires" needs a leader who has any. Figures have no traits at
all today; behaviour is entirely cultural, which explains why *realms* differ and leaves
nothing to explain why one reign differs from the next in the same realm.

One scalar: `Figure.Centralism`, in [0, 1] — how much this person insists on choosing. Rolled
in `Houses.NewFigure` from a fork keyed on the figure's own id, the
`rng.Fork("faith", id.ToDiscriminator())` pattern, so it cannot depend on how many people were
born before them. Its mean comes from government form (a chiefdom's chief has little machinery
to appoint with; a monarchy or theocracy has plenty; a republic distributes by construction)
and the figure varies about that mean by a modest spread.

**One field, and the discipline is in refusing the second.** A five-trait personality record is
the obvious next thought and nothing would read the other four. Anchoring to the culture's mean
rather than rolling free is the other half: independent rolls per ruler make a line of
succession read as uncorrelated noise, where an anchored roll reads as "this people centralises,
and this king unusually so", which is the sentence worth being able to write.

#### Candidates are the people the chronicle already follows

The pool for a mandated office is kin — adult, living, resident in the realm, not the ruler —
which is the same walk `Warfare.Commander` already does through `Succession.Kin`.

**Standing below the front of the line is a qualification, not a disqualification.** The heir
is needed at home; a fourth son is precisely who gets an army or a colony. That inverts the
rank test the household system uses to decide who stops being written about, and it is the
whole reason this feature pays for itself rather than costing: the same 380 idle dynasts the
budget already produces become the candidate pool, at no addition to the figure table.

**A body invents its own only when the court will not do.** An internally-chosen governor is
by definition not a court dynast, so where no candidate exists — or the fill mode says the town
chooses — a local notable is created, of no house, exactly as `MatchAtHome` already invents
consorts. That cannot compound: `HouseholdSystem.Marriageable` refuses anyone with
`DynastyId.IsNone` already, so invented notables hold an office, die, and are replaced without
ever entering a nursery. The guard exists; this design only has to avoid reaching around it.
The bound is one holder per seat, so invented figures scale with settlements above the
threshold and faiths, neither of which compounds.

#### An office never moves a figure between realms

`Figure.CivilizationId` is untouched by this entire system. That is an invariant rather than a
detail: `HouseholdSystem.WhoMoves` documents what happened the last time a figure was moved
carelessly — three realms in a three-century run governed by a corpse for over a century each,
and nothing in the chronicle said so because the events simply stopped.

What an office over a place changes is a new field, `Figure.ResidenceSettlementId`, defaulting
to the realm's capital. This is what a posting *means*, and it repairs a compromise already
recorded above under *Figure deaths*: a disaster reaches a figure only when it strikes the
capital, "the one settlement where the court can honestly be placed". A governor can honestly
be placed in the town they govern. A governor therefore dies in the sack of their own city, in
their own city's plague, and in its earthquake — three exposures that exist and cannot currently
reach anyone outside a capital.

#### Founding parties

`ExpansionSystem` conjures 70 settlers from nowhere and nobody leads them. Three changes, all
of which the user-facing feature needs anyway:

- The party is **drawn from a real parent settlement** — the nearest active one of the realm to
  the target region — and deducted from its population. Expansion currently costs nothing,
  which is why a realm can seed a continent without ever feeling it.
- It is **led by a named figure** where the court can spare one: an adult dynast ranked below
  the fertile front of the line, or, failing that, a notable of the parent town.
- The leader becomes the colony's first governor, `Claim` = "by the founding of the town".

The founding stays one `SettlementFounded` event with the leader in `extra` and two new data
keys, mirroring what `SettlementAbandoned` already does with `resettled` and `refuge`:

```
"{subject} was founded[ by {object}][, {data:settlers} of them out of {data:from}]."
```

The `OfficeGranted` for its first governor follows immediately, so the two read consecutively —
the same pattern `SettlementPromoted` and `SettlementSpecialized` already rely on.

**The second-order effect is the one worth wanting and is deliberately not built here.** A
cadet planted in a colony, whose children are born there, is a branch of a house with a seat
that is not the capital — which is where a breakaway realm comes from. This design creates that
state and does nothing with it. Naming it now is what stops someone modelling secession later
by inventing a rebel from nowhere.

There is also a pleasing loop available and worth taking: a court with adult dynasts and no
offices to put them in has a reason to send one out. Expansion pressure currently comes only
from population per settlement, and "the king has three grown brothers and one throne" is a
historically better answer than most of what is already in that formula.

#### When an office ends, and what gets written down

Four endings: the holder dies (already handled — `Figure.EndAllTitles`, and M8 has already paid
for getting that wrong once); a mandated appointment lapses with its granter's reign; a holder
is dismissed in disgrace; or the body itself ends — the settlement is abandoned, the faith
fades, the realm falls.

**An appointment lapsing with its granter is not an event.** This is the volume decision and it
should be made before anything is written, not after the first run is inspected: recording both
ends of every office roughly doubles the events this feature adds and the second one carries no
information the next grant does not. Deaths already have an event. Only grants and dismissals
are recorded.

Dismissal wants a cause the model actually has: a marshal who loses badly, a governor whose
town is sacked or falls into famine. Scaled by the court's aggression, as political violence
already is — and a disgraced marshal is then exactly the kind of figure `FigureIncidentSystem`
should be able to reach, which is the connection that makes `Execution` reachable for somebody
who is not a failed claimant.

Two new event kinds, in the figures block, leaving 322–329 free:

```
OfficeGranted = 320   "{subject} was made {data:office}[ of {object}][ at {location}][, {data:claim}]."
OfficeRevoked = 321   "{subject} was stripped of the office of {data:office}[ of {object}][, {data:cause}]."
```

#### What it costs the chronicle

Seed 42 at 300 years currently writes 3,590 events across 190 coronations and 64 term endings.
**Estimated**, on one holder per seat and the lapse rule above: roughly 200 marshalships, 250 to
400 governorships, 80 high priests and 90 consorts — **600 to 800 grants, or +17% to +22% on
today's event count**. That is affordable and it is not negligible, so the levers should be
known in advance: the governor size threshold, whether term-limited governments re-appoint on
every term (they should not — eight-year churn across every office is the single likeliest way
this reads as bureaucracy), and whether internal appointments are recorded at all in places the
crown never touched.

#### Where the calibration will fight back

**Predictions, not measurements.** Five, in the order they seem likely to bite:

1. **Republics and oligarchies churn.** A consul standing down every eight years re-appointing
   every office is four times a monarchy's turnover, and offices are the wrong place for a
   government form to express itself — it already expresses itself in the succession rhythm.
   Term governments should lean internal, which is also the historically apt answer.
2. **A standing marshal collapses the variance of who commands.** Today 216 of 604 named
   commands went to non-rulers, spread across whoever the per-battle roll found. One marshal per
   realm concentrates that onto one person who either dies in his first season or appears
   undefeated for thirty years. The marshal should be a strong candidate in
   `Warfare.Commander`, after the existing government-and-aggression roll for the ruler taking
   the field in person — not a short circuit past it.
3. **Governor grants drown the chronicle.** Thirty-eight settlements at Town or above at the end
   of a 300-year run, each turning over every twenty years, is the largest single contributor to
   the estimate above and the least informative event in it.
4. **Offices become a second attention budget, competing with the first.** The rank map is built
   once a year and read by marriage and birth; an office pass that also walks `Succession.Kin`
   for every realm is a second traversal with its own idea of who matters. They must agree, or a
   cadet will be simultaneously too remote to marry and close enough to command an army.
5. **The viewer's figure filters silently change meaning.** "Ruled" becomes "held any office" and
   nobody notices for a milestone. This is the cheapest of the five to prevent and the easiest
   to forget, because nothing fails.

#### The test contract

- Every office kind is reached across the standard seeds, and every fill mode with it — a purely
  mandated world means the culture inputs are not connected to anything.
- No grant to a dead, absent, under-age or already-seated holder; no two holders of one seat.
- Offices close on death — extend the existing `MortalityTests` check that no title outlives its
  holder, which is the assertion that caught the three-century regent.
- No office-holder's `CivilizationId` changes as a result of holding it.
- Invented notables never acquire children, which is the assertion that the existing
  `Marriageable` guard is still doing its job.
- Figure-table growth stays linear in reigns: the count at 600 years is within a stated factor
  of the count at 300, as it is today.
- `DeterminismGuardTests` covers any new per-realm map, which must be a `DetMap`.

Export schema goes to 9: `ExportTitle` gains `kind`, `scopeId`, `grantedBy` and `claim`, keeping
`civilizationId` so existing viewer reads survive; `ExportFigure` gains
`residenceSettlementId` and `centralism`. The golden fingerprint changes and must be
regenerated — this alters every history from year one, since `Houses.NewFigure` draws a
disposition for everyone born.

#### What this deliberately does not do

No councils and no factions. No competence or skill ratings on office-holders — a marshal who
wins is a marshal who was lucky, and the chronicle cannot tell the difference either. No
factional intrigue: an office roster is still not evidence of a plot. Murders later gained a
bounded form of it from state the court already had — a claimant, a disgrace, a wartime enemy —
without turning offices into factions. No revolt, no secession, no provinces spanning several
settlements. Each of those wants this state to exist first, which is the argument for building
it plainly now rather than in the shape some later feature might want.

#### What it cost, as built

Seed 42 at 300 years: **451 grants and 18 disgraces against 4,162 events, 11% of the chronicle**
— inside the 600–800 estimate and at the low end of it, because the fill roll spreads a reign's
appointments over several years rather than filling every vacancy the moment it opens. The world
is otherwise unmoved: across six seeds, cities 102 → 103, active settlements 356 → 354, realms
standing 37 → 36. Figures rise 13.8%, which is the one-time cost of the seats themselves and not
a change in growth rate — doubling the run still only doubles the count, as it did before.

The offices are read, which was the standard set for them. Of 64 named commands on seed 42, 20
went to a sitting marshal; four governors died of plague in the towns they governed, a death
that could not previously reach anyone outside a capital; and schisms are now preached by a
realm's own senior cleric rather than by whichever adult happened to be oldest.

Four places the build disagreed with the design:

- **Founding parties needed a floor on the town they leave.** Deducting settlers made expansion
  cost something, which was the point — but without a minimum left behind, a realm reduced to one
  struggling village would send parties out of it until the abandonment threshold finished it.
  Expansion is meant to be a symptom of success, and it had become a way for a failing realm to
  kill itself.
- **A colony's first governor breaks the size threshold on purpose.** Governorships are for towns
  and a colony of seventy is not one, but somebody led those people there and is who a chronicle
  names. When they die the place governs itself until it grows into the threshold, which turns out
  to be the honest shape of frontier administration rather than the exception it looked like.
- **A governed town promoted to capital kept its governor.** Capitals move — the succession system
  repoints a realm whose seat was abandoned or taken to its largest surviving settlement, which is
  exactly the town most likely to have had one sitting in it. The lapse pass now checks for it.
- **Two test invariants were stated too strongly and failed correctly.** A crown is inherited
  rather than served, so the service-age requirement cannot apply to a child on a throne under a
  regent. And a figure of no house holding an office is not necessarily an invented notable: a
  widowed consort made regent for her own child is exactly the dowager path this design wanted,
  and she has children by definition.

One prediction landed and one did not. Governor grants are indeed the largest single contributor
to the volume, as expected. But the marshal did **not** collapse the variance of who commands —
capping a standing marshal at three fields in four left non-rulers a wide spread of commands,
because a realm fights on more than one frontier and a marshal cannot be at both.

#### What a review of it found

Six things, and the pattern in them is worth more than any of them individually: **every one was
invisible to a green test suite**, and three were invisible because a test asserted something
adjacent to what it claimed.

- **A governor was never reached by anything.** The exposure a residence exists to create was
  wired to disasters alone, which fire roughly once in three worlds on a governed town — across
  five seeds no governor had ever died of one. The test said otherwise because it counted deaths
  by "disaster *or plague*", and plague is modelled at the realm level and had been reaching
  governors since long before offices existed. It passed on a mechanism it was not testing.
  Sacking, which the design named first and which had been left out entirely, is the right
  exposure: a sack is aimed at a particular town by an army that has just carried it, so it
  coincides with a governor far more often and reads better when it does.
- **A fallen realm kept its officers.** `Realms.Fall` ended the ruler's office and nothing else,
  and the release pass walks only standing realms — so a dead realm's marshal and governors held
  their posts for the rest of the run. Exactly the shape of the regent who was recorded as
  governing for three centuries after he died, and found the same way: an invariant test noticing
  a figure holding an office of a realm they no longer lived in.
- **Two sovereigns married to each other kept their own courts, and the consort office claimed one
  of them anyway.** `Enthrone` makes a deliberate exception for a spouse who holds a throne of
  their own; recognising them as a consort regardless gave one figure an office of a realm they
  had never lived in, re-granted every year as the release pass threw it straight back. The
  release pass was also scanning only each figure's *newest* open office, which is unambiguous
  only while nobody can hold two — a property of the grant rules rather than of that loop.
- **The consort's second hook was never built.** The design set an explicit bar — two consequences
  or cut the office — and shipped one. A crowned consort's tie to their birth house now outweighs
  a third cousin's marriage, which is what turns all-or-nothing dynastic warmth into something
  that tracks whose marriage it actually was.
- **Wartime never entered the fill decision**, though the design named it among the inputs.
- **`ResidenceSettlementId` was never exported**, though the design said to export it.

**And residence is now kept for everyone, not only for office-holders.** "At court" used to be
inferred from the absence of an address, which answers the question exactly as long as nothing
needs to know where an ordinary figure is. It follows a birth, a marriage, a crown and a posting;
a recalled governor takes his household back with him. Reading it goes through
`WorldState.ResidenceOf` rather than the field, because the stored value is allowed to go stale —
a town can be abandoned or taken with people living in it, and making every system that moves a
settlement chase its residents is the coupling the resolver exists to avoid.

**What was still open, and is not now:** `FillMode.Customary` was a value nothing produced, because
an office cannot run in a family until raised notables have one. M14 gave them one, and the third
mode is produced by `Offices.HeirTo` — see the section below.

### Notable households: what an office raises out of the population

> **Built**, in M14. The careers landed with M11; the families and `FillMode.Customary` landed
> together, because neither is worth anything without the other — an office cannot run in a family
> that was never allowed to exist. What the build changed about the plan is recorded at the end of
> this section.

**An office is filled from a house or from the ordinary population, and the population is the
larger door.** Of 279 appointed office-holders on seed 42, 104 came from a house and 175 were
raised. That ratio is right and worth keeping: a realm's marshals should not all be the king's
cousins, and the whole point of `Offices.Courtiers` skipping the front of the line is that the
people it does supply are cadets nobody else had a use for.

**What was wrong with the raised half.** They were invented at 26–45 whatever the office was, so a
high priest and a town's headman were the same age on average, and neither had done anything to
get there. An office is the end of a career and the age band is how long that career took: a
marshal has served (32–52), a high priest has risen through a temple (38–62, the slowest ladder),
a governor is established in their own town (30–55). Each now carries the door they came through
— soldiery, clergy, townsfolk — which is the difference between a figure and a placeholder.
Measured after: median age at appointment 52 for priests against 42 for governors.

**There is no birth event for these people and there cannot be.** The chronicle is append-only in
non-decreasing year order, so a birth forty years before the appointment that introduced them
cannot be inserted — the constraint `Houses.NewFigure` already records for anyone created grown.
What they have is a real birth year, worked back from the office, so a reader sees a life rather
than an entrance. Anything more would need the chronicle to accept back-dated entries, which is a
much larger change than it looks and would cost the property that events can be replayed in order.

#### The families, and the bound they need

**This is the one part of the offices design that can break M5's linearity, and it needs the same
trick M5 used.** `HouseholdSystem.Marriageable` refuses anyone of no house, which is exactly why
175 raised notables cost nothing today: they hold an office, die, and are replaced. Give each of
them a spouse and two or three children and the figure table gains about 56% — and if those
children then marry and breed, the growth is exponential, which is the failure the attention
budget exists to prevent.

M5 did not cap fertility; it capped *proximity to the throne*. The same move works one level down:
**a notable's household is followed while they hold office, and for a generation after it ends.**
Their children are recorded and are themselves extended only if one of them takes an office. The
extra population is then a level shift — one spouse and a few children per seat — rather than a
growth rate, and the same sentence justifies it that justified the original budget: this is a
claim about whose children a chronicle bothers to name.

**The generation of grace is the part worth arguing for.** Following a family only while the
office is held makes them vanish the year the holder dies, which is both wrong and useless: a
local family's standing outlives the post that gave it to them, and the whole reason to track
them is what happens *between* appointments. Twenty-five years is a child growing up, which is
the interval that matters, because it is the one in which the next holder could come from the
same household.

**And that is what makes `FillMode.Customary` reachable.** The third mode is "the office runs in a
family: the last holder's heir takes it and the crown's part is to acquiesce". It has been
unbuildable because raised notables had no heirs. With households it becomes the natural third
branch of `ChooseMode`, weighted by the culture's Tradition — which is where a hereditary local
gentry comes from, and eventually the pressure a centralising ruler pushes against.

**Where it will fight back**, in advance:

- **The share of raised holders is itself a dial.** Households make the raised path more
  expensive, so if the figure count runs hot the first lever is not the family model but the
  balance between `Courtiers` and `Notable` — a court that fills more of its own seats needs
  fewer invented families.
- **Customary succession can ossify a realm.** An office that always passes to the last holder's
  son stops being a decision, and a realm whose every seat is hereditary has no appointments left
  to read about. Tradition must weight it, not decide it.
- **Two attention budgets that disagree.** The rank map and the office-proximity rule will both
  be walked every year and must not contradict each other, or a person will be simultaneously too
  remote for the chronicle to marry off and close enough to inherit a governorship.

#### As built, and the three places the build disagreed

**One window, not two.** The design's sharpest warning was that the rank map and the office-proximity
rule would be walked every year and could contradict each other — "a person simultaneously too remote
for the chronicle to marry off and close enough to inherit a governorship". The build removes the
possibility rather than testing for it: `Offices.GraceYears` is one constant, `Offices.HeadsAHousehold`
is one predicate, and notable heads are ranked **into the same `DetMap` the houses are ranked into**.
There is one attention budget, and the same lookup answers for a king's fourth son and for a governor
raised out of a provincial town.

**Only the head is ranked, and that is the whole bound.** The spouse needs no rank, because `Bear`
already asks whether *either* parent is near enough; the children get none, so they are recorded, grow
up, and are not themselves extended. A child who takes an office becomes the head of a household in
their own right — the one door out, and the one the design named. The second guard is
`FindPartner`, which now refuses anyone of no house outright: a notable married into a dynasty would
put their children in a line of succession, after which they are ranked by proximity to a throne
rather than by the window that raised them, and the bound passes out of the office system's hands.

`FillMode.Customary` is tried only where the crown did not name somebody, which is what makes "the
crown acquiesces" true of the model rather than only of the prose — a ruler who wanted the seat has
already taken it. Tradition weights it from 0.15 to 0.70 and the governing person's Centralism scales
it down to as little as 0.35 of that.

Where the build disagreed with the plan:

- **The cost estimate was right, and arrives far more slowly than a level shift should.** The design
  budgeted "about 56%" more figures. Measured on seed 42 against the same seed without households:
  **+17.7% at 300 years, +40.5% at 600, +56.8% at 1200.** The reason is that notables scale with
  *seats* while dynasts scale with *realms*, and a world is still founding towns for most of a run —
  so the shift lands as the map fills rather than at once. The doubling ratios say it is a shift and
  not a bend: 2.23 at 150→300, 2.57 at 300→600, 2.42 at 600→1200, rising while the households fill in
  and then falling back. `OfficesRaiseTheFigureCountWithoutBendingItsCurve` moved its ceiling from
  2.6 to 2.9 for that transient, and still fails on a household that compounds.

- **The dial the design reached for first was not needed.** "If the figure count runs hot the first
  lever is the balance between `Courtiers` and `Notable`." It ran hot and the lever stayed where it
  was, because `HeirTo` turned out to be the same lever from the other end: a seat filled by an heir
  continues a household instead of minting one. The raised share fell from 175 of 279 appointments to
  154 of 301 without anything being tuned.

- **Ossification was the wrong thing to fear.** The design worried that an office always passing to
  the last holder's child stops being a decision. The measured share of appointments that run in a
  family is **2.8%** (45 of 1,617 across five seeds) — a rarity rather than a gentry, and the ceiling
  never binds. The limit is heir supply, not the roll: a notable enters the record *at* their
  appointment, at 30–62, so they marry late and their children are usually still short of sixteen when
  the seat next falls vacant. Giving a raised notable a family on arrival — which the same argument
  supports that gave them a career, since a marshal of forty-five with no wife and no children is the
  placeholder problem in a different place — would make the third mode ordinary rather than rare. It
  is not built, because it spends the figure budget this milestone has already spent.

### Rulers who react: a people, a person, and a recent past

> **Built**, ahead of *Offices* above and against the order originally planned. `Centralism`
> moved here with it: `Disposition` is the record both milestones read, so landing it once
> avoided building the same field twice. What follows is the design as written; *What it cost*
> at the end records where the build disagreed with it.

Offices decide who holds what. This decides how the holder behaves, and it is the larger of the
two. Today every decision a realm makes is read off its culture, which is fixed at worldgen and
never changes: `culture.Values.Aggression` decides whether war is declared, whether a city is
sacked, how hard the levy squeezes and whether a king takes the field in person. So a warlike
people declares war at the same rate in its first century and its ninth, under thirty different
rulers, having won every war or lost every war. There is no reign in the chronicle that reads
unlike the reign before it except by accident of dice.

**Three layers, in order: who they are, who this one is, what just happened.**

```
Effective(civ) = culture.Values
                   .BlendToward(ruler.Disposition, Latitude(civ, ruler, year))
                   .ShiftedBy(civ.Fortunes)
```

#### The worked example, because it is the whole design

An aggressive people, a king who is not, and a war just lost. Culture aggression 0.78. The
king's own, 0.31. Latitude for a monarchy, mid-Tradition people, a king twenty years on the
throne and inclined to insist: 0.42. Blended: **0.58**. Ten years of weariness from two lost
battles and a sacked city: 0.6, which discounts aggression by a third of itself. Effective
aggression: **0.46**.

`DiplomacySystem`'s war roll is `Lerp(0.3, 1.6, aggression)`. At the culture's own value that
multiplier is 1.31; at the effective one it is 0.90. **The realm declares roughly a third fewer
wars while this king lives and the memory holds** — and climbs back when he dies and a warlike
nephew is crowned, or sooner if the weariness decays before he does. That is a chronicle beat
that cannot happen today at all.

#### One disposition record, and it is the culture's own shape

The ruler's dials are *the same dials the culture has* — Aggression, Expansionism, Piety,
Tradition, Mercantile — plus `Centralism` from the offices design and one addition below. This
is the decision the rest of the design hangs off, and the alternative is worse in a way worth
stating: a separate ruler-trait vocabulary (Bold, Cruel, Scholarly, Pious…) means every system
that wants to consult it needs a new branch mapping traits onto behaviour, and thirty such
branches is thirty places to disagree about what "bold" does.

Same shape means the blend is one function and **no system needs a new branch at all**. The
call sites change from `world.CultureOf(civ).Values` to `world.ValuesFor(civ)` and every one of
them becomes reign-aware in the same commit.

**`Learning` is the sixth dial, added to both culture and disposition**, and it is what "books
because they want knowledge" requires. `ArtifactSystem.Appetite` currently reads Tradition and
Piety, and `Choose` treats a tome as one of three things an ordinary town might make — so books
in this world are essentially a dice roll with no motive behind them. Learning gives commission
of tomes a patron, biases `Choose` toward tomes at a scholarly court, and raises the copying
rate in `Tomes.Distribute`. It is the one new axis; the other five already exist.

#### What a ruler may move, and what belongs to the people

There are 30-odd live reads of `culture.Values` in the engine, and **they must not all become
effective values**. The line to draw:

> A ruler moves **decisions**. A people carries **dispositions**.

| Takes the ruler's hand | Stays the culture's |
|---|---|
| `DiplomacySystem:430` declaring war | `PopulationSystem:183,186` how a people farms and trades |
| `Warfare:545` sacking a taken city | `ReligionSystem:126,150,262` conversion pull, resistance, schism |
| `Warfare:480` taking the field in person | `SettlementLifecycle:158` patience with a dying town |
| `Diplomacy:219` the levy fraction | `SuccessionLaw` — see below |
| `ExpansionSystem:56` founding a colony | `ReligionSystem:505` a new faith's fervour |
| `SettlementLifecycle:205` raising walls | `TradeRouteSystem:229` whether merchants use a road |
| `ArtifactSystem:91` commissioning things | `DiplomacySystem:236,246,247` relation drift |
| `ReligionSystem:353,359` founding holy sites | `FigureIncidentSystem` court violence |
| `DiplomacySystem:488,507` relic claims, holy war | |

**`SuccessionLaw` must stay cultural, and it is not a close call.** It derives from government
form with Tradition choosing among the monarchical variants. A ruler who could move Tradition
could change how their own successor is chosen, mid-reign, by having opinions — agnatic one
year and absolute the next. Constitutional change is a real thing to model one day and it is
not a side effect of a personality.

Relation *drift* stays cultural for a calibration reason rather than a philosophical one:
relations are slow accumulators read every year by every neighbour, and letting each new
coronation jolt every bilateral standing in reach would make alliances unreadable. The
*decisions* taken on the back of those relations are where the ruler shows up.

#### Latitude: how far one person can bend a people

```
latitude = Base(government)                        // chiefdom .45 → republic .20
         * Lerp(1.25, 0.65, culture.Tradition)     // a traditional people bends less
         * Lerp(0.50, 1.00, min(1, reign / 15))    // a new ruler has bent nothing yet
         * Lerp(0.75, 1.25, ruler.Centralism)      // and some insist harder than others
```
capped so no reign displaces more than about 60% of the distance to the culture's value. A
consul serving eight years in a traditional republic barely moves the realm; a chief of a
pragmatic people, thirty years in and centralising, moves it a long way. That is the same
`Centralism` the offices design introduced, which now has two consumers and clears the
read-by-nothing bar on its own.

**During a regency the regent's disposition applies, at reduced latitude** — which is what
makes a dowager regent's decade legible as her decade rather than as a gap between reigns.

#### Fortunes: the recent past, kept by the systems that caused it

Four decaying scalars on the civilization, in the spirit of `DeathCause` — written by whichever
system caused them, at the moment it happens, rather than inferred afterwards by walking the
chronicle:

| | Fed by | Meaning |
|---|---|---|
| `Weariness` | battles lost, own casualties, settlements sacked | the realm has been bled and knows it |
| `Calamity` | plague, disaster and famine deaths | the realm has been hurt by something it cannot fight |
| `Triumph` | battles won, territory taken | it is going well and everyone can feel it |
| `Grievance` | territory lost and not regained | the humiliation that outlives the exhaustion |

All four decay geometrically on a living-memory half-life of about twelve years, so a defeat
governs a decade and is a footnote in three. **Weariness and Grievance are deliberately
separate** and pull opposite ways: being beaten exhausts a realm and being humiliated angers
it, and a model with one scalar has to pick one, which is why "we lost, so we will never fight
again" and "we lost, so we will fight until we get it back" are both wrong on their own.

The shifts, applied after the blend:

- Weariness pulls Aggression down and Mercantile up — a spent realm trades.
- Grievance pulls Aggression up.
- Calamity pulls Expansionism down and **Piety up** — which is the first time a disaster or a
  plague in this engine has any consequence beyond the people it kills, and it is the correct
  one: catastrophe drives people to the temple.
- Triumph pulls Expansionism and Aggression up, mildly. Success is its own argument.

#### One answer per year

Effective values are computed once at the top of the year and stored on the civilization,
exactly as `StateReligionId` already is and for the same reason recorded there: every judgement
made within one year should be made against the same answer. A war declared in spring and a
colony founded in autumn are judged by the same king in the same mood.

The consequence to accept deliberately: a ruler crowned in autumn takes effect the following
spring, because succession runs late in the tick and the year's values were settled before it.
That is a year's lag on a thirteen-year average reign, and the alternative — recomputing
mid-year — reintroduces exactly the ordering hazard the annual sync exists to remove.

This wants a new first system in the order, `crown` or `disposition`, running before
`population`. It is cheap: a handful of lerps per realm per year.

#### The electorate votes for the ruler it wants

This is where the user-facing request lands most directly. The realm has a **wanted
disposition**, derived and stored nowhere:

```
wanted = culture.Values
           .ShiftedBy(civ.Fortunes)         // the same shifts, applied to what a people asks for
           .PiousBy(state faith's Fervour)  // a fervent establishment wants a devout ruler
           .CentralisedBy(Weariness + Calamity)   // crisis wants a strong hand
```

`SuccessionSystem.Elect` currently draws over `BallotWeights = {0.45, 0.27, 0.17, 0.11}` by
claim order alone. It becomes `BallotWeights[i] × Affinity(candidate.Disposition, wanted)`,
where affinity is one minus the mean absolute distance across the dials, mapped into a modest
multiplier — roughly 0.5× for a candidate the realm does not want to 1.6× for the one it does.

**Affinity must not overwhelm claim order, and this is the sharpest calibration risk in the
whole design.** Push the multiplier range too wide and an elective realm stops being dynastic:
the ballot becomes pure trait-matching, the fourth-placed claimant wins routinely, and the
entire M5 apparatus of houses and lines of descent stops mattering in exactly the governments
that were meant to showcase it. Claim strength must remain the dominant term with affinity as
the thumb on the scale.

**Monarchies get the same effect through the machinery that already exists.** A disputed
succession currently resolves on a flat `StrongerClaimPrevails = 0.7`; nudging that by the
rival's affinity means "the realm backed the brother who promised war" happens under
primogeniture too, without a second system and without a peasant ever casting a ballot in a
kingdom.

#### The loop this leaves open on purpose

Divergence — how far a reign's effective values sit from its culture's — is derived, stored
nowhere, and currently read by nothing. It is named here because it is what a revolt system
reads, and because the deferred half of the user's request has two halves, not one:

- **The people push back.** Sustained divergence, plus sustained Weariness and Grievance, is
  unrest. A ruler far from their people for long enough is deposed by them rather than by a
  rival claimant — which is a kind of `RulerDeposed` the engine can already record and has no
  way to produce.
- **Or the people come round.** A culture's values could drift slowly toward a long line of
  rulers who all pulled the same way: nine generations of warlike kings make a warlike people,
  and the culture that was fixed at worldgen stops being fixed.

Those two are the same feedback loop seen from either end, and building either one without the
other gives a world that only ever revolts or only ever converges. Neither is in scope here.
What is in scope is producing the divergence they both read.

#### Where the calibration will fight back

**Predictions.** Four, and the first is the one that decides whether this feature is worth
having:

1. **Reign-to-reign whiplash.** Every dial re-blends on every coronation, and with a
   thirteen-year mean reign a realm could change character six times a century. That is not
   history, it is noise. The controls are latitude's reign-length term — a new ruler starts
   near their culture and diverges as they hold on — and the cap. If a chronicle reads as a
   realm with multiple-personality disorder, that term is where to look first.
2. **Fortunes as a death spiral.** Weariness lowers aggression, which lowers levies, which
   loses battles, which raises weariness. Every feedback loop in this engine so far has needed
   its sign checked; this one is negative on the war side and wants a floor.
3. **Every realm converges on the mean.** Blending toward a rolled disposition whose mean is
   the culture's own value is, over many reigns, a random walk that averages out — so the
   world's realms could end up more alike than they started, which is the opposite of the
   intent. Dispositions should be rolled with real spread and low latitude, not small spread
   and high latitude, even though both produce the same average displacement.
4. **The elective-realm failure above**, which is the one that quietly destroys an existing
   feature rather than merely failing to add one.

#### The test contract

- Effective values stay in [0, 1] and never sit further from the culture's than the cap allows.
- The succession law of a realm never changes while its culture is unchanged.
- Fortunes decay to negligible within a stated span of quiet years, from any starting value.
- A realm with high Weariness declares measurably fewer wars than the same realm at rest —
  asserted against the seed's own baseline rather than an absolute rate.
- Across the standard seeds, at least one reign diverges materially from its culture, and the
  distribution of realm characters at 300 years is no tighter than at year one (the convergence
  failure above, made into an assertion).
- Elections still favour the strongest claim in the majority of cases.
- Determinism: `Fortunes` is state on `Civilization` and exported; dispositions come from a fork
  keyed on the figure's own id, so they cannot depend on birth order.

Export goes with the offices bump: `ExportFigure` gains its disposition, `ExportCivilization`
gains fortunes and the year's effective values. The viewer can then show a realm's dials as
three numbers rather than one — *the people 0.78, the king 0.31, and 0.46 today* — which is
probably the single most legible thing this design produces for a reader.

#### What it cost, as built

Landed in four steps, the first two of which changed no history at all. `Pcg32.Fork` derives a
substream from the parent's immutable seed and consumes nothing from it, so both new rolls —
`Learning` on the culture's own stream, a figure's disposition on their id — could be added with
**all 3,590 events of seed 42, and its settlements, civilizations and dynasties, byte-identical**.
That is worth stating because it inverts the usual order of work: the fork discipline that exists
to stop unrelated changes perturbing each other also let the whole foundation be built and
inspected before anything was allowed to move.

Only the third step moved the world, and it moved it less than expected: **3,590 events to 3,645**
on seed 42, no realm lost, nothing runaway. Measured across twelve seeds when the succession
weighting landed on top: realms standing 81 → 84, houses standing 69 → 67, figures up 3.8%. Seed
42 itself loses a realm it previously kept, which is inside that variance rather than a signal.

Three places the build disagreed with the design:

- **`Learning` had to redistribute artifact patronage rather than add to it.** Adding a sixth
  positive term to `ArtifactSystem.Appetite` raised every realm's output by about a tenth, and
  the tome-circulation calibration caught it: making more books late in a run lowers the share
  that have had time to circulate. Lowering the floor by half of Learning's mean contribution
  keeps world volume where it was, so the dial changes *who* commissions rather than *how much*
  gets commissioned. A dial that silently doubles as a volume knob is not the dial it claims.
- **The claim-dominance invariant is about the extremes, not adjacent places.** The first version
  asserted that no claimant could be lifted over the one ahead of them, and it failed immediately
  — correctly. Preference *must* be able to lift the second claimant over the first, or the model
  cannot change the outcome it exists to change. What must never invert is the far ends: the best
  claim in the realm, unwanted, still outweighs the remotest of the four who is everything the
  realm hoped for. The test asserts it over the constants, so it holds for candidates no seed has
  produced yet.
- **A seed-pinned test went stale, as that kind of test does.** `FlavourTests` checked that a
  faith can be forgotten on seed 11, which lost exactly one; the perturbation stopped it. Faith
  fading is undiminished — five of twelve seeds lose one, seed 8 loses nine of sixteen — so the
  check moved to seed 8. A seed chosen for having a single qualifying event is testing that one
  history has not changed, not that a mechanism works.

Of the four predicted calibration failures, none has yet appeared. That is not the same as their
being wrong: reign-to-reign whiplash and convergence to the mean are both properties of a long
run rather than of a 300-year one, and neither has been looked for over eight centuries.

### Diplomacy and war: reach, not borders

The M6 deliverable was relations, alliances, named battles, territory transfer and
sackings. All five were written and correct for a day before a single war was declared,
and the reason is the most useful thing this milestone produced.

**Contact was defined as a shared border, and that is far too strict for these worlds.**
Eight civilizations on a 4096-unit map hold ninety-odd regions out of a thousand. On
seed 42 the first two territories to actually touch did so in **year 201**, and half the
pairs that ever met did so after year 245 — so a three-century chronicle contained
perhaps eighty civilization-years in which a war was even possible, and rolled none. The
mechanism was not mis-tuned; nobody could reach anybody.

It is also wrong on its own terms. Two realms with a day's ride of empty forest between
them knew exactly who each other were and fought accordingly: empty land is something
armies march through, not a wall. **Contact is now the distance between the nearest
settlements of two realms**, with friction full out to a few regions and fading to
nothing at 1600 units — a little under the range a realm can already supply a colony
over. On the same seed, seven of eight realms are in contact from year one and the
eighth sits alone across the map with an empty diplomacy page for three centuries, which
is exactly the right chronicle for where it was founded.

Everything downstream follows from the same change. The *front* is the enemy's territory
ordered by distance from ours, so battles are fought and provinces are ceded at the near
edge of a realm whether or not the two share a border, and one ordering answers both
questions.

**Relations are pulled toward a level, not accumulated.** Each pair has a natural
standing set by the things that do not change year to year — a shared culture, how close
they stand, a marriage between the two houses, how much each lives by trade, whether
they fear the same third party — and the opinion drifts toward it at 6% a year. A war's
terms are a step change on top, which then fades. That is what makes a grudge a grudge:
felt sharply, spent within a generation, leaving the two realms back at whatever their
geography always said. The first attempt summed yearly deltas instead; any constant
pressure saturates that at ±1 within a few decades, after which every neighbour is
permanently at war with every neighbour.

**Relations are directed.** Each realm keeps its own view and the two rarely agree. The
peace terms are where it earns its keep: the loser's opinion of the winner falls twice as
far as the reverse, which is what sends a beaten realm back for its province a generation
later. Note what that does *not* claim — the loser is not the colder of the two
afterwards, because the winner is usually the more aggressive realm and so structurally
the colder both before and after. What is asymmetric is the movement, not the level. A
test asserting the level was written first and fails; the one asserting the movement is
in `WarTests`.

**Alliances needed a term that is about somebody else.** Every civilization is founded
with a culture of its own, so the kinship term never applies, and warmth from trade and
marriage alone never reached the threshold — a milestone listing alliances among its
deliverables swore none in three centuries. Two terms fixed it, and both are the reason
alliances exist at all rather than padding: realms warm to each other for each **common
enemy** they both regard as hostile, and warm further while **fighting the same war on
the same side**. A pact sworn because two realms fear the same third one is a pact that
means something when the third one declares, because that is precisely who it will be
called against. Defenders' allies answer at 80%, aggressors' at 30% — a pact to defend is
the one everybody signs, and a pact to join somebody else's invasion is the one that gets
ignored.

**Wars had to stop being an oscillator.** Initiative was first given to whichever side
was ahead, which reads well and is wrong: the side attacking is the side giving up the
defender's advantage, so it is the side likely to lose the next battle, and the score is
shoved back across zero every time it crosses. Four wars in six ran to the exhaustion cap
and one of them fought seventeen battles. The aggressor now presses until it is a clear
battle behind, and only then does the defender counter-attack — so a war has a direction,
and whoever is winning goes on winning.

**Wars end two ways, and only one of them was implemented.** A decisive war ends because
somebody won. An indecisive one ends because both sides have had enough, and with only a
hard cap standing in for that, every indecisive war lasted exactly the cap. An exhaustion
ramp of 3.5 points a year gives the spread instead: median war length is six years, a
fifteen-year war is remarkable, and the cap survives only as a backstop for two realms
that cannot reach each other at all.

**A battle is not a peace.** Winning a siege sacks a town; it does not take it. Borders
move at the treaty and nowhere else, which is both how it worked and what makes the peace
the event a war is remembered for. Fortification finally does something: walls are worth
half again a defender's strength, which turns Aggression — which drives wall-building —
into a defensive trait as well as an offensive one, and gives a small realm a way to
survive a large neighbour.

**Armies are 4% of a realm's people, and only 55–100% of that reaches any given field.**
The levy fraction is what makes a large realm beat a small one, and therefore the only
thing that makes conquest run in one direction rather than being a coin toss. The
commitment spread is what stops it being arithmetic: without it two realms of similar
size fight the same battle every year and the larger eventually wins every war by
inevitability, which is most of what makes a war not worth reading about. A war's score
moves by the *margin* of each battle rather than by who won it, so one decisive siege can
settle a war that a dozen skirmishes would not.

**Casualties are spread over settlements, capped per settlement.** A levy is drawn from
everywhere, but proportional arithmetic will happily empty a hamlet that contributed
forty men, so no settlement loses more than a fifth of itself to one battle. Without the
cap a lost battle abandons villages that were never near it.

**Three calibration faults, all the same shape as M4's:**

- **Realms declared war as hamlets.** Four percent of a population of two hundred is
  eight men, and the first five battles in the world were fought at that scale — one
  ending in the sack of a hamlet by an army of eight. A realm now needs two thousand
  people behind it, which a founding seventy reaches in about sixty years, so the opening
  decades read as what they are: realms establishing themselves.
- **Everything got sacked.** Sacking on every victory at any settlement made a sack more
  common than one per two battles, which is precisely backwards for the thing a war
  should be remembered for. A settlement must be worth taking, and a war may sack a given
  place once — a town besieged year after year was otherwise sacked year after year, each
  entry smaller than the last, which reads as a loop rather than as a war.
- **A place was renamed mid-war.** Naming sieges after the town and field battles after
  the region meant a town sacked out of its tier stopped qualifying as a siege, and the
  next engagement at the same walls appeared under the region's name — so the chronicle
  read as though the war had moved. Battles are named for the settlement when there is
  one, and the ordinal counts engagements at the *place* rather than under an exact
  wording.

**Wars and battles are named by composition, not by a naming language.** Nobody names a
war in advance; chronicles call it after the province it ruined or the succession it
settled, and both are already entities the reader can follow. Battle names are therefore
not globally unique, because settlement names are not — two towns eight centuries apart
can both be Puolijoki, and a battle at each is genuinely the Battle of Puolijoki. That is
the same trade M3 made deliberately, and real geography makes it constantly.

**A realm ends the same way whichever thing killed it.** `Realms.Fall` is shared between
the settlement lifecycle, which ends a realm whose last village emptied against a failing
climate, and the peace table, which ends one whose last town was ceded. Two copies would
eventually disagree about releasing a dead realm's territory or closing its ruler's
title, and the symptom would be a chronicle that quietly stops rather than an error
anybody can find.

**None of it costs a terrain sample.** Every question these two systems ask about the
land — how far apart two realms are, how defensible a province is, which frontier is
worth taking — is answered from region statistics derived once when the world was built.
`WarTests` pins it by running the same world with the war systems removed and comparing
sample counts, because the obvious implementation of "how defensible is this ground"
reaches straight for the sampler.

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
renders event kinds it has never heard of. This paid for itself in M6: seven new kinds —
declarations, battles, sackings, cessions, alliances — appeared in the timeline, in its
filter list and on every entity page with **no viewer change at all**, correctly narrated
and correctly cross-linked. The alternative is a per-kind switch kept in sync across a
language boundary, which it would not be. M8's plagues will land the same way.

The slots are used pragmatically rather than literally, and that is deliberate. A war
declaration puts the aggressor in `subject`, the defender in `object` and the *war* in
`location`, because three entities matter and there are three slots — and every one of
them becomes a cross-link and an entry in that entity's page index. `Extra` takes the
overflow: a war's whole coalition, a battle's commanders.

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
  the viewer wants its own ramp, with height/biome/rivers as composable layers. The
  height range ships alongside so metres are recoverable.
- **`schemaVersion`** is checked by the viewer, which refuses politely rather than
  misrendering a file it does not understand. **v5** records the particular relic and the
  two faiths behind religious causes of war; v3 added wars, battles, and the relations,
  alliances and truces on a civilization; v2 added dynasties and family links.
- Enums serialise as **strings** — numeric values would silently change meaning the
  first time someone inserted a value mid-enum.
- **Relations are a list of pairs, not an object keyed by id.** Everything else in the
  file is an array of records, and a map keyed by entity id would be the one thing the
  viewer had to iterate in an order it did not choose.

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

### World identity: a name for the history, unique to the seed

The chronicle used to open on an unnamed world. A list of exports was a list of filenames
and seed numbers, which is how you reproduce a history and not how you remember one.

The world is now named once from the seed, in the same world-level language as its
regions, and composed into a designation:

- a planet: "The planet Borion", or "The planet Borion of the Vathri system" when a
  second draw gives the world a system of its own
- a moon: "Ithil, the 3rd moon of Endor"

Two proper nouns is what keeps two seeds from sharing a label: a single Markov place
name repeats, and "The planet Vratislavl" was the first collision the uniqueness test
caught. The short form "the 3rd moon of Endor" was tempting and is exactly the shape
the feature asked for, but parent-plus-ordinal collides the same way; the moon's own
name is what makes that phrase a unique history rather than a class of them.

That roll is flavour. It does not feed terrain, founding, or any later system. The
stream is forked from the seed under `world.flavour`, never from `WorldState.Root`, so
adding it cannot shift a civilization's traits or a battle's outcome. The proper nouns
come from `INameGenerator.ForWorld`, which depends on the seed and a role (`Body` /
`Parent`) the same way a region name depends on its id — stretching a run, or asking for
more civilizations, cannot rename the world.

The designation is unique to the seed across the sampled range; collisions would make two
histories share a label, which is the failure the test is watching for. The seed still
travels in `ExportMeta` and in the overview, because that is what you type to get the
same history again. Schema 20 carries the identity at the front of `ExportWorld` so a
catalog that only reads the file header — cutting before the raster — still knows what
to call the world.

Rejected: putting the seed inside the designation ("Borion-42"). That would make every
name unique by construction and would also make the flavour a serial number. The seed
already sits next to the name wherever the name is shown.

Rejected: drawing the name from `WorldState.Root`. Cheaper, and it would have renamed
every founding the moment this shipped.

`WorldFlavourTests` pins seed-stability, uniqueness across 256 seeds, that both planet
and moon forms arise, and that the `WorldCreated` event and the export agree with the
world in memory.

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

### Running a seed from the viewer, in development only

The loop the viewer was missing is the short one — pick a seed, look at it, pick another —
and it needs a process spawn, which a static bundle with no server behind it cannot do.

An Astro API route is the obvious shape and was rejected: a non-prerendered route makes the
build want an adapter, which is precisely the coupling the static shell exists to avoid.
An **Astro integration** (`viewer/dev/world-generator.mjs`) instead injects `/new` and a
Vite dev middleware only for `astro dev`. The page lives outside `src/pages/`, with a React
island for the form and static Astro around it, so neither route nor island enters the
production graph. The feature is not hidden in a production build, it is **absent** from
it, and the built viewer still opens straight off disk.

The endpoint takes three numbers — seed, years, civilizations — bounded, and handed to
`spawn` with no shell between them. `--size` and `--raster` are deliberately not exposed
and stay at the CLI's defaults, so a world generated in the page is the same file the same
seed gives through `make generate`. Runs are serialised, because concurrent `dotnet run`
invocations contend over one `obj/`; a poll every 600ms carries the CLI's own summary back,
which is what actually answers "was that seed worth looking at"; and cancelling signals the
process group rather than the launcher, which is the difference between stopping a
simulation and orphaning it.

The generator does not become a second viewer. When a run finishes, `/new` navigates to
the ordinary viewer with the export in `?world=`; parsing and indexing the chronicle stays
the viewer's job, and the generator island does not load megabytes just before leaving.

Serving the result took one more piece. Vite lists `public/` once at startup, so a world
written after that is a 404 — which was already true of `make generate OUT=…` into a
running dev server, and had always been fixed by restarting it. The same middleware serves
`public/worlds/` per request, so both routes work now.

### Territory over time: replayed, not snapshotted

The export ships **final** state — a region's last owner, a settlement's last tier. A map
that answers "what did this world look like in 187?" needs every year, and there were two
ways to have them.

Shipping a snapshot per year was rejected on arithmetic: a thousand regions times eight
hundred years is most of a megabyte of ownership alone, on a file that is already 4.5 MB at
that length, to carry a value that changes perhaps thirty times per region. The viewer
instead **replays the chronicle** and stores one entry per actual change.

That trade only works if the log is complete, and it was not. Three transfers of land were
happening silently, because none of them went through the expansion system that records
claims: the homeland a realm takes at its founding, the provinces a fallen realm releases,
and the region given up when its last settlement is abandoned. Each was a border move with
no event behind it, so a replay drifted from the exported map — a dead realm's colour still
sitting on land a neighbour had since taken.

The fix is three events rather than a viewer that special-cases them, because the gap was
in the record and not in the reader. `RegionReleased` joins `RegionClaimed` and
`RegionCeded` in the territory block, and the founding claim is now recorded like any
other. It costs +9 events on the standard seed and changes no simulation behaviour: the
same world, more completely written down. Fjordvik's page now names who held it before
Heraanes took it, which is exactly the sort of thing the chronicle should always have said.

**`TerritoryTests` is the contract.** It asserts across three seeds that replaying the log
reproduces the exported map exactly, that every realm claims its homeland in its founding
year, and that no land is ever attributed to a realm that has already ended. The property
belongs to the engine, so the test lives there — a Milestone 8 system that moves a border
without recording it fails in the engine's own suite rather than by drawing a plausible
and wrong map three layers away. The viewer additionally checks the replay against the
final map on load and warns, which covers a world file written by a newer engine than the
viewer reading it.

### Time: seasons on the year, and days where they are earned

> **Built** as M13, staged so that every part of it that could land without moving a history did
> so first. The first three stages were inert and each was verified as inert before cadence or
> scheduled work was allowed to change a world:
>
> 1. **The clock.** `Stamp`, `Calendar`, `Docket` and a `Cadence` on every system. Nothing used
>    any of it; the fingerprint proved so.
> 2. **The record learns a day.** `HistoryEvent` gained `Day` and kept `Year`, and the chronicle
>    is told which step is open so that several hundred recording calls did not have to name a
>    day none of them has. Every system is still `Annual`, so every day in every world is zero.
>    The export moved to schema 17 and the fingerprint with it; the history did not.
> 3. **Step ordering.** A step's events are sorted into `(day, system index, sequence)` order
>    when it closes, so system order and the calendar can disagree within a step and be
>    reconciled by it. The seed-42 fingerprint is byte-identical across that change.
>
> 4. **War left the year.** The tick loop steps the year in seasons; `war` is `Seasonal` and
>    every other system is still `Annual`, running in the opening season only. Campaigns are
>    seasonal, peace is annual and settled in the closing season, and the campaigning season is
>    read from the defender's ground rather than from a world clock. This is the first stage that
>    moved a history, and it moved one system's worth of it: across five seeds, wars 71 → 70 and
>    battles 290 → 240, the missing sixth being northern winters. No capital sits on ground shut
>    all year; the 12% of land that is, is arctic.
>
> 5. **The docket can wake a system.** `IEpisodic` declares which `DocketKind` a system answers
>    for; the simulator drains due work in the docket's own order and routes each entry to its
>    owner. Inert again — nothing schedules anything yet, so the fingerprint did not move.
>
> **Why the simulator dispatches rather than each system draining for itself**, since this was
> the one architectural decision the milestone had left: `Docket.TryTakeDue` hands back whatever
> is due, of *any* kind, so a system draining for its own work would take everybody else's out of
> the queue on the way past. Filtering by kind at the call site fixes that and puts the same total
> order at the mercy of which system happened to ask first. One drainer, dispatching by a declared
> owner, is the only arrangement in which the queue's order remains a property of the queue.
> Ownership is declared by the interface rather than by cadence, because a system may legitimately
> do both — a plague igniting once a year and stepping its outbreaks on their own schedule is
> exactly that shape — so `Cadence.Episodic` now means only that the clock never ticks it.
>
> 6. **The plague travels.** `DocketKind.Arrival`'s first consumer, and the first model that
>    needed a day rather than merely being able to carry one. Infection now takes days to reach
>    the next town — six plus one per twenty-two units — so a jump along a trade route lands
>    seasons after it left, where a year made every jump simultaneous however far it went.
>
> It was chosen over a siege or a settling party because it is the only one of the three that is
> *additive*: the same arrivals happen and no rate is converted, they are merely displaced in
> time. One consequence still had to be answered. Reach per outbreak left its 2–5 budget for 5.46,
> because several carriers now set out before any of them lands and each reads a `Reached` the
> others have already made stale — so the quarantine damping arrived too late. Counting what the
> plague is travelling *toward* alongside what it has reached restores the budget, and is the
> truer model: a town closes its gates on the news, not on the disease.
>
> 7. **Every plague keeps its own clock.** `OutbreakStep`'s consumer. An outbreak steps on its own
>    docket entry at its own interval — thirty days for the most virulent, a hundred and twenty for
>    the mildest — and every per-year rate the step applies is scaled by that interval against the
>    calendar, so the change buys granularity and never the annual total. The annual tick keeps
>    only ignition, which is a question about the world rather than about any running epidemic.
>
> This is the row that "what a year cannot say" opened with, and the measurement is the shape
> rather than the budget: on seed 42 the Wasting Fever begins on 157.0 and is over by 157.89 — an
> epidemic that arrives, peaks and burns out inside one year — while the Summer Contagion still
> grinds through seven. Durations are continuous instead of quantised to whole years, and the
> plague budgets held without a constant being touched.
>
> **Two ordering bugs came out of it, both found by tests rather than by reasoning.**
> `Calendar.Plus` now carries the year: adding to `Stamp.Day` overflowed once steps chained off
> each other, and `AbsoluteDay`'s deliberate tolerance for a day past the end of its year — which
> the docket needs in order to sort — is not a licence for the *record* to claim year three for
> something that happened in year four. And the step sort compares the whole stamp rather than the
> day, because scheduled work means a step can now hold two years at once.
>
> 8. **Expansion left the year, on the second attempt.** A settling party sets out for ground it
>    can arrive on, read from the region being settled rather than the realm settling it.
>
> **The season decides when, not how often, and that is the difference from war.** A war runs
> continuously and a closed season genuinely removes chances to fight, so the north fights less
> over a year — battles fell a sixth and that was the point. Colonising is a decision of state
> taken when pressure builds, and pressure does not evaporate over the winter: the settlers wait
> for spring. So a frontier open two seasons in four gets half as many chances at twice the odds,
> the yearly weight is preserved, and only the timing moves.
>
> The first attempt was reverted, and what it got wrong was not the rate. Gating on the season
> alone stalled any realm whose best frontier was frozen the year round — it waited for a spring
> that never came. Seed 7 lost 22% of its colonies and the population that should have gone to them
> concentrated into cities until a third of its settlements were one; compensating the rate fixed
> that and then failed the same test from the other side on long runs. Ground with no open season
> has no better season to wait for, so it is now settled whenever it is settled, and the long-run
> size distribution holds without a constant being tuned to make it.
>
> **`crown` was listed for a season and should not have been.** It fades a realm's memory by a year
> and settles the values that memory produces — a slow accumulator, which is the case the
> three-tier argument explicitly reserves for the year. Dividing a yearly fade by four gives the
> same answer through four times the arithmetic and informs no decision a season could. The spring
> in "a crown made in the reign of a ruler crowned this spring" belongs to `artifacts` running
> after the houses, and is already true of the ordering.
>
> 9. **A siege lasts.** `SiegeResolves`, the last declared docket kind, now has its consumer. A
>    seasonal campaign at a walled or town-sized settlement begins an investment, commits its two
>    forces and commanders, and schedules the decision forty-five to one hundred and fifty days
>    out. The war spends no second campaign elsewhere while that army is invested: a later campaign
>    is the relief force coming to meet it. A closed campaigning season or a peace can lift it first,
>    and stale docket entries then do nothing rather than resolving the same siege twice.
>
> This is the stage that needed state rather than only a re-phasing. A battle now keeps its opening
> and ending stamps and one explicit siege outcome — ongoing, carried, relieved, or lifted — and
> schema 18 carries those facts to the viewer. The event log keeps both ends: `SiegeBegan`, then
> either the deciding `BattleFought` or `SiegeLifted`; it never edits the beginning after learning
> the end.
>
> Across seeds 2, 7, 11, 42 and 99, 73 wars produced 114 immediate field battles and 143 siege
> episodes. Of the sieges, 53 were carried, 76 relieved, and 14 lifted without a deciding battle —
> ten by the season and four by the peace. Their median span was 90 days and the longest 150. The
> important calibration is what did *not* inflate: 243 engagements were actually fought, against
> the 240 measured when war first became seasonal. The extra fourteen records are investments that
> ended without pretending a pitched battle occurred.

The year is the atom, and it is the last load-bearing choice in this engine that was never
argued for. `IYearSystem.Tick(world, year)` is the only entry point a system has,
`Chronicle.Record` takes a year, `HarvestModel.QualityAt` takes a year, and the RNG convention
is one fork per system per year. Everything that happens *within* a year is expressed as
**system order** — a single total order over seventeen systems, fixed for the whole run, and
identical in every realm at every latitude.

That was enough for twelve milestones. It is now the thing standing between four models and
the behaviour they are already described as having. `Simulator.DefaultSystems` reaches three
times for a calendar it does not have — "a war declared this spring is fought this summer", "a
province taken in a spring campaign", "a crown made in the reign of a ruler crowned this
spring". None of those springs exists. They are the doc-comment describing what the ordering
*would* mean if the year had parts, which is the clearest possible signal that the model wants
them.

#### What a year cannot say

| | Today | What the year forbids |
|---|---|---|
| **War** | one campaign roll per war per year, `WarSystem.cs:91` | a siege cannot last, be lifted, or be relieved; there is no closed season, so a war in the far north is fought exactly like a war on the equator |
| **Plague** | one spread step per outbreak per year, `PlagueSystem.cs:112` | an epidemic that arrives, peaks and burns out inside eight months has one tick to do it in, so the shape of an outbreak is a calibration constant rather than a consequence |
| **Harvest** | one quality per region per year, `HarvestModel.cs:80` | a campaign fought across a region cannot cost it the harvest, because the harvest has no date to be interrupted before |
| **The record** | events carry a year and nothing finer | two events in one year have no order but the order the systems ran in, and a ruler crowned in autumn governs from the following spring — recorded, and accepted, under *One answer per year* |

The fourth is also the Phase 3 seam. Vintage Story keeps a calendar in months and days, and a
mod that hands a player a history will be asked what date something happened on.

#### Rejected first, because it is the obvious answer

**A uniform finer tick — every system ticked every day, or every month.** At 360 days a
300-year run is 108,000 ticks against today's 300. The M9 table puts a run at ~253 ms, so the
naive daily loop is somewhere around a minute and a half per world, and it lands on the whole
suite at once, much of which runs full worlds. Monthly is 12× and still buys a suite that takes
twelve times as long.

The arithmetic is the smaller half of the objection. The larger half is that **most systems
have nothing to say on most days**. Population growth of 3.8% a year divided across 360 days is
arithmetic noise with a random number attached; a diplomatic relation that drifts 6% a year does
not want 360 dice where it currently has one. A uniform clock pays its full cost on every system
in order to give a finer answer to the four that asked.

#### Three tiers, and the same argument `TerrainAtlas` makes

The terrain design's organising idea is that resolution is bought where a decision needs it and
nowhere else: a coarse lattice for the majority of queries, a bounded refinement per decision, an
exact sample only for coordinates that become permanent. **Time gets the same treatment.**

1. **The year** stays the spine — the unit of the harvest, of growth, of a culture, of a
   figure's age, of every slow accumulator. Most systems never see anything else.
2. **The season** is the standing sub-year cadence, four to the year. It is what a system ticks
   on when its subject has a rhythm: campaigning, sowing, the closing of a mountain pass.
3. **The day** is never looped over. It is reached two ways only — as a **stamp** on something
   that happened, and as a **due date** on something scheduled. A siege that will resolve in
   forty days costs one queue entry, not forty ticks.

The consequence to state plainly, because it is the whole reason the cost is affordable:
**nothing in this design iterates days.** Cost scales with the number of episodes in play rather
than with the length of the run — the same property `TerrainDisciplineTests` already asserts for
terrain sampling, and it should be asserted here for the same reason.

#### The calendar

```csharp
public sealed record Calendar(int DaysPerYear = 360, int SeasonsPerYear = 4);
```

Twelve months of thirty, so a season is ninety days and both divisions are exact. It is
**config, and therefore hashed** — the calendar changes how many steps a year has and how far a
party travels in one — so `WorldConfig.HashedFieldCount` goes 21 → 23 and `ConfigHashTests`
covers it.

> **Phase 3 note.** Vintage Story configures its year as months × days-per-month, and its
> default year is materially shorter than 360 days. Confirm the figure against the version
> actually targeted and set `DaysPerYear` to it; the point of the field is that matching the
> game's calendar is configuration rather than a change to the model.

**A season is local, not global.** This world already has latitude-driven temperature and a
region knows its own climate, so "the campaigning season" is a property of the ground being
fought over rather than of the world clock. Winter in the north is high summer in the south, and
a realm on the equator has no closed season at all — which is a real asymmetry the model is
about to acquire, and it is listed below among the things that will need calibrating rather than
hidden here as flavour.

#### `Stamp`: a year that kept a day

```csharp
public readonly record struct Stamp(int Year, int Day) : IComparable<Stamp>;
```

`HistoryEvent` gains `Day` and **keeps `Year`**. That is the migration decision, and it is the
one schema 9 already made when `ExportTitle` gained four fields and kept `civilizationId`: every
existing read survives. `eventsByYear`, the viewer's timeline slider, the year filters and the
territory replay all continue to work untouched, and `day` is additive detail they can adopt
when there is a reason to.

**The chronicle keeps reading in order, and this is the sharpest structural consequence in the
design.** Events are appended in non-decreasing year order today — asserted by
`ExportTests.cs:59`, relied on by the timeline and the year index, and named in `Houses.cs:28`
as the reason back-dating a birth is not available. Once events carry days, system order and the
calendar can disagree: `succession` runs after `war` in the system list, so a king who died on
day 40 would be recorded after a battle fought on day 200.

Two rules keep the invariant, and both are cheap:

- **A system may only stamp inside the step it is running in.** A seasonal system ticking the
  summer step stamps days within that summer. It is a discipline, and it is checkable.
- **A step's events are put into stamp order when it closes**, sorted on
  `(day, system index, sequence)` — a total order, so it is deterministic. One sort of a short
  list per step.

  *Built as a sort in place rather than the buffer-and-flush this originally said, and the
  difference matters.* `Tomes.Annals` reads the log **during** a step to write a settlement's
  annals, so holding a step's events back until it ended would hide the year's own entries from
  the tome being written in it — a change of history rather than of plumbing, and one that would
  have arrived disguised as a refactor. Appending as before and reordering afterwards leaves
  every mid-step read seeing exactly what it sees today, which is what let the change land
  against a byte-identical fingerprint.

Nothing in the engine stores an event id on an entity — a tome's passages carry entity ids, not
event ids — so reordering within a step is safe, and ids go on encoding position in the log.

#### The docket: scheduled work, in stamp order

```csharp
world.Docket.Schedule(due: stamp, kind: DocketKind.SiegeResolves, subject: battleId);
```

A sorted list with binary-search insert, in the shape of `DetMap` and for the same reason:
enumeration order has to be a property of the keys and not of insertion history. The total order
is `(absolute day, kind, subject index, sequence)` — all integers, no floating point anywhere in
the ordering. It lives on `WorldState`, which is what makes it survive the `Advance`-versus-`Run`
split test, the strongest determinism test the suite has and one that gets stronger here because
a run can now be split mid-year.

The docket is **not exported**, on the argument `Outbreaks` already carries: it is state a system
holds between steps rather than something history refers to afterwards. What survives it is the
events it wrote.

It is also, incidentally, the escape hatch `IYearSystem.cs:22` already names — "buffer intents
and apply them in a separate phase" — arriving for a different reason than the one it was written
down against.

#### RNG: one rule per kind of work

The convention today is `world.Root.Fork(Name, year)`. It becomes three, and the split is what
preserves the fork discipline's central property — that adding a die roll to one system cannot
perturb another:

| Work | Forks on | Why |
|---|---|---|
| an annual system | the year, exactly as today | a system that did not change cadence produces the identical stream |
| a seasonal system | the absolute step index | monotone, unique, and independent of how many steps any other system took |
| a scheduled episode | its own subject's id, `Fork(kind, id.ToDiscriminator())` | a siege's dice must not depend on how many other sieges were scheduled before it — the rule M12 used for dispositions, for the same reason |

The third is easy to get wrong and expensive to find. An episode forked on its due date, or on a
queue position, makes every outcome depend on unrelated scheduling — precisely the failure the
fork convention exists to prevent.

#### Cadence is part of the run's identity

`SystemOrderHash` folds in the system list because reordering two systems changes the history as
much as changing the seed. **Changing a system's cadence does exactly the same thing**, so the
hash folds in each system's cadence alongside its name. `IYearSystem` becomes `ISystem` with a
declared `Cadence` and `Tick(WorldState, Stamp)`; `WorldState.Year` stays as a property derived
from `WorldState.Now`, so the great majority of call sites do not move at all.

Most systems stay annual. That is the design working rather than a shortcut:

| Cadence | Systems | Note |
|---|---|---|
| **Annual** | `population`, `settlement-lifecycle`, `specialization`, `religion`, `diplomacy`, `figure-incidents`, `figure-lifecycle`, `houses`, `offices`, `artifacts`, `disaster` | growth is realised at the harvest step; the rest keep today's rhythm and gain only a stamp |
| **Seasonal** | `crown`, `war`, `expansion` | values resettle each step, campaigns run in the season the front allows, parties leave in spring |
| **Episodic** | `plague` advance, `succession`, sieges, arrivals | woken by the docket, so an outbreak that is not running costs nothing |
| **Unchanged** | `trade-routes` | already every five years; seasonal corridor closure is a term inside it, not a cadence |

`diplomacy` staying annual is deliberate, and it is M12's argument reused: relations are slow
accumulators read by every neighbour, and jolting them four times a year makes alliances
unreadable. The *decisions* taken on the back of a relation become seasonal; the relation does
not.

`succession` becoming episodic is the quiet repair in that table. The system order's tightest
coupling exists because "deaths must precede succession or a realm spends a year without a ruler
for no reason the chronicle can explain" — a death that schedules its own succession states that
requirement directly instead of encoding it as an adjacency in a list.

#### What each of the four gets

**War acquires a season and a siege.** The campaign roll moves from once a year to once per
campaigning step, with the chance *converted* rather than repeated — `1-(1-p)^(1/n)` — because
the first thing a seasonal war system will do if left alone is quadruple every war in the world.
The bar is stated in advance and it is the M6 measurement: **15.5 wars, 59 battles, 11 sackings
and 10 provinces ceded per 300-year world across eight seeds, median war six years.** A seasonal
model that moves those has mis-converted its rates, whatever else it got right.

What is genuinely new is the siege. A siege becomes an episode with a start stamp and a
resolution scheduled weeks out, which can be **lifted by the season turning or by a relief force
arriving first** — the first mechanic in this engine that a year cannot express at all, rather
than one it expresses coarsely. Winter quarters then give indecisive wars a shape the flat
3.5-a-year exhaustion ramp cannot: a war can be *stalled* rather than merely slow.

**Plague gets its own timescale, and travel becomes time.** An outbreak steps roughly
fortnightly while it is running and disappears from the cost model when it is not — perhaps
twenty entries over eight months, against 360 ticks. M8's bounding term, the one line that
separated a regional catastrophe from a world-ending one, was that people react: a plague two
towns away closes gates. With travel time modelled, that term stops being a fudge and becomes
what it describes, because **news has a speed** and the gates close after it arrives.

That needs one new constant, and it is the most consequential number in the milestone:

```
UnitsPerDay = 64      // a 4096-unit world is ~64 days across
```

A neighbour at the 1600-unit contact horizon is then about twenty-five days away, a region is two
days wide, and news crosses a continent in a season. Everything that already moves gets a
duration for free: settler parties arrive, looted relics arrive, tome copies arrive. It is
**hashed**, and it should be measured against the settlement equilibrium before it is believed —
see the predictions.

**The harvest acquires a date, and with it a causal loop.** `QualityAt(region, year)` stays a
*year's* number and stays stateless: its noise fields run on nine-year and seventy-year periods,
and sampling them per day would be inventing detail the model does not have. What changes is that
the year's quality is **realised at the harvest step**, discounted by what happened to the region
during sowing and growing — a campaign fought across it, a siege sat on it, a sacking in it. One
multiplier accumulated per region per year.

This is the first time in this engine that a war can cause a famine. Note what it does *not*
change: growth stays 3.8% a year applied once, because the whole settlement lifecycle was
calibrated around settlements reaching capacity in ~110 years and then living at the mercy of the
harvest, and dividing that rate across four steps is the fastest available way to lose the regime
that made decline reachable at all.

**The record gets dates, and one recorded compromise expires.** *One answer per year* accepted
that "a ruler crowned in autumn takes effect the following spring", with the alternative —
recomputing mid-year — rejected for reintroducing the ordering hazard the annual sync exists to
remove. Under a seasonal `crown` system the answer is resettled at each step boundary, so the lag
falls from a year to a season **without anything being recomputed mid-step**: the invariant that
every judgement inside a step is made against the same ruler in the same mood is preserved
exactly as written. It is the cheapest win in the milestone, and it comes from the clock rather
than from new code in `CrownSystem`.

Disasters get seasons almost free, since every one of them is already drawn from the ground it
struck: storms in autumn, wildfire in a dry summer, floods at the melt. No new terrain data, one
new term.

#### The sample budget does not move

Every question in this design is asked of region statistics, the harvest field and the hydrology
grid — all derived once at world creation. **A four-times-finer clock must not cost one
additional terrain sample**, and the existing budget test is where that is caught. It matters
more after M10 than it would have before: that milestone spent the headroom, taking a run from
5,798 samples to 8,969 against a ceiling of 12,000, so there is no longer room for a time
milestone to be careless and be caught later. `TerrainDisciplineTests` asserts today that
sampling scales with decisions rather than years; the assertion extends to steps, and if it fails
the design is wrong rather than the budget.

#### Staging, because M12 showed what it is worth

M12 landed in four steps, the first two of which changed no history at all, and recorded that
this inverted the usual order of work: the foundation could be built and inspected before
anything was allowed to move. The same is available here and is worth more, because this
milestone touches every system in the engine.

1. **The clock, at one step per year.** `Stamp`, `Calendar`, `Docket`, `ISystem` with cadences —
   every cadence annual, every stamp day zero, every fork still on the year. **Byte-identical
   histories.** The golden does not move, and the whole mechanical change is reviewable against a
   fingerprint proving it changed nothing.
2. **Dates in the export.** Schema 13: `day` on events. The simulation is still unmoved; the
   export gains a field, so the fingerprint moves once, deliberately, for a reason written down.
3. **Seasons on.** `crown`, `war` and `expansion` re-phased, rates converted. Every history
   changes, and the calibration work lands here.
4. **Days.** The docket's first three consumers: sieges, outbreak steps, arrivals.

**Sequence this after M10.** Both regenerate the seed-42 fingerprint, and two milestones' worth
of change under one golden regeneration is exactly the situation that test exists to prevent —
nobody can tell by looking which move was which.

#### Where the calibration will fight back

**Predictions, not measurements.** Six, in the order they seem likely to bite:

1. **Everything happens four times as often.** Any per-year Bernoulli re-rolled per season
   quadruples its outcome unless converted, and the dangerous cases are the ones where the fix is
   not a conversion at all: an exhaustion ramp measured in points per year, a decline counter
   measured in consecutive years, a truce measured in years. Grep for the rates, not only for the
   rolls.
2. **The chronicle inflates without gaining anything.** M11's governorships are the precedent —
   the largest contributor to event volume was also the least informative event in it. A seasonal
   system that records must not record four times a year. **Estimated** cost of the genuinely new
   events, sieges and arrivals, is +150 to +300 on a run that currently writes 4,162 — so +4% to
   +7% — and the lever if it lands higher is recording an arrival only where the arrival is the
   point.
3. **Equatorial realms acquire a structural advantage.** No closed season means more campaigning
   steps a year, permanently, for every realm near the equator. This is a *new* asymmetry the
   seasonal model introduces and the annual one could not have; it wants a floor on the
   campaigning window rather than a literal reading of the climate.
4. **A north–south war has no shared season.** Two realms whose windows do not overlap can be at
   war for a decade without a battle. The front's own season should govern rather than either
   homeland's — which is also the truthful answer, since the front is where the armies are.
5. **Travel time collapses the colonisation range.** Settled land currently reaches 52–55% of
   land regions at `BaseChance = 0.10`. Making a distant founding take a season may quietly move
   that, and the failure mode is a world that stops expanding rather than an error anybody sees.
   `UnitsPerDay` is the dial, and that equilibrium is what it should be measured against.
6. **The suite gets slower and nobody notices until it is annoying.** **Estimated** at 1.5× to
   2.5× today's wall clock — roughly 5,100 system-ticks per 300-year run becoming roughly 7,200
   plus the docket. The bar: **under 3×**, and if a stage exceeds it, the cadence table is where
   to look before the code is.

#### The test contract

- The split-run determinism test extends to splitting **mid-year**: advancing by seasons, and by
  days, must produce the identical history to one `Run`. It is the strongest test in the suite
  and this milestone makes it stronger.
- Every event's stamp lies inside the step that wrote it, and the chronicle is non-decreasing in
  `(year, day)` — the existing `ExportTests` assertion, strengthened.
- Stage 1 reproduces the pre-milestone golden exactly. A temporary test by design: it is deleted
  when stage 3 lands, having done its job.
- Terrain samples do not rise, asserted against the same budget by running one seed at four steps
  per year and at one.
- War, plague and famine volumes stay inside the M6 and M8 envelopes quoted above, asserted
  against the seeds' own baselines rather than against absolute rates.
- The docket is covered by `DeterminismGuardTests` — no `Dictionary`, no floating-point ordering
  key — and an episode's outcome is unchanged by scheduling an unrelated episode before it.
- `Calendar` and `UnitsPerDay` reach `ConfigHash`; `HashedFieldCount` moves 21 → 23.

#### What this deliberately does not do

No daily loop, in any form, for any system: the moment one exists, the cost model is the rejected
design above wearing a different name. No hourly time and nothing below a day. No per-day
weather — the harvest keeps its two noise fields and its year. No positional armies marching
along paths; an army remains a levy attached to a war, and roads stay deferred where the
trade-route design left them. And no real-time coupling to a running Vintage Story server:
aligning the calendar is Phase 3's requirement, while simulating history live against a game
clock is a different project that this one should not pre-empt.

---

### M15: grievance that finally bites

The fortunes model measured a settlement's hardship densely — a sack, a lost war, a foreign
garrison, a famine each raised the grievance, weariness and calamity a realm carries — and until
M15 nothing downstream read a *settlement's* own copy of it. A place could be stormed, occupied
and starved and go on paying its taxes as if content. `UnrestSystem` is the consumer that pressure
was missing.

**Per-settlement fortunes first.** M12 gave realms the four-measure fortunes; M15 gives every
settlement its own, written by the same systems at the moment they happen — a sack, a plague, a
siege, an occupation — and faded once a year with the realm's. They shift no one's values; they are
read, sampled and exported so a town's page shows its own years rather than only its owner's. This
is the substrate the rest of the milestone reads.

**Two speeds, then two political endings.** Most discontent never becomes a rising: it festers as
`Settlement.Banditry`, a standing tax on the trade through a place that dims on its own once its
cause fades, and is written to the chronicle only when it erupts from a quiet country into a lawless
one — not each year it lingers. Only real pressure boils over into a revolt, which resolves the year
it breaks out rather than looping: a crushed rising vents the grievance that fed it, so the same town
does not rise every spring.

A rising that wins is not one outcome but several, and they are kept apart on purpose, because mixing
them in one roll produced chronicles that could not tell which had just happened:

- against a **garrison**, the town is freed back to its owner — the one relief an occupation can end
  in that no treaty wrote;
- against its own realm, the province **defects** to a rival in reach, or, with none close enough,
  **breaks away** as a realm of its own;
- a **capital** that throws off its own crown is a deposition, not a secession — the seat cannot
  break away from itself;
- and a mandated **governor** may instead march on the seat and **usurp** the throne, taking it or
  settling for independence when the march never quite arrives.

Wanting the throne and wanting out of the realm are opposite war aims, so `RevoltUsurped` and
`RevoltSeceded` are separate endings of the same pressure rather than one rising with interchangeable
outcomes.

**Placement is causal.** `unrest` runs after `war` so a campaign's grievance is felt the year it is
earned, and before `trade-routes` so the brigandage a rising raises suppresses that year's traffic.
Adding it is a behaviour change and moves the fingerprint, which is the point of it.

**Numbers still to sweep.** The pressure weights, the revolt threshold and scale, the muster
fractions, the defect and march distances, and the usurpation gate were set by argument, not by a
sweep. Each is a candidate for tuning against a seed panel — how many risings a century, what share
of them a crown can actually put down, and how often a world ends a run holding a realm born of
secession — without redesigning anything.

### M16: the lives the chronicle follows

M5 breeds a court full of people and M11 found work for one of them. M16 gives the rest a life on the
page without adding anyone to the figure table.

**An occupation, chosen once.** Everyone who reaches majority has an `Occupation` — soldiery, clergy,
townsfolk, a craft, trade, the court, office, or letters. Raised notables arrive with the career their
office implies; children of a recorded household choose from their disposition the year they come of
age, weighted by a blend of their people's values and their own and pulled — not compelled — toward a
parent's trade. The vocabulary lines up with the offices a court fills, so a marshal is raised from
soldiery and a governor from the standing of a town, and the appointment model reads a career without
a second mapping every consumer would have to keep honest. An office is a posting, not a new birth:
taking one puts a figure in the career the seat is, and laying it down restores the one they had.

**Journeys, not moves.** `TravelSystem` sends recorded adults out for a year and brings them back — a
merchant along a standing route, a cleric on mission or fetching copies from a monastery, a pilgrim to
a holy place of their faith, a courtier as a guest of an ally. Residence stays where they live; a
journey is a trip. The distinction is load-bearing: changing residence would make a merchant vanish
from their town every year they used a route, and the disasters that reach a residence would miss them
for no reason the chronicle could defend. Travel runs after `trade-routes` so the corridor a merchant
walks is actually open, and its journeys are marked `Routine` — the life page wants the itinerary; the
world's spine does not.

**Campaign memory.** A figure keeps the wars and engagements they stood in — a soldier the battles
they reached, a ruler the wars their realm fought, anyone living in an invested town the siege itself —
each settled with whether their side prevailed once the thing ends, the same late-filled pattern as an
office's `ToYear`. Commanders were already named on the battle; this is how the rest of a life keeps
the same facts.

**The figure page needs a second voice.** A world event ("X declared war on Y") reads wrong on a
person's own page. M16 adds a `.self` narration layer: a second template per kind, keyed `Kind.self`,
telling the same fact as something they did ("Declared war on Y"), with role tests (`{as:ruler}`,
`{self:extra}`, `{not:victor}`) that let one template serve every witness of an event and drop the
segments that do not apply to the reader. Kinds without a `.self` template keep the world wording.

**Numbers still to sweep.** The occupation weights and family pull, the four journey probabilities and
their disposition coefficients, and the office affinities were set by argument. What a sweep would pin:
the occupation mix across a seed panel (that no single trade dominates), that journeys stay a minority
of figure-years, and that raised notables' careers match the offices they were raised for often enough
to be worth the coupling.

### M17: the seed's cosmology, and tomes that read it

**Flavour with a physics.** `WorldCosmology` rolls a host star and a habitable body from the seed
before any history begins: spectral class, mass, radius and luminosity; the habitable-zone edges and
the body's orbit within them; surface gravity, escape velocity, albedo, greenhouse and surface
temperature; and, for a habitable exomoon, the parent giant, the Roche limit and the tidal day. It is
flavour — it feeds no simulation decision, exactly as the world's name does — and it travels in the
export. A set of `CosmologyCheck`s records whether the rolled system is self-consistent, so an
implausible draw is visible rather than silently shipped. The host galaxy was added later, on its
own stream, so the local system did not reshuffle — see the follow-up after M19.

**Why a book can now be about the sky.** The one place cosmology reaches the simulation's output is
`Tomes`: a learned faith may compose a cosmology codex, and it draws on the same rolled system rather
than inventing stars per book — so two codices of one world agree about its heavens the way two
codices of one religion already agree about its gods.

**Tomes and treasures, enriched.** Alongside cosmology, M17 widens what a book can be about and what an
artifact remembers: tomes compose from the figures, campaigns and faith known when they are written (a
chronicle written mid-war still says the outcome was uncertain); treasures gain owners and provenance;
and `ArtifactRevised` records a work continued by a later hand. Contents are composed once and stored
on the artifact, so later history cannot rewrite a book that was already closed.

**Numbers still to sweep.** The cosmology check thresholds and the tome-kind weights — how often a
faith reaches for cosmology over a campaign or its own annals — were set by argument. A sweep would
confirm the check pass-rate across seeds and that no one tome kind crowds out the others.

---

### M18: a people that changes

**The half of the loop M12 left open.** Ruler dispositions gave a realm values that bent for a reign
and fortunes that shifted them for a decade, but both worked from a baseline fixed at worldgen. A
people was therefore exactly what it was founded as under thirty rulers and eight wars, and three
centuries read as a fixed culture with per-reign noise on top. `Civilization.BaseValues` is that
baseline made a realm's own: seeded at founding from the culture, moved a little each year by
`cultural-drift`, and read by `crown` as the thing a ruler bends. `Culture.Values` stays immutable —
the founding seed and the identity anchor — so a people can change without its culture being
rewritten underneath the succession law derived from it.

**Per realm, not per culture.** Two realms of one founding culture diverging is the entire point, and
a breakaway carries the parent's baseline *as it then stood* rather than the founding culture's, which
is what makes a secession the start of a related people rather than a reset to the ancestral one.

**Three pulls and an anchor, no dice.** Drift reads state and draws no random numbers, like `crown`.
Contact pulls a realm toward its neighbours' baselines, weighted by proximity and by the square root
of their population, normalised so the step is bounded however many neighbours there are; sustained
weariness and grievance pull aggression toward a war target; a state faith pulls piety toward its own
fervour; and a weak pull back toward the founding culture keeps the whole thing from running away.
Every term is a fraction of the distance to a target, so nothing needs clipping. It runs late in the
year — after diplomacy, war, trade and religion have settled — and writes the baseline that next
year's `crown` reads first.

#### Where the build found something

**Opinion is the wrong sign for culture.** The first version took affinity straight from
`Relations`, pulling toward realms that liked each other and away from realms that did not. It made
neighbours *diverge*: most contact relations in this engine sit mildly negative — the diplomacy model
bunches opinions below zero by construction — so the common case was a push apart, and friendly
cross-culture neighbours ended 0.46 apart on the six dials having begun at 0.33. The fix is a
statement about what a frontier is: culture spreads down a shared border and a trade road whether or
not the two realms are fond of each other, so contact is convergent by default (`ContactBias`) and
opinion only tilts it. An *active war* is what reverses it into a people defining itself against its
enemy.

**Convergence with no counterweight is a monoculture.** With contact made convergent, a small densely
settled world collapsed: on seed 2 the two most different realms in the world ended 0.16 apart on a
scale where their founding cultures began at 0.28. Convergence is a fixed point at "everyone holds
the average", and nothing in the model resisted it. The anchor back to the founding culture
(`RootsRate`) is what makes the equilibrium sit *partway*, which is the behaviour actually wanted: a
crowded frontier becomes a region of related-but-distinct peoples rather than one people. Its first
value over-damped in the other direction — mean drift fell to 0.09 and nothing moved — and half of it
is the current setting.

**Measuring drift by distance from founding hides the mechanism.** The obvious test — connected realms
should drift further from their origin than isolated ones — failed, and was wrong rather than the
code. War and faith move *every* realm, isolated ones included, so distance-from-founding is dominated
by terms that have nothing to do with contact. The social pull's actual signature is that *neighbours
grow alike*, and it is only legible on the four traits contact spreads: expansionism, tradition,
mercantile and learning. Aggression and piety are what war and faith drive a realm's own way, and
including them measures the two forces fighting each other.

#### What it produced

Five seeds (2, 7, 11, 42, 99) at 300 years:

| | |
|---|---|
| mean baseline shift from founding, per dial | **0.118 – 0.176** (max 0.293) |
| friendly cross-culture neighbours, adopted traits | **0.348 → 0.225** apart per dial |
| realms of different founding cultures, at the end | **0.22** apart per dial — distinct, not merged |
| a realm under sustained war, against itself at peace | **+0.05 or more** aggression over a century |

The third row is the one that matters, and it is the one the two failures above were each a way of
losing: a world that converges without homogenising. `CulturalDriftTests` pins all four, and the
war row is measured as the same world twice with the only difference being what happened to one
realm — a realm's fortunes have faded by the end of a run, and the drift they caused has not.

#### What it cost, and what that measurement actually showed

The social pull needs to know how near two realms are, and proximity — every settlement of one realm
against every settlement of another — is the most expensive question this engine asks in a year.
Drift asks it a few systems after diplomacy has already asked it, so diplomacy now publishes its
contact map on `WorldState`, keyed to the year it describes, and drift reads that instead of
recomputing. A reader in a later year, or in a run whose system list has no diplomacy in it, is told
nothing and computes its own, which keeps this an optimisation rather than a dependency between two
systems. The seed-42 fingerprint is byte for byte what it was before the change — the property that
makes a performance fix reviewable at all.

**The honest numbers are smaller than the first reading of them.** The duplicate proximity looked
like it had tripled the suite, because `NoLandIsHeldByARealmThatHasEnded` — five seeds by eight
hundred years — was taking 8m45s. Sharing the map took it to 8m18s *measured the same way*, about
five percent. The much larger figure first attributed to the fix came from timing that test **alone**
on an idle machine (1m32s) and comparing it against a run under full parallel contention: two
different conditions, not a before and after. What actually dominates that test is the contention of
twenty other heavy tests, not this system.

**Drift does not make a run slower or a world bigger.** Seed 42 at eight hundred years, with the
system and without it: 5.9s against 7.4s elapsed, 132 settlements against 128, 6,090 figures against
6,077. What it does change is how much war there is — **33 wars and 163 battles against 25 and
129** — which is the war pull doing what it was built to do over eight centuries, and the first
number to watch if a later sweep decides the world has become too warlike.

**Numbers still to sweep.** `SocialRate`, `ContactBias`, `RootsRate`, `WarRate`, `PeaceAggression` and
`FaithRate` were set by argument and two rounds of measurement, not by a sweep. The ratio that
actually decides the world's character is `SocialRate` against `RootsRate` — convergence against
identity — and it is the one worth sweeping first.

**Deliberately not built.** Language and naming drift (a separate Markov concern). A drifted people
becoming a *newly named* culture: drift moves values, not identity, and a values-schism that reads as
a new people is a follow-up that would want the naming system in the same breath. Per-person drift
beyond `Disposition`, which already rolls around the culture and now rolls around a culture that moves.

---

### M19: the road has a cost

**What reading the chronicle showed.** M16 gave recorded people journeys, and by seed 42 they were
the third-largest event class in the world — 1,250 of 7,051 — and every single one of them ended the
same way. Nobody was robbed, nobody drowned, nobody failed to come home. A merchant's life page was
forty identical lines saying he had been to the same town again. Travel was the only thing in this
world with no downside, and the shape of that failure was familiar: brigandage had been raised by
unrest since M15 and nothing but the trade ledger had ever read it, so a country could go lawless
without a single traveller noticing.

**A journey now rolls against the road.** The floor is set by the journey's kind, ordered by how much
company the traveller keeps — a merchant moves with a caravan along a corridor somebody's soldiers
nominally patrol, a guest of an allied court travels under that court's protection, a missionary and
a pilgrim have neither. On top of that go the lawlessness of the worse of the two ends, the distance,
and a war at the far end. A mishap kills 28% of the time by land and 55% by water, more past 55, and
a robbery that spares the traveller may still take something they personally owned — the only way an
artifact leaves a settlement in this engine without an army arriving.

**The unsafest end, not the average.** The same rule the trade system already applies to a route's
security, for the same reason: a road is as safe as its worst stretch and nobody is robbed on average.
When one end is lawless enough that the chronicle has already told the reader so, the robbery is
pinned on it — "set upon by brigands out of Tamasqa" is the one line in the world that ties a robbery
to the grievance that produced the robbers.

**Residence is still untouched.** A journey remains a trip. Being robbed does not relocate anyone,
and the mishap is recorded as its own event rather than a field on the itinerary, so a life page reads
"travelled to Shche" in most years and "came to grief on the way to Shche" in the year that mattered.

#### The first consumer of a road

DESIGN has been asking since M17 for something to read road geometry, on the condition that it read
something the route's traffic does not already say. Safety is that consumer, and the term has two
halves. A cut track leaves 80% of the land hazard and a paved road 62% — bridges instead of fords are
most of what the engineering bought. Then the line the road *had to take* gives some of it back: the
ratio of its length to the straight distance between the two towns measures how hard the country in
between is, and hard country is dangerous country. Traffic cannot supply that. Traffic says how much
is carried; it says nothing about how far round the carrying has to go.

Measured before the term was written, because a term that cannot distinguish its cases does not get
kept: the seven roads built on seed 42 have detour ratios from 1.00 to 1.41, and the term reaches 85
of 1,472 journeys with multipliers spread over 0.62 to 0.97. A paved road across easy country makes a
journey a third safer; a track dragged 45% the long way round is worth almost nothing. Nothing here
samples terrain — the path was searched once when the road was built, and its length has been sitting
on the route ever since.

#### What it produced

Seed 42, 300 years: **48 mishaps, 31 survived and 17 fatal**, against 5 battle deaths and 3
assassinations in the same run. Rare enough to be remarkable, common enough to appear — a High Priest
lost in a storm on pilgrimage, a Consul caught by weather in the hills, a merchant of 74 taken by
armed men on a road he had walked for forty years.

**The cause is carried twice, and that is not redundancy.** The clause that reads well in the
chronicle — "came to grief on the way to Shche, when the ship was driven aground" — reads as "died, of
when the ship was driven aground" in an obituary a line later. One string in both slots produces one
good line and one bad one, which is exactly the sort of defect that survives a demo.

#### What it disturbed

Three tests failed that were not about travel at all, and one of them was a real bug.

**A newcomer could be ordained on their wedding day after all.** M18's celibacy work closed three
doors and `BarredFromOrders` still had a fourth open: it asked what faith the *figure* professed, and
a spouse invented for a wedding professes none. So the guard saw a person of no faith and barred them
from nothing — the exact person the rule exists for. The vow that binds is the one held by the temple
standing where they live, and that is what it asks now.

**Two assertions were reading proxies.** A standing realm was required to have a seated ruler at the
final tick, but a ruler can die on a scheduled day after succession has made its pass for the year;
the same run carried one year further seats an heir. And a frontier post was required to stand in a
realm that had fought a war, when the model's own threat test also counts a live truce — and a truce
is sworn between a parent realm and a province that secedes from it. Seed 15 builds a post on exactly
that provocation. Both assertions now ask for their subject rather than for a stand-in that agreed
with it on the sample they were written against.

#### Deliberately not built

**A journey that ends in staying.** The most interesting thing a traveller could do is not come back
*and not be dead* — a merchant's family relocating to the partner city, a missionary who stays among
his converts. It is also a residence change across a realm boundary, which touches houses, offices
and civilization membership, and it belongs in a change that can be reviewed as being about migration
rather than smuggled into one about hazard.

**Pirates.** Lawlessness is a fact about the country around a town, and following it out to sea would
be borrowing a number from a place it does not describe. The sea has its own hazard and no brigands.

**Folding the routine itinerary.** Forty "travelled to Megalophura" lines are still forty lines. The
mishap gives the class a spine, but whether a merchant's standing circuit should collapse into one
recorded fact is a question about the chronicle's shape, not about travel, and it wants measuring
against the viewer's own filters first.

#### The follow-up: a journey that says what it was for

Landed immediately after, because the hazard made the class worth reading and then the class still
would not say anything. A journey rendered as *"travelled to Kaarikkagrad, on pilgrimage"* — a
destination and a category — and the export had held the answer since M16 in `Journey.ViaId`: a
route for trade, a holy site for a pilgrimage, a faith for a mission, a realm for a visit. Only the
template dropped it.

**A new grammar slot rather than a name in `Data`.** `{extra:kind}` resolves the first entity of a
short kind prefix among an event's extra ids, and renders as a cross-link like the named slots. The
alternative — writing the name into `Data` at record time, as `RevoltBroke` does for its leader —
needed no syntax version bump and would have produced dead text. A faith or a shrine named in a
chronicle line is a thing the reader should be able to click, so the grammar route won and
`SyntaxVersion` went to 3.

**One template, four errands.** The slot is absent when the event carries nothing of that kind,
which is what lets mutually exclusive clauses live in one template — exactly one of `{extra:hol}`,
`{extra:rel}` and `{extra:civ}` can hold for any given journey. `purpose` now ends in its own
preposition and the clause supplies the rest, so the four errands read:

> Tanislavius travelled to Sandomicenik, **on pilgrimage to the Shrine of Volaticula**.
> Imilk travelled to Tamattim, **to fetch copies from the Monastery of Gebalisarshalim**.
> Hranislavena travelled to Roshtau, **to preach among the Turovneans**.
> Kleon travelled to Akkaros, **on trade**.

**Trade names nothing, on purpose.** The route is carried and deliberately not printed: "traded to
Shche along the Aigionanvos–Shche route" tells a reader nothing the line already said. A slot exists
so that a template *may* use it, not so that every template must.

**The copying errand stopped lying.** `PickScriptorium` falls back to an ordinary town of the same
communion when no monastery is reachable, and the old wording said "to request copies" either way.
The errand now carries the monastery (`Tomes.ScriptoriumAt`, the same question `HasScriptorium`
asked, answered with the house instead of a yes) and is recorded as what it actually is — a circuit
among co-religionists — when there is no library to send anyone to. It fires in 11 of 12 seeds;
seed 42 is the exception, because both its monasteries are founded after year 267.

**No behaviour changed.** Verified rather than asserted: seed 42 exports the same 6,919 events with
identical `(year, kind, subject, object, location)` skeletons, the same 38 settlements, 1,365
figures, 5 wars and 63 routes. The 574 events that differ are the non-trade journeys, and they
differ in their purpose text and one via id. The fingerprint moved because the recorded facts moved,
which is the case the golden is meant to catch and approve rather than forbid.

---

### Follow-up: the host galaxy (M17, not a new milestone)

M17 rolled a star and a habitable body from the seed and stopped at the system. Astra Extera later
grew a host galaxy around the same idea — morphology, a habitable annulus, an observer's site,
metallicity enough for an iron core and ores — and Historia now carries that layer too. It is the
same contract one scale up: flavour, unique to the seed, visible in the export and the cosmology
tomes, and unread by any simulation decision.

**Its own stream, so the sky does not jump.** The galaxy is rolled from `world.galaxy.morphology`,
`world.galaxy.elliptical` and `world.galaxy.spiral`, never from `world.cosmology`. Adding it cannot
reshuffle the host star or the habitable body, which is the same isolation the world's name already
has from the history. The local-system checks still pass; two new ones record whether the site sits
in the galactic habitable annulus and whether the crust can hold iron and ores.

**Spirals are the common case; giant ellipticals are rare.** About one world in forty is an
elliptical. Spirals take a metal-rich ring outside a crowded inner disk, two or four arms, and a
thin disk a few hundred parsecs thick. Ellipticals are more massive, have no arms, and keep their
habitable shell farther out in a spheroid — the core is dynamically hostile. A site is rejection-
sampled until it has [Fe/H] high enough for an iron core (≥ −0.50) and ores (≥ −0.30), a supernova
rate no worse than 2.5× the solar neighbourhood, and a radius inside the annulus.

**Schema 31.** The export grows `cosmology.galaxy`. Cosmology tomes gain a "The host" section that
agrees about the galaxy the way they already agree about the star. The cosmology page draws the
galaxy face-on and edge-on, with the habitable ring and this world's mark. Schema 32 later added
comets on `world.cosmology.comets` and painted Astra's night sky — unresolved disk glow plus
resolved stars — from the same observer site, still unread by the simulation.

**Numbers still to sweep.** Elliptical frequency was set to 2.5% by argument, matching Astra. A
panel across seeds would confirm the rate, that no site falls through the metallicity floors, and
that the independent stream really does leave star class and orbit untouched — the tests already
assert the last two on 250 seeds.

---

### Personal quarrels: what a grievance does when it cannot start a war

The world could already produce a wrong done to a named person — an office taken away, a succession
lost, a relative murdered, a plot exposed and a name given to the court — and #128 gave those wrongs
somewhere to live, as a directed grievance on a bond. What none of it could do was be *acted on*.
Between not speaking at dinner and raising an army there was nothing, so two people who hated each
other for forty years produced exactly as much history as two people who had never met.

**The four causes are the whole permission.** A quarrel is opened by the event that caused it, not
by an annual survey of who dislikes whom. `Offices.Revoke`, the surviving loser of a contested
succession, an exposed conspiracy and a murdered relative each offer one; nothing else can, and a
grievance below 0.30 is not offered at all. That is deliberately a narrow door. A model that can
find hostility by scanning pairs will find a great deal of it, all of it plausible and none of it
about anything, and the export becomes a list of courtiers who dislike each other for no year.

**The ladder is visibility, not anger.** Grudge, insult, charge laid, satisfaction demanded. A rung
cannot be skipped, so a duel always has the record of the years it took to get there, and each rung
is harder to step back from than the one below it — the withdrawal chance is braked from 1.00 at a
grudge to 0.45 at a challenge. Climbing reads the grievance already in the bond, the anger derived
from memory, both dispositions and the realm's aggression; pulling back reads piety, custom,
whether anyone with standing is available to judge it, and rank.

**Rank is why this system is not the conspiracy system.** A subject with a grievance against their
own reigning ruler cannot call them out, so a power gap adds up to 0.30 of restraint and the quarrel
cools, is judged, or goes where such things went historically. `Conspiracies` and `Disputes` read
the same bonds and the same grievances and deliberately answer different halves of the same anger:
one is what you do to someone you can face, the other is what you do to someone you cannot. A
regression across the panel holds duels against reigning rulers at or below duels between equals.

**One wound model.** A duel wound goes through `LifeStories.Injure` — the same severity draw,
recovery years and permanent tail as a battle wound — and a duel death goes through `Houses.Die`
with `DeathCause.Duel`, so succession, offices, estates and bereavement all follow from it without
a private path. `FigureInjury.BattleId` became `CauseId` with a `SourceKind` beside it, which is the
only honest way to say that the cause is a battle in one case and a person in the other.

**Both pages, one record.** The two parties share the object rather than each holding a copy; the
viewpoint is derived at the export edge from which side is being read. Two copies of a quarrel is
two chances to disagree about what happened in it.

**Measured across seeds 2, 7, 11, 42 and 99.** Five to nine quarrels per 300-year world among
1,100–1,350 adults, the longest running 8–17 years, and 13–23 chronicle lines out of 9,000–16,000
events — 0.08% to 0.20% of the timeline. All four causes appear. Across the panel the outcomes are
20 lapsed (death or distance took a party first), 10 reconciled, 2 judged, 3 ending in a wound and
1 in a death. The tests hold the count under one per twenty adults and the event share under 2%,
which is the bound this system most needs: a world with two hundred feuding courtiers is not a
livelier world, it is a timeline in which nothing else can be read.

**Schema 35.** Figures gain `disputes[]` and an injury names its cause rather than its battle.

**Three latent faults it shook loose.** Moving every history is also a sweep, and it found three
things that were reachable before and simply had not been reached. A realm whose seat was taken in
a season after `succession` had run for the year kept a null capital until the following spring —
and for ever if it happened in the last year — so its living people exported as living nowhere;
`Realms` now reseats at the moment of the transfer instead of leaving the repair to an annual pass
that has already gone by. A figure who married and took orders in a realm professing nothing broke
no rule doing it, and a celibate faith arriving in their town twenty years later made them a
married priest of it retroactively; `ConvertTheFaithless` now declines to enrol them rather than
rewriting a career or a household after the fact. Reseating in turn had to release the governorship on the town it
promotes, for the same reason and with the same timing: a capital is governed by whoever holds the
throne in person, the office system drops such a posting on its yearly pass, and a seat promoted
after that pass has run would otherwise leave a governor sitting in a capital. And the road
ceiling, a per-world share, was being asked of an eighteen-route network where one road moves it
six points — it is now asked only where the share carries information, with the pooled bound doing
the work it always did.

The common shape of the first three is worth naming, because it is the same one the artifact
system's estate settlement hit a milestone earlier: an annual repair cannot see damage a seasonal
system does after it has run for the year, and in the last year of a run it never sees it at all.
Where the repair is cheap, doing it at the moment of the damage is strictly better than moving
another system onto the season.

---

### The sky is a thing that can be wrong about (M20, first slice)

M17 rolled comets on real orbits and left them as flavour: exported, unread, feeding no decision.
The interesting part was always their periods. Seed 11 carries one on 74.8 years and one on 160.7,
so a three-century run sees the first four times and the second twice — long enough for the same
object to be seen by people who never met, which is the only reason an interval can be noticed at
all.

**The schedule is derived, never stored, and never rolled.** `Skywatch.Apparitions` computes returns
from the orbit and the argument of periapsis, which serves as the phase so that no comet arrives in
year one merely because that is when the chronicle starts. A world simulated for three centuries and
one never simulated at all agree about every return, and a test asserts it. That property is the
whole point: a prediction made in year 152 can be checked against something the simulation knew
before the realm existed, rather than against a roll.

**A faint comet has to be rare.** Brightness alone admitted seed 7's ten-year visitor, which returned
thirty times and was written down a hundred and forty-eight times — a chronicle of the weather. A
bright comet earns its line at any period; a faint one needs twenty-five years, so that most people
who see it are seeing it once. Seed 7 went from 33 chronicled returns to 2.

**Looking up is not universal.** A realm records a return only if somebody there keeps records — a
scribe or a priesthood — and the odds read the realm's learning, the brightness, and whether it was
at war. Across the panel: seed 42 has nothing in its sky for three centuries, seed 99 has one
apparition ever, seed 11 has seventeen. The character of a world's astronomy falls out of the seed,
which is a better argument for the cosmology roll than anything M17 could make on its own.

**The best thing here was not designed in.** The interval an observer derives is what their own
realm's register held, so a realm that missed a return counts from the one before and gets a clean
whole multiple of the truth. Across the panel, thirty intervals are the real period and sixteen are
two, three, five or seven times it. Someone with a century and a half of honest evidence about a
seventy-five-year comet will name the wrong year, and the sky will refute them. That is a real error
mode of real astronomy arriving for free, and it is pinned by a test, because every claim #147 will
adjudicate rests on it.

**Where it runs.** In the artifact pass rather than a system of its own: an observation is a made
record like the tomes beside it, it needs the year's offices and households settled to know who keeps
the register, and a system of its own would change the order hash for a pass that writes a handful of
lines a century.

**Schema 36.** The cosmology gains the true schedule and figures gain what they wrote down, including
the realm whose register it went into — recorded rather than read off the observer later, because
people change realms and a book does not follow them.

---

## Milestones

| # | Deliverable | State |
|---|---|---|
| M0 | Repo, solution skeleton, DESIGN.md | done |
| M1 | Vertical slice + terrain discipline | done |
| M2 | Determinism hardening | done (landed with M1) |
| M3 | Markov naming languages | done |
| M4 | Culture traits + settlement lifecycle depth | done |
| M5 | Figures: dynasties, succession, marriages | done |
| M6 | Diplomacy & war: named battles, territory transfer | done |
| M7 | Viewer depth: territory rendering, richer filters | done |
| M8 | Flavour: religions, artifacts, plagues, disasters | done |
| M9 | Phase 2 spike: raster-backed `ITerrainSampler` | done |
| M10 | Phase 2 proper: site selection with teeth on real terrain | **done** |
| M11 | Offices: appointments, governors, founding parties | **done** |
| M12 | Rulers who react: dispositions, realm fortunes, trait-aware elections | **done** |
| M13 | Sub-year time: seasons, dated events, scheduled episodes | in progress |
| M14 | Notable households: families for the figures an office raises | **done** |

M12 landed first. `Disposition` is the record both milestones read, so building it once — rather
than landing `Centralism` alone with M11 and folding it in a milestone later — was the cheaper
order, and the foundation went in without changing a single existing history. M11 and M10 remain
independent of each other: no terrain dependency one way, no figure dependency the other.
See *Offices: what a court does with the people it already has* and *Rulers who react: a people,
a person, and a recent past* above.

M13 follows M10 rather than running beside it, and the reason is the golden: both regenerate the
seed-42 fingerprint, and a single regeneration covering two milestones' worth of change is
exactly what that test exists to prevent. It touches every system in the engine and is therefore
staged so that the first step of it changes no history at all — see *Time: seasons on the year,
and days where they are earned* above.

Unrest and cultural drift — a people that deposes a ruler it has diverged from, or slowly
becomes what a long line of them wanted — are the two halves of the loop M12 leaves open, and
are not scheduled.

### As built

Sixteen yearly systems, in order (the order is hashed). `crown` settles first — each realm's
fortunes fade by a year and the values it will be governed by are fixed before anything reads
them, so every judgement made within one year is made against the same ruler in the same mood.
Then: `population` → `plague` →
`disaster` → `settlement-lifecycle` → `specialization` → `expansion` → `religion` →
`diplomacy` → `war` → `trade-routes` → `figure-incidents` → `figure-lifecycle` →
`succession` → `houses` → `artifacts`. Reads as a causal chain: populations change against the
harvest, pestilence and the land take their share, settlements acquire character, pressure moves
borders and faiths, neighbours judge each other, wars are fought, commerce responds to the
resulting peace, exceptional hazards and then biological mortality empty thrones, succession
fills them, houses continue, and what the survivors made is written down.

Diplomacy follows expansion so an opinion is formed about the frontier that exists rather
than last year's, and war follows diplomacy so a war declared this spring is fought this
summer. The last three remain the tightest coupling in the list, and war now leans on the
same property: deaths must precede succession or a realm spends a year without a ruler for
no reason the chronicle can explain — as true of a king killed at a siege as of one who
died in bed — and succession must precede the houses or a new king's brothers are still
ranked as heirs on the day he is crowned, and marry accordingly.

Measured on seed 42, 300 years, 8 civilizations, 4096-unit world:

| | M1 | M4 | M5 | M6 | M7 | M8 | M9 |
|---|---|---|---|---|---|---|---|
| Wall clock | ~65 ms | ~67 ms | ~215 ms | ~250 ms | ~233 ms | ~240 ms | ~253 ms |
| Events | 359 | 950 | 3,299 | 3,216 | 3,225 | 3,083 | 3,083 |
| Settlements | 96 | 96 (15 cities), 1 abandoned | 96 (15 cities), 1 abandoned | 91 (15 cities), 1 abandoned | 91 (15 cities), 1 abandoned | 75 (13 cities), 1 abandoned | 75 (13 cities), 1 abandoned |
| Figures | 81 | 81 | 1,072 | 1,033 | 1,033 | 919 | 919 |
| Houses | — | — | 16 (8 standing, 8 died out) | 15 (6 standing, 9 died out) | 15 (6 standing, 9 died out) | 15 (4 standing, 11 died out) | 15 (4 standing, 11 died out) |
| Wars / battles | — | — | — | 10 / 38 | 10 / 38 | 10 / 40 | 10 / 40 |
| Faiths / artifacts | — | — | — | — | — | 13 / 27 | 13 / 27 |
| Civilizations fallen | 0 | 0 | 0 | 2 | 2 | 3 | 3 |
| Simulation samples | 6,050 | 6,050 | 6,050 | 5,990 | 5,990 | 5,798 | 5,798 |
| Export size | 0.73 MB | 0.73 MB | 1.36 MB | 1.36 MB | 1.36 MB | 1.31 MB | 1.31 MB |
| Tests | 100 | 100 | 114 | 129 | 134 | 145 | 164 |

**M9's column is M8's column, and that is the result.** A milestone that added a second
terrain backend moved nothing but the test count, because the export fingerprint for seed 42
is unchanged — the same history, byte for byte, which is the only proof that the new backend
was added *beside* the simulation rather than *into* it.

**Terrain sampling went down.** Two systems that both reason about geography added nothing
to the budget — every question they ask is answered from region statistics derived once at
world creation — and the small fall is fewer settlements being founded, because conquest
took two realms out of the expansion business. A milestone about war costing negative
terrain samples is the discipline working.

**One seed is a poor sample of a stochastic process**, so the war figures are quoted
across eight. Per 300-year world: **15.5 wars, 59 battles, 11 sackings, 10 provinces
ceded, 4.8 alliances sworn, 5.4 calls to arms answered, 1.6 civilizations conquered.** The
spread is the point — seed 1 fights five wars and loses nobody, seed 99 fights
twenty-four — and worlds have characters rather than a common rate. Wars run a median of
six years and rarely past fifteen. All four grievances occur: 46 conquests, 39 border
disputes, 21 wars of revanche, 18 pressed dynastic claims. Aggressors win about four times
as often as defenders, which is what choosing when and whom to fight is worth, and two
wars in five settle nothing at all.

**Civilizations now fall, and it took conquest to do it.** Climate alone never could: a
capital sits on the best land its realm could find and carries a capacity bonus, so a
realm sheds its marginal holdings and keeps its seat. Through M5 a three-century world
lost nobody. It now loses one or two, and an 800-year world with fifteen realms ends with
seven — eight fallen, seven of them conquered outright.

**Event volume fell, and that is the trade rather than a regression.** 3,299 → 3,216 at
three centuries, and 19,257 → 15,890 over 800 years with 15 civilizations. War adds a few
hundred events of its own and removes the realms that were generating thousands: a
conquered realm holds no more courts, and courts are what produce births, marriages and
successions. Approaching the brief's 50k target now depends on M8's flavour systems and on
longer runs rather than on more realms surviving. The viewer is built for 50k regardless.

**One aggressive culture can dominate a world, and the chronicle says so plainly.**
Heraanes on seed 42 rolls Aggression 0.97, declares nine of the world's ten wars,
extinguishes two realms and takes five provinces across three centuries — every province
any realm took from another that run. That is not a runaway to tune out — it is the trait
doing exactly what it is for, and it is the most legible thing in the export.

**Houses consolidate over long runs, and war accelerates it** — an 800-year world now ends
with four houses standing and twenty died out, against seven and eighteen before M6. A
conquered realm is a court that stops producing heirs, so the houses that were following
it stop being written about; the mechanism is the same one that always drove
consolidation, with conquest supplying more of the input. Two rules slow it without
stopping it, and neither should stop it: an elective ballot fields one candidate per house
rather than a whole line, and passes over a house that already rules elsewhere.
A handful of great houses spread across neighbouring realms by marriage is what
late-medieval Europe actually looked like.

**Government form is legible in the shape of a ruler list.** Over three centuries the two
chiefdoms hold 18 and 19 reigns between one and two houses; the three realms that elect a
ruler for a fixed term hold 25, 28 and 41 across five to seven. A theocracy that elects for
life sits between them at 13 across three. The office changes hands on a schedule, and it
changes families while doing so — which is exactly the difference the law was meant to make
and the thing that was invisible before the ballot fielded one candidate per house.

**Abandonment is rare by design** — one settlement in 300 years, more over longer runs.
Marginal settlements are only founded once a civilization has run out of good land nearby,
and on a 1024-region world that takes centuries.

The martial spine of one world, seed 42 — every declaration, sacking, cession and peace
in three centuries. Note that the same world's dynastic spine now reads differently from
the one M5 produced: conquest changed which courts existed to have successions in.

```
 75  Heraanes declared war on Tyroslenses, in naked conquest. So began the War of Fjordvik.
 76  Calatae was sacked by Heraanes, losing 415 people.
 79  Fjordvik was ceded to Heraanes, and with it Calatae.
 79  The War of Fjordvik ended after 4 years, in victory for Heraanes.
 79  Tyroslenses came to an end after 78 years, its last holdings taken in war by Heraanes.
125  Heraanes declared war on Smolenovtsi, in naked conquest. So began the War of Sland.
127  Sandomice was sacked by Heraanes, losing 140 people.
130  Sland was ceded to Heraanes, and with it Sandomice.
130  The War of Sland ended after 5 years, in victory for Heraanes.
130  Smolenovtsi came to an end after 129 years, its last holdings taken in war by Heraanes.
187  Heraanes declared war on Lundfjilaiset, in naked conquest. So began the War of Taipalsvik.
197  The War of Taipalsvik ended after 10 years, with neither side prevailing.
212  Calatates declared war on Juvaltavaki, over the frontier. So began the War of Draugrad.
222  Puolijoki was sacked by Calatates, losing 113 people.
224  The War of Draugrad ended after 12 years, with neither side prevailing.
231  Heraanes declared war on Lundfjilaiset, in naked conquest. So began the Second War of Taipalsvik.
235  The Second War of Taipalsvik ended after 4 years, with neither side prevailing.
241  Heraanes declared war on Calatates, in naked conquest. So began the War of Asgarnesberg.
246  Falerienum was sacked by Heraanes, losing 667 people.
246  Mordvik was ceded to Heraanes, and with it Falerienum.
246  The War of Asgarnesberg ended after 5 years, in victory for Heraanes.
250  Heraanes declared war on Lundfjilaiset, pressing a claim through marriage. So began the War of the Lykos Succession.
256  Mordaljoki was ceded to Heraanes, and with it Eldfellirvik.
256  The War of the Lykos Succession ended after 6 years, in victory for Heraanes.
269  Calatates and Ilibalim swore an alliance.
274  Heraanes declared war on Lundfjilaiset, in naked conquest. So began the Third War of Taipalsvik.
276  Eldfellir was sacked by Heraanes, losing 551 people.
278  Taipalsvik was ceded to Heraanes, and with it Eldfellir.
278  The Third War of Taipalsvik ended after 4 years, in victory for Heraanes.
290  Heraanes declared war on Juvaltavaki, in naked conquest. So began the Second War of Draugrad.
293  Puolijoki was sacked by Heraanes, losing 273 people.
297  The Second War of Draugrad ended after 7 years, with neither side prevailing.
299  Heraanes declared war on Lundfjilaiset, in naked conquest. So began the War of Skoghaboenes.
300  Snorranes was sacked by Heraanes, losing 108 people.
```

Everything M6 was for is legible in that list without knowing anything about the model.
Heraanes is the world's aggressor and has been since it rolled its culture. Lundfjilaiset
is the neighbour it cannot finish, attacked four times in a century — twice to a
standstill, once on a marriage claim and once for a province. Two realms end. The wars
that settle nothing are as visible as the wars that do, which is what stops the
chronicle reading as a scoreboard.

### M7: the map in time

The only figure M7 moved is the event count, +9 on the standard seed, and that is the
milestone working as intended: it is a viewer milestone, and the engine changes it needed
were three missing records rather than any change to what happens.

**Territory is drawn as realms, not as the cells it is stored in.** One filled path per
realm and an outline only where the neighbouring cell belongs to somebody else. The
per-region rectangles it replaced made every realm look like graph paper and made a
frontier between two realms indistinguishable from the seam between two provinces of one —
on a map whose subject is where the borders are.

**Every political layer is drawn for a selected year**, replayed from the chronicle:
borders, which towns existed and at what size, who sat which throne, and the battles fought
that year. The slider plays. Scrubbing an 800-year world of 15,909 events costs ~7 ms a
step, so playback runs at its full rate on the largest history the engine currently
produces — the cost is one binary search per region rather than a snapshot per year, which
is also why the export did not have to grow.

**It changes what the world file is worth reading for.** Seed 42's flat final map shows
Heraanes holding 22 regions. Scrubbed, it shows how: one region for its first thirty
years, four by 79, and 22 by 286 — of which **18 came from settling empty land and five
were taken at a peace table**, in 79, 130, 246, 256 and 278. Every one of those five years
is the last year of a war it declared, and its page shades the years it spent at war
behind the extent curve, so the five steps that sit on a shaded band separate themselves
from the eighteen that do not. The realm that reads as a runaway in the summary reads as
nine declarations and a great deal of ordinary expansion in the history, which is the
difference the viewer exists to make.

**Filters are faceted, and the counts are the point.** Each list carries filters built from
what is actually in the world, and each option shows how many rows it would leave —
counted against every other filter but its own, so narrowing to cities tells you how many
are known for mining rather than repeating that there are ninety settlements. It answers
the comparative questions the tables were already sorted for: which wars moved a border
(5 of 10), which houses ruled more than one realm, which figures married in rather than
being born to a house.

### M8: flavour, and what it cost

Four systems — plague, disaster, religion, artifacts — and one order change. The year now
reads: population → **plague** → **disaster** → lifecycle → specialization → expansion →
**religion** → diplomacy → war → figure-lifecycle → succession → houses → **artifacts**.
Plague and disaster follow growth so a year's mortality applies to a settlement that has
already grown, and precede the lifecycle so a town gutted this year is judged this year.
Religion sits between expansion and diplomacy for the reason diplomacy sits after
expansion: an opinion should be formed about the frontier *and the faith* that exist now.

**Faith is held by settlements, not realms.** A state religion would have been one field
and a decree, with nothing happening between decrees. Per settlement, a faith crosses a
border before it crosses a throne, a realm can be religiously divided, and a schism has
somewhere to start. A realm's own faith is derived — whatever its capital follows —
which means a realm can change religion having converted nobody, by losing the capital it
had. Six such changes on seed 42.

**Faith now leaves architecture and geography behind.** Every new faith establishes a holy
site, and established congregations sometimes raise another as they convert. A temple, church or
shrine inside a town shares its settlement coordinate; a monastery or sanctuary beyond the walls
is a first-class location with its own exact coordinate in the surrounding region. The distinction
matters because an independent pilgrimage site is not a settlement — it has no invented population
or owner — while both kinds remain on the map after the faith fades or the nearest town empties.
Remote sites reuse the exact terrain grid refined when their nearby settlement was founded, so
religious geography does not introduce a per-year terrain-sampling cost.

**Plagues had to be bounded, and finding out how was the milestone's real work.** The
first cut spread along distance and traffic with no other term, and produced five
pandemics in three centuries that each reached *every inhabited settlement in the world*:
eighteen abandonments against M7's one, five cities left of fifteen. The flavour milestone
had become the dominant force in the model. What was missing is that people react — a
plague two towns away closes gates and turns travellers back — so the spread chance now
decays against how far the outbreak has already got. One line, and it is the difference
between a regional catastrophe and a world-ending one. Across eight seeds a world now sees
**5 plagues per 300 years, each reaching about three settlements and killing 12,300 people
between them**.

**Every disaster is drawn from the ground it struck**: floods need a river, storms a coast,
eruptions violent geology and height, wildfire a dry warm biome with something to burn. So
the map explains the chronicle — the mining town in the mountains is shaken and the coastal
trading city is wrecked by storms — and it costs nothing, because every trait consulted is
a region statistic derived once at world creation. `FlavourTests` asserts the property
directly: no settlement is ever struck by a disaster its terrain cannot produce.

**The one term that changes M6 rather than adding beside it is faith in diplomacy, and it
was measured rather than assumed.** Running eight seeds with the coefficient zeroed and
again at its chosen weight: **16.9 wars and 64.5 battles without it, 20.4 and 78.1 with**
— about a fifth more war, with settlements, cities and figures unchanged to within noise.
M8 deliberately stopped there. The later war-cause pass kept that pressure and added two
narrow grievances on top: a devout realm can name one relic in a neighbour's treasury, or
a sufficiently fervent faith can make a war against another faith explicitly religious.
Difference alone is still only a thumb on the diplomatic scale.

**Two pre-existing bugs surfaced, both found by plague deaths reaching code that had never
seen a death.** A figure holding two offices at once — a regency and a throne — had only
the most recent closed when they died, leaving a regent recorded as governing three
centuries after their death; deaths now close every open title. And a realm could be
declared upon while already fighting, because the declaring side checked whether *it* was
at war and never whether its target was: on seed 99, Vladane was called into an ally's war
in 296 and declared upon in 297. Both were reachable before M8 and neither occurred in any
tested seed, which is the argument for adding systems that push on the ones already there.

**Seed 42 is a harsh draw and the table above says so.** Six plagues killed 22,670 people
there against a mean of 12,300, which is why it ends with 75 settlements and 13 cities
where the eight-seed mean is 100 and 27. Abandonment stayed rare as designed — 0.1 per run
across seeds, one on seed 42, unchanged from M4 through M7.

**Looting is the most evocative thing here and the rarest**: 0.9 artifacts carried off per
world. It needs a sack of a settlement that happens to hold one, and with 24 objects spread
across a hundred settlements the overlap is thin. It is left alone rather than tuned up,
because the objects are meant to be famous and a world where everything portable changes
hands every generation makes them inventory. Seed 42's single instance is exactly the page
the model was built for: Reykholmasalmi held two relics, made in 71 and 96, and when the
place was sacked in 142 one was carried off to Aigionanvos and the other did not survive
the night. Both facts are on both objects' pages, and neither needed an event kind of its
own — a sack already knew how to happen.

**A tome contains the history available when it was written, not the final export's
omniscience.** Books choose among a ruler's life, a commander's service in one war, the
settlement's faith and its own local annals, then preserve several linked passages on the
artifact. A campaign account written before the treaty continues to say that the outcome
was unsettled even after the simulation knows the victor. Rites and teachings are the one
invented part: they are derived from the faith's identity rather than from the individual
book, so two codices of one religion agree about what its followers do.

### A figure's own faith

Congregations are still settlements. A person is not. Before this, the only faith a figure
had was whichever church their town or their realm currently named — so a high priest of a
fading church was indistinguishable from their neighbours, and a child raised after a
conversion was rolled as if the sermon had never happened.

**`Figure.ReligionId` is personal.** Assigned at birth from the birthplace, then a parent,
then the realm. Living people born before any faith existed pick one up the year it reaches
their residence, without rewriting the disposition already rolled. A town that converts does
not convert its people: that is the whole reason the field exists. If it always matched the
residence it would be denormalised data.

**The faith tints the disposition, and does not consume the roll.** `Disposition.Roll` still
draws around the culture from a fork on the figure's id. `FaithCharacter.Inclines` is the
teaching — dogma onto the six cultural dials, authority onto centralism — and
`Disposition.TintedBy` blends toward it by a piety-weighted pull. A worldly sibling of a
devout one drew the same numbers; the devout one moved further. No further dice, so adding
the tint cannot shift any other stream.

The same teaching is the second consumer of `Inclines`: a fervent establishment's
`Succession.Wanted` blends toward it, so a church of warfare asks for a warlike ruler and
not merely a pious one.

Holy office reads the person. A courtier of another church is not eligible for this one's
seat; taking the seat is how a faithless one enters it.

**Schema 16** carries `religionId` on each exported figure.

### Faith character: what a religion is, not only how hard it presses

M8 gave every faith a name, a founder, a fervour, and a congregation. That was enough to
spread, schism and fade, and not enough to tell two churches apart. Fervour is how hard a
faith presses *outwards*; it does not say how many gods it admits, who may speak for it,
whether it will coexist, or how readily it splits. Those are now a `FaithCharacter`,
rolled once at founding from the culture it arose among — piety colours the gods and the
church, tradition the clergy, learning a monastic bent — and never revised. A later
congregation that believes something else is a schism, not an edit.

**The dials besides fervour each move an outcome in the religion tick.**

| Dial | Moves |
|---|---|
| **Fervour** | Conversion press (unchanged role) |
| **Zealotry** | How hard a congregation holds what it already believes |
| **Tolerance** | Damps conversion of a neighbour that already believes something else; gates religious war |
| **Schism proneness** | Per-faith split chance, further weighted by authority |
| **Syncretism** | Kinship conversion (parent/child, or the same gods when both sides are syncretic); how close a splinter stays to its parent |

**Structure is not flavour.** Deity structure *admits* holy-site dedications, it does
not merely bias them: a monotheism raises houses to a God or a saint, never to a nature
spirit or an ancient god; an animism does the reverse. Authority admits the form of the
house — a church is a hierarchical (or monotheistic) word, and an animism will not
raise one — and how readily the crown names a high priest. Wealth practice tilts
independent sanctuaries (mendicant) against landed temples. Offerings follow dietary
rules, so a dry congregation is not described leaving wine. Clergy admission is read
when a high priest is invented or chosen from court: a male-only faith does not raise
a priestess, a bloodline faith prefers a dynast, and a celibate clergy is refused
marriage for as long as they hold the seat.

**Cosmology, dogma and observance are the identity tomes already promised.** Rites and
teachings used to be a random pick forked on the faith's id, which made two books of one
religion agree without either of them describing *this* religion. They now pick from
tables keyed on deity, afterlife, dogma, diet, dress and festival season. The festival is
a season because the world's calendar has four; it is named in the books and is not yet
a yearly tick.

**Schism inherits.** Cosmology usually survives a split — people leave a church over who
may speak for it, not over whether the soul transmigrates. Authority and dogma are the
seats of the argument. A syncretic parent keeps its children closer.

Bloodline clergy made high priests out of dynasts, who enter the marriage pool that
invented clergy never do. A sitting officer who moved realm on marriage left their seat
in one civilization and their person in another — the M5 class of bug, now also covering
every office below the throne. `HouseholdSystem.WhoMoves` will not move a sitting officer;
a foreign officer is not a marriage candidate.

Fervour itself is still the first draw on the faith's own stream, so the number that
already fed diplomacy, succession and religious war did not move to make room for the
rest. The character stream is a fork of that one.

#### What this does not yet do, and where it should land

These are stored so a later system can read them rather than invent a second vocabulary.
None of them should grow a parallel "religion flavour" table.

| Term | Belongs to | Why it is not wired yet |
|---|---|---|
| Tithes / landed wealth as an economic fact | `PopulationSystem` / harvest | A tithe that moved carrying capacity would be the first religion term to change M4's demography. Measure it the way the M8 faith-in-diplomacy coefficient was measured, or do not add it. |
| Festival as a yearly gathering | a calendar tick; then trade and opinion | Would boost traffic on routes into a holy site's region in that season, and perhaps standing between co-religionists. Needs a seasonal pass that does not currently exist — the year is the tick. |
| Daily prayer / dress / diet as settlement modifiers | population happiness, if that ever exists | Observance is currently chronicle-facing. A happiness dial would be a new system, not a new religion field. |
| Hereditary priesthood as true office succession | `OfficeSystem` / `FillMode.Customary` | **Built in M14.** `Offices.HeirTo` gives the last holder's child the seat, weighted by Tradition and pushed down by the ruler's Centralism. Bloodline clergy still prefers a dynast from court and is tried first: drawing every priest from the ruling house is a different claim from one family keeping one seat. |
| Tolerance as a diplomatic standing term | `DiplomacySystem.NaturalStanding` | Faith-divide is still the measured M8 coefficient (piety × same/different). Replacing "different" with a tolerance-weighted distance would rewrite war volumes. Measure it before substituting. Tolerance currently only gates whether a fervent faith will *name* the war religious. |
| Syncretism absorbing neighbour traits in place | religion tick | Absorbing would mutate a living faith, which this model forbids: change is schism. A later "local rite" overlay on a settlement is the honest version — the faith stays, the town's observance drifts. |
| Holy sites as pilgrimage magnets | conversion pull / trade | Sites already exist as geography. Weighting conversion pull by a nearby shrine, or founding a seasonal fair, is the next use of that geography. |

**Schema 14** carries the character on each exported faith. The golden moves because
conversion, schism, holy-site form, appointments and tome text all read it.

**Written works circulate without becoming a pile of new artifacts.** A tome's copying
ceiling is fixed when it is written: tradition, mercantile culture, religious subject matter,
city and capital scriptoria, and trade, craft or shrine specializations make duplication more
likely. Some manuscripts remain unique, most reproducible ones allow one or two additional
settlement copies, and a hard ceiling of four preserves scarcity. The ceiling is only potential;
an actual copy takes a later yearly roll and passage within a realm, along the tome's faith, or
over a persistent trade route. Each copy records its destination and the settlement
whose exemplar it used, so distribution can branch gradually without giving every copy the full
provenance and fame of a singular treasure.

### M9: terrain from somewhere else

The claim the whole three-phase plan rests on is that the simulation runs against an
abstract terrain interface and the backend can be swapped without touching simulation
code. Until this milestone that claim had never been tested, and could not be: the only
backend in existence was written alongside the interface, by the same hand, and produced
every field the interface asks for. M9 is the first time something else answers.

**Rasters, not a library.** Of the three routes in the Phase 2 notes, the raster one is
built. A backend that consumes heightmap and climate planes binds to no generator's
codebase, so Azgaar's Fantasy Map Generator, a WorldEngine export, a GIS raster and a
painted heightmap are all the same amount of work — one conversion. The format is PGM,
which is not a fashionable choice and is the correct one: the engine takes no NuGet
dependencies so that the assembly which eventually loads into Vintage Story cannot
conflict with the game, and the BCL decodes no image format whatsoever. PGM is the one
raster format that is a hundred lines to parse, and everything writes it.

**Sea level is zero by construction, not by arithmetic.** The datum rule was written in
M1 — heights are metres relative to a sea level of exactly 0, no backend defines its own
— and a foreign heightmap is the first thing that could break it. Generators put the
shoreline where they like; Azgaar's is 20 on a 0..100 scale. Mapping such a plane through
a single linear range puts the coastline wherever the arithmetic lands, and every coastal
settlement in the world with it, in a world that still looks entirely plausible. So the
manifest names the shoreline value and the loader scales the two sides of it separately.
The join is exact, which turns the contract from something a manifest author has to get
right into something the loader cannot get wrong.

**`TerrainCapabilities` finally has a backend that declares less than everything.** The
flag set was written in M1 against a stated fear: Phase 1's noise sampler produces six
fields as easily as one, so code written against it would silently assume everything is
always available. That fear was untestable for eight milestones. Real generators export a
heightmap and, with luck, one climate layer — so the raster backend requires only height,
models the rest from latitude and elevation, and leaves the modelled fields **out of its
declaration**. A bare heightmap runs a full world and reports honestly that five of its
six fields are inferred. The CLI prints the split, because a world built on a heightmap
should say so rather than present six numbers as though they were all observed.

**Interpolation inside a backend is not the interpolation the contract forbids.**
`ITerrainSampler` tells implementations to stay dumb — no caching, no interpolation — and
a raster sampler does bilinear reads. That rule is about not duplicating `TerrainAtlas`,
which decides *which* points are worth paying for. Reading between the values of a finite
raster is how this backend answers the point it was asked about at all; nearest-neighbour
would quantise every coastline to the raster stride and put settlements on a visible grid.
The distinction is worth stating because the rule as written does not make it.

**The engine exports the format it consumes, which is what made the proof possible.**
`--emit-terrain` bakes Phase 1's noise world out as a raster set. That gives the test
suite the same world by two completely different routes — evaluated as noise, and read out
of a file — reachable from a seed rather than from a multi-megabyte fixture nobody dares
regenerate. It also gives anyone wiring up a real generator a reference manifest to
compare theirs against.

**The terrain crosses intact. The history does not, and cannot.** Baking seed 42's
4096-unit world and reading it back:

| Bake resolution | Units per pixel | On disk | Worst height error | Mean | Shoreline disagreement | Events |
|---|---|---|---|---|---|---|
| 128 | 32 | 256 KB | 40.9 m | 0.43 m | 0.12% | 3,874 |
| 256 | 16 | 928 KB | 19.8 m | 0.19 m | 0.05% | 3,489 |
| 512 | 8 | 3.5 MB | 6.2 m | 0.14 m | 0.03% | 3,571 |
| 1024 | 4 | 14 MB | 3.8 m | 0.13 m | 0.02% | 3,253 |
| *noise, direct* | — | — | — | — | — | *3,083* |

At 1024 the reconstructed world is within four metres of the original at its worst point,
a tenth of a metre on average, and agrees about land versus sea at 99.98% of points. It
still produces a different three centuries. Nothing is wrong: a candidate site's score
moves by a fraction, one founding lands a region over, and three hundred years compound
it. The history converges toward the noise world as resolution rises and never arrives.

That result is worth more than a matching one would have been, because it is what forced
the provenance work. **A world file that recorded only the seed and the numeric config
would have claimed those five runs were the same history.** A raster backend's input is a
set of files, and a file path is not the pixels — re-export the map and the same path is a
different world. So `WorldConfig.TerrainSource` carries a content digest of the manifest
and every plane it names, and the config hash folds it in. The five rows above have five
different config hashes.

**It is folded in only when it is set**, which is a deliberate asymmetry rather than a
shortcut. An empty source means the procedural backend, whose inputs are already hashed;
appending it unconditionally would have changed the config hash of every world generated
before Phase 2 existed, and with it the golden fingerprint, for a run whose history had
not changed by one event. That is precisely the reflex the golden test exists to
discourage — regenerate often enough for reasons that are fine and you will regenerate the
one time it was not.

**The sample budget did not move**, which is the quieter half of the result. A history over
rasters spends 5,870–6,098 samples against noise's 5,798, across every resolution tested.
The budget is a property of how the simulation asks its questions rather than of who
answers them — and if swapping the backend had moved it, the three-tier access pattern
would have been measuring the noise sampler rather than the simulation all along.

**What the spike does not prove.** A raster read is an array lookup, so nothing here
exercises the cost model that the tiering exists for; that remains asserted by
`CountingTerrainSampler` against a hypothetical 1–2ms sample. And no external generator has
been driven through the route end to end by a person — the round trip proves the format and
the contract, not that Azgaar's export ranges are what its documentation says. Both are
Phase 2 work rather than spike work, and site selection growing teeth on real terrain
(M10) is the natural place for them.

M10 did the first and **not** the second. The second was finally done on its own, against
WorldEngine — see *The Phase 2 terrain trial* below for what held and the six things that
leaked. The cost model is still asserted against a hypothetical sample, because a raster
read is an array lookup whichever generator wrote the array.

### M10: the ground decides

The score `SiteSelection` used could not describe a site. Across eight seeds its fertility and
height terms spread **0.071** over the 64 candidates of one decision while its river and coast
booleans spread **0.184** — so the choice was made by one flag, quantised to a grid four times
coarser than the choice itself, and a quarter of decisions had no water variation at all to go on.
Nothing asked whether the ground could be built on, so **19.6%** of settlements stood on a grade
steeper than 1-in-2 and the worst stood on 2:1.

**What it produced.** Water became a distance and shelter a weight; confluences, river mouths and
passes became worth something; slope became a quadratic penalty; and region habitability stopped
treating a river through the middle the same as a headwater at the corner.

| | before | after |
|---|---|---|
| settlements on a grade steeper than 1-in-2 | 19.6% | **5.6%** |
| p90 grade under a settlement | 0.779 | **0.437** |
| worst site in eight worlds | 2.081 | 2.038 |
| settlements / cities | 542 / 189 | 555 / 213 |
| terrain samples per run | 5,798 | 8,969 |

Every site now records what it was chosen for, and the rare characters are the ones that make
cities: **64% of harbours, 60% of open-coast sites and 47% of river mouths reach city size, against
34% of unremarkable ground**. That is the milestone's real result — not that towns moved, but that
where a town is now explains how large it got.

#### Where the build disagreed

**A col is not a mountain.** Passes were specified as saddles that are themselves high and steep.
Both conditions are backwards: measured over eight seeds a saddle's median height is 58–344 m
against a 520 m base land height, and its grade is consistently *lower* than the land around it —
which is exactly why anyone crosses there. Under the original gates seed 42 found 87 saddles and
called none of them a pass. What has to be formidable is the barrier either side, not the gap.

**A shore is half-enclosed by definition.** Harbour shelter, counted over the eight touching cells,
put every shore in every world between 0.38 and 0.83 with the bulk within a few hundredths of a
half — because what it actually measures is "this water is beside a shoreline", which is as true of
a headland as of a bay. It needed a three-cell radius and stretching over the range a shore really
occupies. The same trap `Fertility` fell into in M4, in a different costume.

**A linear slope penalty cannot work.** Two linear versions moved the median site grade by
hundredths and left the worst site on 2.8, because a linear response has to choose between ignoring
cliffs and punishing hillsides. Squaring it separates the two: ordinary slope costs almost nothing
and unbuildable ground costs more than any other term returns.

**Defensibility was tried four ways and cut.** It was one of the four ingredients this milestone
was named for, and at the resolution a siting decision can see, prominence and steepness are not
merely correlated — they are the same measurement, so rewarding one is rewarding the other:

| | on 1-in-2 ground | median grade | sites called a spur |
|---|---|---|---|
| no defensibility term | **8.4%** | 0.231 | — |
| rise above the touching ring | 12.8% | 0.262 | 0 |
| rise above the ring beyond it | 14.8% | 0.283 | 16 |
| the same, gated on buildable ground | 13.1% | 0.283 | 7 |

The best case bought a label on 3% of settlements for six points of exactly the defect the milestone
existed to remove. Held to the bar the design set — a term that does not earn its place is cut
rather than kept for flavour — it went. Defensibility as a property of *ground* is what failed;
walls and garrisons remain open to whatever eventually reads `IsFortified`.

**The sample budget moved, and the design said it must not.** It rose 55%, from 5,798 to 8,969 per
run against a 12,000 ceiling. The cause was not the new measures — those are all derived from grids
already paid for, exactly as planned — but expansion's refinement, which was 4×4 where a capital's
is 8×8, on the reasoning that a colony is a smaller bet. At 32-unit spacing the ground a settlement
stands on is not visible: colonies were sited on a grade steeper than 1-in-2 21.1% of the time
against a capital's 10.9%. **Nine in ten settlements in a finished history are colonies**, so this
was never a cheap decision made rarely — it was the decision, made at half the resolution of the one
nobody minded paying for. The bet is smaller; the ground is the same ground.

#### Four tests failed that M10 had not broken

Worth recording, because three of them were passing by luck and one was wrong.

- `DispositionTests` asserts that realms are governed both more and less aggressively than their
  cultures. Divergence is not symmetric — downward is common, upward rare — so at five seeds it was
  asserting a rare event appeared in a sample of about 25 surviving realms. New histories, no
  upward case. Widened to ten seeds; thresholds untouched.
- `DynastyTests` needed a world containing an agnatic realm, and seed 42 stopped producing one. Now
  checked over five worlds, which also tests the invariant harder.
- Two sample-budget comparisons carried tolerances in *samples* that were calibrated when a founding
  cost 16 of them. Restated in foundings.
- `WarTests` compared who won a relic against **who owns the holding town at the end of the run**,
  so it quietly asserted that no relic-winner ever subsequently lost the town. Not a property of the
  relic system, and not true. It now replays region ownership from the chronicle to the year the
  peace was signed — the same discipline the engine already applies to territory, and to the relic
  one line above.

### Trade routes: topology before roads

**Commerce now has an identity of its own.** A route is an undirected pair of settlements with
a founding year, optional closure year, present and peak traffic, economic status, and a preferred
overland, river, or coastal corridor. It is not recomputed into existence whenever a system wants
to move something. Closed routes remain in the world, and a later reopening is a new entity, so a
chronicle can distinguish an old road's memory from the trade that returned a century later.

**The graph is deliberately sparse.** Every five years the engine scores viable settlement pairs
by distance, size, specialization, mercantile culture, political standing, and water access, then
fills a small capacity per endpoint from strongest to weakest. Villages can sustain one link,
towns three, and cities five. The bound is both an economic claim and a presentation constraint:
a route network should reveal hubs and corridors rather than collapse into a complete graph.

**Decline is slower than interruption.** Abandonment and war close a route immediately. Ordinary
weakness has to persist for eight years, so one failed harvest does not erase an established
connection. Prosperity and decline are recorded as transitions rather than annual events; the
entity keeps the continuous traffic measure while the chronicle keeps only the changes worth
reading.

**This is the input to roads, not a substitute for them.** The viewer draws straight endpoint
links and labels them as logical connections. No route stores a polyline or claims a pass, ford,
bridge, or paved segment. A future road network can pathfind the overland routes, choose which
traffic merits construction, share road segments among several routes, and reroute physical
infrastructure without changing the commercial relationship it serves.

Tome circulation now uses active routes instead of inventing a nearby trade centre at the point
of copying, and plague spread gets the same shared traffic network. That is the first payoff of
making routes state: two systems that mean “ordinary travellers moved between these places” now
mean the same thing.

### Physical roads: geometry hung off the topology

The demand the entry above was waiting for exists — tome circulation, plague spread, carrying
capacity and figure travel all consume the route network — so the busiest land routes now have a
way over the ground.

**A road is a fact about a route, not a route of its own.** `TradeRoute.Road` holds a polyline, the
year it was cut, the year it was bridged and paved if it ever was, and its length along the way.
The route's id, founding year, traffic record and chronicle are untouched by anything that happens
to the surface, and an upgrade carries the original `BuiltYear` forward. The alternative —
a `Road` entity with its own id — was rejected because it would give the world two records of the
same relationship that could disagree about which settlements it joins.

**Where is a terrain question, so it lives under `World/`.** `TradeRouteSystem` decides *when* a
link has earned a road, from traffic it already holds; `World/Roads.cs` cuts the path.
`TerrainDisciplineTests` forbids anything under `Systems/` from naming `ITerrainSampler`, and the
split is the same one `SiteSelection` already makes for founding.

**Free, and measured to be free.** The search runs over the 64-unit grid `Hydrology` primes at
world creation, re-read through `TerrainAtlas.SampleGrid`, which memoises. Simulation samples
across seeds 2/7/11/42/99 are 5,414 / 9,494 / 7,334 / 7,574 / 10,214 — **byte-identical to the same
five runs before roads existed**. `RoadTests.CuttingARoadSamplesNoTerrain` pins it: deriving the
planes and cutting twenty roads moves the counter by zero.

**Cost scales with construction, not with years.** A path is computed when a road is built or
upgraded and never again. `TradeRoute.RoadSurveyed` records the one attempt, including a failed
one: two towns can trade across a strait — the route system measures straight-line distance, not
walking distance — and without the flag a pair no road can reach would pay for a graph search every
year it stayed busy. No qualifying pair across the five seeds was actually unreachable, so that is a
guard against a world that can happen rather than a fix for one that did.

**Integers, and a total order on the frontier.** Every toll is an integer, and the priority queue is
keyed on `cost × cells + index` so the minimum is unique. Dijkstra is then reproducible whatever
heap the runtime implements, which matters because the engine compiles for net7.0 and its tests run
on net10.0. A float cost with ties broken by pop order is the classic version of this bug: correct
on the machine it was written on, quietly different elsewhere.

**Prioritised by sustained traffic, and the threshold was measured, not chosen.** Peak traffic over
the five seeds runs min 0.50, median 0.59–0.65, max 0.79–0.89. At `BuildThreshold = 0.68` the
roaded share of *land* routes is 4/13, 42/108, 3/13, 15/53 and 31/130 — 23% to 39%, which draws
trunks and spurs. At 0.62 it is roughly half the network and no corridor stands out from any other;
at 0.76 the sparsest seed keeps one road.

**What the geometry buys, in numbers.** Mean detour over the straight line is 1.00, 1.15, 1.00, 1.15
and 1.17 by seed; over roads longer than 256 units it is 1.10–1.23, with a worst case of 2.17. Mean
ruggedness along a road against the same endpoints' straight line: 0.033 vs 0.040 (seed 42), 0.124
vs 0.152 (99), 0.131 vs 0.135 (7) — the road picks gentler ground by up to 18%. Cut in `River` mode
the path stays nearer the water than the same endpoints cut overland, in **every** seed measured:
mean distance-to-river 98 vs 129 units (42), 83 vs 98 (99), 132 vs 156 (11), and nearer on 46 of 57
individual pairs.

**Seeds 2 and 11 are the honest caveat.** Their roaded routes join towns 128 units apart — two
cells on the hydrology grid — so their paths are straight lines and their detour is exactly 1.000.
Road geometry is known to 64 units because that is the finest grid the world has; a road shorter
than a few cells has nothing to say, and inventing a finer grid for it would be per-decision terrain
sampling the budget cannot afford (a 16-unit corridor refinement for 40 roads is ~14,000 samples
against a 12,000 budget for the whole run).

**Coastal routes were deliberately left without geometry.** A coastal route is sailed, and the
engine has no hulls, no ports beyond access, and no sea lanes; a polyline hugging the shore would be
geometry nothing in the simulation earned. It also keeps "an overland road never crosses water" a
real invariant rather than a special case. The cost is that a maritime seed has commerce with few
roads on it — seed 11 has 48 routes and 3 roads — which is the truthful picture of such a world.

**Rejected: roads feeding back into the model.** Capacity already takes live route traffic, and a
road exists *because* that traffic was high; adding a road term would count the same fact twice.
Armies moving along roads is a standing non-goal. So roads are presentation and record for now, and
the first system to consume one should arrive with a measurement showing the road says something the
traffic does not.

#### Paving: judged on the wrong measure, then fixed on the right one

The paving tier was first held to whether it moved the *line*, and by that measure it barely earned
its place: across five seeds the engineered way differs from the track in only **2** of 18 cases. The
recommendation was to delete it. That was the wrong test. Paving's output is not a polyline, it is a
dated fact in a chronicle — a road that a town has used for three generations being bridged is
history whether or not the course shifts a cell. `RoadGrade` is kept, and the question of whether a
term earns its place is asked of the chronicle as well as of the geometry.

Reading the chronicle instead of the digest found two faults a fingerprint cannot show:

- **Thirteen of nineteen pavings landed the year after the road was cut.** `PeakTraffic` is a
  high-water mark that only rises, so a link crossing `BuildThreshold` had usually crossed
  `PaveThreshold` by the following spring. "Cut in 182, paved in 183" is not a road anyone has lived
  on; it is the same decision made twice. Paving now requires the track to have *stood*.
- **A flat minimum then made the floor the mode** — thirteen gaps of exactly the minimum, nine lines
  all reading "after 40 years of use". So the wait is a reason rather than a constant:
  `PavingWait` scales from the minimum by how mercantile the keener of the two ends is, giving
  culture a second job and the chronicle a spread. Over eight centuries the gaps are now 28, 31, 31,
  36, 38, 38, 38, 42, 43, 44, 44, 45, 46, 65, 72, 86, 94, 136 and 200 years — **15 distinct values
  across 19 pavings**, against a single value covering thirteen of them before.

**The minimum was then set by what a default-length world shows, not by what sounded right.** Forty
years read better in isolation and was worse in practice: at forty, two of the five standard seeds
finished three centuries with no paved road at all. Twenty-five — a generation, about two reigns —
keeps the generational feel and does not make the tier invisible at the run length everything else
is measured at. The measurement also corrected the diagnosis: the binding gate on those two seeds is
not the wait but `PaveThreshold`, which their trade never reaches. Thin-trade worlds not paving
roads is the model being right, not a fault to tune away.

The chronicle that comes out of it reads as a record of use rather than of construction: *"The road
between Mendosene and Caereslinum was bridged and paved after 200 years of use, shortening the way
by 12%."*

**The narration said everything twice.** The build line read "A road was cut between A and B, giving
the A–B route a way over the ground" — the viewer names an unnamed route by its endpoints, so the
sentence carried the same two names twice. Both lines now name the towns once.

**Numbers still to sweep.** `MinimumTrackYears` and `PavingPatienceYears` were set from the five
standard seeds at two run lengths and want a wider sweep, as does `PaveThreshold` — it, not the wait,
is what decides whether a world paves anything at all. Also unswept: whether the pass discount changes which saddle a
road crosses often enough to be worth its own constant, and whether the ford toll should scale with
the river's drainage rather than being flat.

**One limitation, recorded rather than hidden.** Only the current line is stored. A road paved in
year 400 is drawn on its engineered course in year 300 too, because the track it replaced is not
kept. The grade *is* replayed by year, so the viewer never shows a paved road before it was paved,
and the year a road was first cut is what gates it appearing at all.

---

### The Phase 2 terrain trial: WorldEngine end to end

> The full writeup, with every measurement, is *Terrain trial* in the developer docs. This
> records the decisions and the verdict.

M9 built the raster route and tested it against the engine's own baked output, and said so
plainly: the round trip proves the format and the contract, not that a real generator's
export is what its documentation claims. This closes that. **WorldEngine 0.20.0** (MIT —
plate tectonics, erosion and climate) generates a 512x512 world; a script converts its
protobuf into PGM planes and a manifest; a 300-year history runs on it. No engine code
changed and the suite stayed green, which is the acceptance criterion and also the least
interesting part of the result.

**The conversion is a script, deliberately outside everything.** `HistoryEngine` takes no
NuGet dependency, and neither does the docs toolchain: `make terrain-worldengine` runs the
generator through `uv run --no-project --with` in a throwaway environment, and the
converter is a PEP 723 script carrying its own inline dependencies. Nothing about
WorldEngine is committed to this project except a version number in a Makefile. That is
the shape any future adapter should take — the interchange is four files and a manifest,
and the generator stays on its own side of it.

**What held.** The two-piece datum map put WorldEngine's shoreline — 1.00 on its own
unitless −0.40..12.59 scale — at exactly 0 m, which is the one thing the design anticipated
completely. `TerrainCapabilities` declared `Height, Temperature, Rainfall, Lakes` and the
CLI reported three modelled fields, correctly, without anyone checking by hand. The
content digest works on terrain nobody here made: flipping one bit of `height.pgm` moves
the digest, the config hash and the export fingerprint, and deleting the whole set and
regenerating it from the generator's seed reproduces it byte for byte. The sample budget
moved 5%.

**The result that matters is a defect, and it is not in the format.** `Hydrology` has no
depression filling. On WorldEngine's eroded terrain, **26 of 41 river cells are D8 sinks**
— cells with no downhill neighbour — against 15 of 69 on the procedural bake. Flow
accumulation piles into undrained pits, the top-4%-by-drainage rule then names those pits
rivers, and a sink exports no segment: 15 river segments where the reference produces 54.
Two-thirds of this world's rivers are puddles. Phase 1's noise is smooth by construction
and never produced enough sinks to notice; Vintage Story's terrain will not be smooth
either. A priority-flood pass before flow directions is the fix, and it belongs before the
Phase 3 adapter rather than after it.

Compounding it: `Hydrology` reads height at a 64-unit stride from an 8-unit raster — one
pixel in sixty-four — and `ITerrainSampler` is a point query, so there is nowhere to ask
for the mean over a cell. Box-averaging the same data before the D8 pass cuts the sinks
from 35 to 20 and raises the segments from 15 to 21, while barely moving the procedural
world at all. Aliasing costs a band-limited noise field nothing and costs real terrain
half its drainage. This is the trap `TerrainAtlas` guards against in the cost dimension,
turning up in the signal dimension where nothing was watching.

**Four leaks are the manifest being too thin**, and each has a shape:

1. *No ocean mask.* WorldEngine decides ocean by flooding in from the border, so 1,999
   cells here are dry land below its shoreline value. The engine calls anything under 0 m
   ocean and the `water` layer only marks lakes *on land*, so the mask has to be flattened
   into the height and the converter has to choose which way to be wrong. Drowning the
   basins invents inland seas and takes the coastal sites from 14 to 23; filling them
   creates flat plateaus at exactly 0 m that are themselves sinks, 35 against 20.
   `WaterKind` already distinguishes ocean from lake; the format does not.
2. *Units the generator does not have.* Elevation is unitless and temperature is a
   normalised field with quantile thresholds, so the metres and the degrees are invented
   by the conversion — and they matter. The same pixels with `max` at 404 m rather than
   2,920 m produce 45 settlements rather than 55 and 3 coastal sites rather than 14, with
   the river structure untouched, because D8 ranks heights and siting reads metres.
   Nothing tells you that you chose badly.
3. *No rivers.* WorldEngine simulates them and `TerrainCapabilities.Rivers` is unreachable
   through the raster route, so the engine rederived them and only partly agreed: 36.6% of
   the cells it names as river are within 64 units of a WorldEngine river, against 23.4%
   for land cells generally — enriched, not matching. A flow layer in the manifest is the
   cheapest of these fixes and probably the most valuable, because Algernon's Watersheds
   sampler is the identical situation.
4. *No topology.* WorldEngine fades its borders to ocean, so its maps do not wrap.
   `--east-west-periodic` accepted the set anyway and produced a perfectly plausible
   history with 64 active trade routes against 43, several of them crossing a seam that
   does not exist. The manifest should declare topology and the loader should check the
   seam.

**Verdict.** The abstraction held where it was designed to — datum, capabilities,
provenance, budget — on data the engine did not make, and the simulation never knew. What
leaked leaked either from a manifest that carries too little, or from hydrology tuned
against smooth noise. The second is the one to fix first, because it produced a world that
looked entirely reasonable and was quietly wrong, which is the failure mode Phase 3 cannot
afford to discover inside the game.

---

### Depression filling: rivers that leave the map

The Phase 2 trial found it and the trial doc records the measurement; this records the fix
and the four things it moved that were not rivers.

**The defect.** D8 gives a cell its steepest downhill neighbour or nothing at all, and
`Hydrology` had no answer for "nothing at all". That is not a lost cell. Flow accumulates
*into* a sink, so the wettest cells on a map are its pits, and `ClassifyRivers` names the
wettest 4% of the land. On WorldEngine's eroded terrain 26 of 41 river cells were sinks:
two thirds of that world's rivers were puddles. On the engine's own baked noise it was 15
of 69 — bad, but not bad enough that anyone looked in eight milestones. That is the whole
argument for having built the trial: the failure was invisible against a single backend
because the single backend was smooth by construction.

**Priority flood, with an epsilon.** The sea is the outlet; the flood works inward from it,
always from the lowest frontier cell, and raises each cell it reaches to at least the level
the water arrived at. Real relief is untouched — a cell higher than the arriving level keeps
its own elevation — so only the hollows fill. The epsilon is the half of it that is easy to
leave out: filling a basin *flat* trades a sink for a plateau, and every cell on a plateau
has no downhill neighbour either. Raising each cell a micrometre above the one that reached
it leaves a monotone ramp instead, so the network is connected by construction rather than
by luck. Over the longest path a lattice this size can hold, the accumulated tilt is
millimetres — orders below what raster quantisation already costs.

**The filled surface is a drainage construct and does not escape.** Only
`ComputeFlowDirections` and `ComputeAccumulation` see it. Height, submersion, coast, shelter
and coast distance all read the real elevation, so a basin floor is still at its real height
for siting and fertility. There is a test for exactly that, because the tempting version of
this change is to fill `heights` in place.

**Determinism.** `PriorityQueue` promises nothing about equal priorities and a lattice is
mostly ties — a flat sea floor is thousands of them. The comparer orders by level and then
by index, which is a total order, so the flood visits cells in one sequence whatever the
heap does. No extra terrain samples: this is post-processing on a grid already paid for.

| at the 64-unit stride | procedural bake | WorldEngine |
|---|---|---|
| Land cells with no downhill neighbour | 25 → **0** | 35 → **0** |
| River cells that are sinks | 15 → **0** | 26 → **0** |
| Segments exported | 54 → **63** | 15 → **36** |
| Connected components | 18 → **2** | 10 → **6** |
| Largest component | 8 → **50** nodes | 6 → **20** nodes |

The reference world's rivers went from eighteen fragments to essentially one network. Both
figures were reproduced by an independent re-implementation outside the engine before the
change was believed.

**The prefilter is cut, on the measurement that motivated it.** The trial's other hydrology
finding was aliasing: reading height at a 64-unit stride from an 8-unit raster is one pixel
in sixty-four, and box-averaging first halved the sinks. After filling, those sinks are
identically zero, and box-averaging moves what remains from 33.3% to 32.4% agreement with
WorldEngine's own rivers, which is noise. A 3×3 kernel would take hydrology from ~4,225
samples to ~38,000 against a 12,000 budget for the whole simulation. Paying three times the
simulation's entire budget for an effect that no longer measures is the wrong trade.

**What moved that was not a river.** Before the fill, pits were scattered across the land,
so river access was near-ubiquitous: 42% of land on the procedural world sat within 128
units of a "river", and 60% on WorldEngine's. Now it is 28% and 35%. Rivers became a
scarcity, which is what they are supposed to be — and the consequences are real:

- **Fewer settlements.** Seed 42 seats 33 rather than 43 over three centuries. Site scores
  were being inflated everywhere by a river premium that almost everywhere qualified for.
- **Riverside siting falls and coastal siting rises**, which follows from the same thing.
- **M10's premiums are now calibrated against a distribution that no longer exists.** The
  river and coast weights were swept when 42% of land counted as river-adjacent. They want
  re-sweeping; that is recorded as outstanding rather than guessed at here.
- **Roads move**, because `Roadbed` prices a ford. Across five seeds, 85 paved roads: 17
  shorter than the track they replaced (one by 29%), 67 identical, one longer by 1.6%.

**Four tests failed and none of them was this change breaking something.** Worth recording,
because "the change is fine, the tests were wrong" is the most self-serving sentence
available and it needs its evidence attached.

- `GoldenExportTests` — the seed-42 fingerprint. Expected: rivers moved, so the history
  moved. Regenerated deliberately, as its own commit.
- `PavedRoadsAreNeverLongerThanTheirTracks` asserted length where `Roadbed.Cut` minimises
  **cost**. Paving lowers the price of slope and of fording, not of distance, so the
  engineered line may buy a slightly longer route to cheaper ground — one road in 85 did, by
  1.6%. The test now holds the ceiling per world, requires the population to shorten on net,
  and says why an exact bound would be asserting that the cost function is distance.
- Two unrest tests and one dynasty test failed with "this never happened". Measured: a town
  secedes in **8 of 259** consecutive seeds, and before the change the 22-seed sample
  contained exactly **one** occurrence. So they were passing on a single event at a 3% rate —
  a coin flip that any change to world layout would have flipped. The rate is unchanged;
  seeds known to carry the event were added and the rate written down, so the next person can
  tell a resample from a regression.
- `RoadsAreBuiltOnlyForTheLinksThatEarnedThem` asserted a per-world roaded share above 5%,
  and seed 2 is a sea world with six land routes in total. A share over six items has a
  resolution of one sixth. The ceiling — the assertion the test's own comment calls the one
  that matters — stays per world; the floor is now pooled.

---

### The vow binds the priesthood, not the seat

`HouseholdSystem.VowedToCelibacy` asked whether a figure held `OfficeKind.HighPriest`. That
was right while clergy were only ever an office — one person per faith, and the rule reached
all of them. M16 made the priesthood a population through `Occupation.Clergy` and the vow did
not follow, so every ordinary priest was exempt from a rule the faith's own generated
scripture asserts. Measured across six worlds: 20 of 267 clergy in celibate faiths had a
spouse, and the chronicle contained a man taking holy orders and marrying in the same year.

**A vow that fires in one direction is not a vow.** Refusing the marriage of someone already
in orders closed less than half of it. Three doors had to shut, and each was a separate way in:

1. **The marriage.** `VowedToCelibacy` now reads `Occupation.Clergy` against the figure's own
   `ReligionId`, and still reads the seat for a high priest — whose faith may be the realm's
   rather than one they professed.
2. **The ordination.** `Occupations.Choose` zeroes the weight on `Occupation.Clergy` for a
   married figure of a celibate faith, and `Offices.EligibleCleric` refuses them the seat.
   Without the second, an appointment is a back door into orders, because the seat carries the
   occupation with it. The weight is zeroed rather than the option removed, so the roll makes
   the same number of draws for everyone and one married figure cannot shift the careers of
   everyone chosen after them.
3. **The restore.** Holding an office overwrites the career, so a cleric who was crowned reads
   as Court for the length of the reign — long enough to marry without the vow noticing — and
   `Occupations.Sync` was handing the orders back on the way out. That was the last remaining
   violation in the panel, one figure in 214, and it is the whole reason the target was zero
   rather than "nearly none": at 3.6% it looked like acceptable residue, and it was a bug.

**A spouse invented for a wedding now takes their trade after it.** `MatchAtHome` creates a
partner and called `Occupations.Ensure` before `Wed`, so the career was chosen for someone who
was single for two more lines. That was the largest single source once the vow itself was
fixed. An existing partner is untouched — they had a life already.

**Zero, and a counterweight.** Nobody in the panel is now both in orders and married where the
faith forbids it. That number is only worth having next to the one that stops the lazy version
of this fix: barring the married from orders removes people from a pool, and barring too many
satisfies the vow by abolishing the priesthood. Clergy were 11.5% of recorded figures before
and are 11.0% after, and both tests are in `OccupationTests`.

**Two things the issue proposed that measurement did not support**, recorded because not doing
them was a decision:

- *`FigureOrigin.Clergy` implies a hereditary priesthood.* It does not — the enum means "rose
  through a temple", which a celibate faith does as readily as any other.
- *`ClergyAdmission.Bloodline` contradicts celibacy.* It does not, quite. A priestly caste can
  be drawn from particular families whose lay members are the ones who reproduce, which is a
  real arrangement rather than an oversight. It occurs in 4 of 26 celibate faiths and is left
  alone.

**Still open: leaving orders as an event.** The restore rule above is a resignation in
substance — a former ruler who married does not get their priesthood back — but the chronicle
does not say so. *"Left holy orders, and married"* is a line worth writing, and it needs an
event kind and narration rather than a rule change.

---

### The march: a founding that is about somebody else

The third need, and the first that is not a fact about the ground. Land and ore are answers to
what a region has; a frontier post is an answer to who is next to it, and it could not have been
built at M11 because nothing then knew that a particular realm was a particular realm's problem.
M15's wars, truces and grievance are what made the question answerable.

**The threat is read from two signals that cover the same fact from either side.** An active war
is the obvious one; a live truce is the more useful, because a truce exists precisely because a
war has just been settled, which is when frontier posts actually get built. Between them, plus a
twenty-five-year memory on ended wars, they answer "have we lately had trouble with these people"
without a hostility score nothing else would read. The most recent war wins, ties break on the
lower realm id, and nothing draws a number.

**The appetite floor is zero, and that is the difference from ore.** Every realm that becomes a
state plants a first mine if there is ore in reach, because ore is wealth and every crown wants
wealth. A peaceable crown facing a neighbour it has fought answers by *not* building forts, and
the model should let it. So the frontier appetite runs 0.00 to 0.08 against Mercantile — and
`WorldState.ValuesFor` has already folded the fortunes in, so grievance spurs this and weariness
damps it without either being named in the need.

**Two walks, deliberately different.** One measures how near each region lies to the threatening
realm and crosses anything, because distance to somebody is a fact about the map rather than a
route anybody takes. The other gathers candidates outward from this realm's own territory and
crosses only its own ground and nobody's — the rule the ore search already gives, since planting a
post beyond another realm's territory is a claim about borders that expansion is not entitled to
make. A candidate has to be in both; when nothing is, the threat is on the other side of the world
and the caller falls through to the ordinary needs.

Measured over twenty-four seeds:

| | frontier post | everything else |
|---|---|---|
| Share of settlements | 5.3% | mines 11.3%, ordinary 83.4% |
| Regions to the nearest realm that is not theirs | **1** (median) | 3 |
| Region ruggedness | **0.155** | 0.109 |
| Median aggression of the realms holding one | **0.517** | 0.442 |
| Posts in realms that never fought anybody | **0** | — |

#### Both siting terms were built and both were cut

The design above said the need should reach the ground twice, as ore does — once choosing the
region and once scoring the sites inside it. It reaches it once. Two terms were written for
`SiteSelection` and neither survived its own measurement, which is the M10 bar applied to work
done under it.

- **A raised pass bonus (0.45 against 0.12) never fired once.** Across 74 frontier posts, not one
  of the regions a post was sent to contains a pass *anywhere in it*. The design assumed a
  frontier post holds a pass. On this map a march is the settled country between two realms and
  passes are in high broken ground that nobody borders — so the term was a weight on a quantity
  that is identically zero everywhere it was ever asked.
- **A lowered soil weight (0.60 against 1.00) moved the median fertility under a post from 0.8015
  to 0.8026** — a thousandth, in the wrong direction. `SiteSelection` says at the top that within
  one region soil barely varies and everything else does; this is that, measured. It is also why
  the ore need had to move four weights before it was visible at all.

So the need picks the region and the ordinary score picks the spot in it, which is defensible on
its own terms: a garrison still wants the best ground on the march it was sent to hold. The
difference a reader can see — a post a region nearer the border, on rougher country — is entirely
the region search's doing.

**Defensibility is still not a term, and this is the second time that has been the answer.** The
tempting version of a frontier post rewards steep ground, which is exactly the formulation M10
measured four ways and cut for putting settlements on ground nobody could build on. The slope
penalty is therefore at full strength for a march — ore relaxes it, this does not — and
`SiteCharacter.Fortress` stays unfilled. `Strategic` says why the party was sent, not that the
ground is steep.

**The character is the errand, unconditionally**, which is one step further than `Mine` goes.
Mine's claim is about the ground and has to be checked against it: a party sent for ore can arrive
and find none. A post's claim is about why it was sent, which is true by construction. It costs
the "astride the pass" line for a post that holds one — but letting `Pass` win would make the
character mean "sent for the march, unless the march had a pass in it", after which no reader and
no test could count the posts.

---

## Notes for Phase 2

Three routes were listed here, in rough order of expected fit. **The first is built** —
see *Terrain raster interchange* above and *M9* for what it proved. The other two are
still open, and are now alternatives to a working route rather than to nothing:

1. ~~**Raster exports consumed via `ITerrainSampler`**~~ — built in M9. Any generator's
   heightmap and climate planes, via one conversion to PGM. It was indeed the pragmatic
   winner, and the pragmatism was mostly in not binding to a codebase.
2. **Custom pipeline on FastNoiseLite** — C#-native, MIT, no interop. Now worth
   considering mainly for worlds generated *in situ* rather than imported, since the
   raster route already covers import.
3. **WorldEngine-style plate tectonics + climate**, ported or adapted. Its output can
   reach the engine as rasters today, which is an argument for adapting rather than
   porting.

What `ITerrainSampler` needs from any of them: height in metres relative to sea
level (sea level is exactly 0 by contract — no backend defines its own datum),
temperature, rainfall, and an honest `TerrainCapabilities` declaration. Rivers are
*not* required; hydrology derives them. If a backend does supply real rivers, declare
`TerrainCapabilities.Rivers` and hydrology becomes the fallback rather than the only
path.

M9 relaxed one of these in practice: only height is genuinely required. Everything else
is modelled from elevation and latitude when absent, and left out of the declaration —
which is what makes "almost any generator" true rather than aspirational, since almost
none of them export six fields.

~~Phase 2 is also where site selection should grow teeth — river confluences, harbour
quality, mountain passes, defensibility from real slope.~~ Done in M10, three of the four:
confluences, harbour quality and passes all landed, and defensibility was cut on measurement
rather than deferred — see *M10: the ground decides*. It did belong in `SiteSelection`, and being
one function is what made it a single change rather than four.

~~What is left of Phase 2 is the piece that needs a file rather than a decision: driving a real
external generator through the raster route end to end.~~ Done, against WorldEngine — see
*The Phase 2 terrain trial* above. It found one defect worth fixing before Phase 3
(hydrology does not fill depressions) and four places the manifest is too thin to describe
somebody else's map.

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

Build a world on terrain from elsewhere. `--emit-terrain` bakes the noise world out as a
raster set, which is both the round trip the tests use and the reference manifest to
compare a real generator's export against:

```bash
dotnet run --project src/HistoryEngine.Cli -- --seed 42 --emit-terrain build/terrain
dotnet run --project src/HistoryEngine.Cli -- --seed 42 --terrain build/terrain/terrain.json
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
