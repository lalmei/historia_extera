# Historia Extera — Design Notes

A Dwarf Fortress-style world history generator for Vintage Story, plus a Legends-mode
viewer. This file is the running decision log: what was chosen, and why, so that a
decision can be revisited on its merits rather than rediscovered.

**Status:** Milestones 0–9 complete. Real naming languages, a settlement lifecycle that
runs its full course rather than only ever growing, rulers who inherit from a family
tree instead of appearing from nowhere, realms that fall to conquest as well as to the
weather, faiths and pestilence that cross the borders those realms draw, a map that
can be scrubbed to any year of the run to watch all of it happen — and, since M9, a
world that can be built on terrain the engine did not generate.

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

> **Planned** for M10. The measurements below are of the world before it landed.

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
siting and shrine capacity) and Tradition (how long a people clings to a dying town). M5
gives three of them a second job in the dynastic systems, where they are far more visible:
Tradition chooses among the monarchical succession laws, Aggression sets how often a
succession is contested and how the loser fares, and Mercantile decides how outward-looking
a people is about marriage — so a trading culture's family tree reaches across the map while
an insular one marries its neighbours.

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
  Poisoning is likelier at restrained courts and direct assassination at aggressive ones, but
  no culprit is named: there is no intrigue evidence model from which to choose one honestly.
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
  the viewer wants its own, themed light and dark, with height/biome/rivers as
  composable layers. The height range ships alongside so metres are recoverable.
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
| M10 | Phase 2 proper: site selection with teeth on real terrain | next |

### As built

Fifteen yearly systems, in order (the order is hashed): `population` → `plague` →
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
