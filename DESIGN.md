# Historia Extera — Design Notes

A Dwarf Fortress-style world history generator for Vintage Story, plus a Legends-mode
viewer. This file is the running decision log: what was chosen, and why, so that a
decision can be revisited on its merits rather than rediscovered.

**Status:** Milestones 0–6 complete. Real naming languages, a settlement lifecycle that
runs its full course rather than only ever growing, rulers who inherit from a family
tree instead of appearing from nowhere, and realms that fall to conquest as well as to
the weather.

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
  misrendering a file it does not understand. **v3** added wars, battles, and the
  relations, alliances and truces on a civilization; v2 added dynasties and family links.
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
| M7 | Viewer depth: territory rendering, richer filters | next |
| M8 | Flavour: religions, artifacts, plagues, disasters | |
| M9 | Phase 2 spike: raster-backed `ITerrainSampler` | |

### As built

Nine yearly systems, in order (the order is hashed): `population` →
`settlement-lifecycle` → `specialization` → `expansion` → `diplomacy` → `war` →
`figure-lifecycle` → `succession` → `houses`. Reads as a causal chain: populations change
against the harvest, that changes what settlements are, a settlement that has outgrown a
hamlet acquires a character, pressure drives expansion, expansion moves borders,
neighbours judge each other across the borders as they now stand, the wars that follow are
fought, people die — of age, of illness and of wounds alike — thrones emptied by those
deaths are filled, and the houses go on, marrying and bearing children against the line as
it now stands.

Diplomacy follows expansion so an opinion is formed about the frontier that exists rather
than last year's, and war follows diplomacy so a war declared this spring is fought this
summer. The last three remain the tightest coupling in the list, and war now leans on the
same property: deaths must precede succession or a realm spends a year without a ruler for
no reason the chronicle can explain — as true of a king killed at a siege as of one who
died in bed — and succession must precede the houses or a new king's brothers are still
ranked as heirs on the day he is crowned, and marry accordingly.

Measured on seed 42, 300 years, 8 civilizations, 4096-unit world:

| | M1 | M4 | M5 | M6 |
|---|---|---|---|---|
| Wall clock | ~65 ms | ~67 ms | ~215 ms | ~250 ms |
| Events | 359 | 950 | 3,299 | 3,216 |
| Settlements | 96 | 96 (15 cities), 1 abandoned | 96 (15 cities), 1 abandoned | 91 (15 cities), 1 abandoned |
| Figures | 81 | 81 | 1,072 | 1,033 |
| Houses | — | — | 16 (8 standing, 8 died out) | 15 (6 standing, 9 died out) |
| Wars / battles | — | — | — | 10 / 38 |
| Civilizations fallen | 0 | 0 | 0 | 2 |
| Simulation samples | 6,050 | 6,050 | 6,050 | 5,990 |
| Export size | 0.73 MB | 0.73 MB | 1.36 MB | 1.36 MB |
| Tests | 100 | 100 | 114 | 129 |

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
Heraanes on seed 42 rolls Aggression 0.97, declares eight of the world's ten wars,
extinguishes two realms and takes six provinces across three centuries. That is not a
runaway to tune out — it is the trait doing exactly what it is for, and it is the most
legible thing in the export.

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
