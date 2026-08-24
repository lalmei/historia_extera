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
| Personal history | Directed bonds, bounded causal memories, wounds, quarrels, and multi-year undertakings |
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
| 2 | PGM rasters plus JSON manifest | Supported; trialled end to end against WorldEngine |
| 3 | Vintage Story world generation | Designed for, not implemented |

Raster content, not its file path, contributes to terrain provenance and the config
hash. Only height is required; absent climate fields are modeled and are not falsely
declared as measured capabilities.

The Phase 2 trial (`docs/dev/terrain-trial.md`) ran an external generator through this
route and found the datum, the capability declaration, the provenance digest and the
sample budget all held on foreign data. It also found hydrology deriving flow over
undrained pits, which cost band-limited noise nothing and cost real eroded terrain most of
its drainage. Flow is now taken from a depression-filled surface, so every land cell
drains to the sea; the real elevation is untouched and only the flow graph reads the
spill surface.

### Time

Time follows the same rule as terrain: buy resolution only where a decision needs it.

- The **year** is the unit of growth, harvest, aging, and slow accumulators.
- The **season** is a standing cadence for systems with a real seasonal rhythm.
- The **day** is a stamp or a scheduled due date. The engine never loops over days.

`Stamp` keeps year and day directly addressable. `Calendar` defines the length of a
year and its seasons. `Docket` holds scheduled work in a deterministic total order and
dispatches due entries to their declared owners. Cost therefore scales with active
episodes, not with calendar length.

Most systems remain annual, `unrest`, `travel` and `cultural-drift` among them. `expansion`,
`war`, and `artifacts` are seasonal in the current checkout. Artifact creation and exchange
still happen only in the opening season; the later ticks settle estates after deaths resolved
from the docket, including a death in the final winter. `plague` still performs its annual
ignition work and also handles scheduled outbreak steps and arrivals, while `war` schedules a
siege's decision instead of resolving an investment on the campaign day that began it.

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
  from ordinary or exceptional causes. Their bonds, formative memories, wounds, and
  undertakings persist as explicit state rather than being inferred from prose.
- Trade routes and holy sites persist after closure or abandonment so the past remains
  visible.
- Wars, battles, artifacts, tomes, outbreaks, and offices are records with provenance,
  not strings embedded in narration.

### Events and export

`HistoryEvent` is a flat fact record: kind, stamp, subject, optional object and location,
plus a small deterministic data map. Narration templates turn those facts into prose.
The facts remain filterable and replayable even when wording changes.

A template may name an entity the event is merely indexed under, by kind — the shrine a
pilgrimage was made to, the faith a priest travelled to preach among. The slot is absent when
the event carries nothing of that kind, so one template can hold several mutually exclusive
clauses and stay grammatical whichever one survives.

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
11. `unrest`
12. `trade-routes`
13. `cultural-drift`
14. `travel`
15. `figure-incidents`
16. `figure-lifecycle`
17. `succession`
18. `houses`
19. `offices`
20. `artifacts`

The order is causal, not merely organizational. Mortality precedes succession; succession
precedes household and office decisions; diplomacy reads the frontier and faiths already
established that step; trade responds to the peace or war that survives campaigning.
`unrest` sits between `war` and `trade-routes` so a year's grievance is felt the year it
is earned and the brigandage it raises suppresses that year's trade; `travel` follows
`trade-routes` so a merchant walks a corridor that is actually open. `cultural-drift` runs
after diplomacy, war, trade and religion because it moves a people's baseline against the
relations, wars and faiths the year has just settled — it is `crown`'s counterpart at the
other end of the year, writing the baseline that next year's `crown` reads first. Changing
the order is a behavior change and must be reviewed as one.

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

Cultures provide a people's founding values, and each realm carries its own baseline that
drifts from them over the centuries: contact and trade pull a people toward its neighbours,
war leaves it warlike, its faith pulls its piety, and an anchor back to the founding culture
keeps convergence from becoming one culture. A ruler's disposition then bends that drifted
baseline within bounded latitude, while realm fortunes carry the recent consequences of war,
calamity, triumph, and grievance. Systems read one settled answer for the current step. The
founding culture itself is never rewritten — it is the seed and the identity, not the state.

Succession follows explicit laws and family relationships. Offices elevate existing
figures from the simulated population instead of inventing disconnected names. Notable
households give those figures family continuity without attempting to simulate every
person in the world.

### Causal lives

The chronicle is the complete record of what happened; a life story is the smaller set of
consequences a person still carries and acts on. The engine records a bounded sample of people,
not every inhabitant, and it does not manufacture drama merely to fill a page. A child who died
young may have a short life. An adult who crossed a realm, lost family, held office, fought, or
plotted should have a readable causal thread rather than a bag of unrelated event labels.

That thread is built from five durable forms of state:

- **Bonds** are directed relationships between two recorded figures. Structural roles such as
  parent/child, patron/client, and mentor/apprentice are reciprocal, while affection, trust,
  obligation, fear, and grievance may differ on each side. A bond retains both the event facts
  that began it and the latest material event that changed it; “rival” without a dispute, person,
  or place is not sufficient provenance.
- **Salient memories** are at most twelve causal experiences: bereavement, injury, triumph,
  defeat, humiliation, gratitude, mentorship, rivalry, ambition, betrayal, marriage,
  parenthood, journeys, and conspiracy. Every memory names the event kind and at least one
  concrete person, place, battle, route, or other entity. Repetition reinforces it; deterministic
  fading and eviction keep the set small enough to mean “what still shapes this person,” not a
  second copy of the chronicle.
- **Feelings** are a present reading of active memories, interpreted through disposition. Grief,
  fear, anger, pride, and loyalty are derived without another random roll and are not permanent
  personality labels. The same defeat may harden an aggressive figure and frighten a cautious
  one. When the memories fade, the feeling fades with them.
- **Battle consequences and undertakings** carry consequences across years. Every named battle
  participant receives one role-sensitive bodily fate from a fork keyed by battle and figure:
  returned unharmed, wounded, or killed. Wounds remember their battle, recovery, and any permanent
  cost; trauma and desertion reduce later service; battle-earned renown can decide a later command
  or marshal's appointment and retains the battle that earned it. An undertaking records an
  objective, sponsor, concrete motive, target, deadline, participants, progress, event-sized steps,
  and a succeeded, failed, abandoned, or active end.

Ordinary events feed that state. Birth and marriage establish kinship; coming of age may establish
mentorship; appointment creates patronage and dismissal may turn gratitude into humiliation or
rivalry. A battle can leave comradeship, rivalry between commanders, pride or defeat, and a wound
that temporarily prevents travel or fighting. A death is indexed on immediate family so the
bereavement memory and its causal obituary appear on the same survivor's page.

Journeys are steps within undertakings, not isolated annual errands. Trade ventures, pilgrimages,
missionary circuits, and embassies begin with a stated objective and may require several trips.
A martial undertaking can likewise begin in a concrete defeat and end only in a later battle
against the same opponent, through revenge, another defeat, loss of the office that enabled it,
death, or its deadline. Only one public undertaking may be current at once; a secret conspiracy is
tracked separately, and a cooldown prevents a terminal arc from immediately becoming a queue of
replacement goals. Steps must be chronological, unique, and linked to real entities. Death, an
invalid destination, exposure, success, failure, or loss of office closes the arc explicitly.

- **Quarrels** are personal disputes between two named people, from a wrong the chronicle already
  recorded to how it ended. Four causes can start one — an office revoked, a succession lost, a
  relative murdered, an accusation laid — and nothing else can; two people sharing a realm is not
  a cause. A quarrel climbs a ladder of public visibility one rung at a time, from a grudge held to
  an insult given, a charge laid, and satisfaction demanded, and it may end at any rung in
  reconciliation, a judgement imposed by someone with standing, a meeting that draws blood, a
  death, or nothing at all when death or distance takes one of the parties first. Both people hold
  the same record and each page reads it from their own side.

Rank decides what a quarrel can become. A subject with a grievance against their own reigning
ruler cannot demand satisfaction of them, so that anger either cools, is judged, or goes where such
things historically went, which is a conspiracy. Duels and battles share one wound model: severity,
recovery, and the permanent tail are the same rule whether the blow was struck on a field or on a
morning appointed for it, and a duel death goes through the ordinary central death path so
succession, offices, and bereavement all follow from it.

Conspiracy is the test that this state matters. A plot is not a one-roll assassination label: a
leader needs motive grounded in grievance, rivalry, memory, disgrace, or a claim; access to the
target affects progress; recruits need trust in the leader, grievance against the target, or their
own access; and every additional participant places pressure on secrecy. The result is a
multi-year undertaking whose discovery or success can be explained from the relationships and
memories already on the people involved.

### Travel

A recorded adult may leave home for a year as one step in a larger undertaking: a merchant along
an open route, a priest to preach or to fetch copies, a pilgrim to a shrine of their own faith, a
courtier as a guest of an ally. The chronicle names the thing the trip was for — the shrine, the
monastery, the faith, the host realm — and not merely the category of it. Trade is the exception,
because for a merchant the destination already is the reason.
Residence does not move — a journey is a trip, and a merchant who vanished from his town every
year he used a route would be missed by the disasters that reach a residence for no reason the
chronicle could defend.

A journey can go wrong, rarely. The hazard rises with the lawlessness of the worse of its two
ends, the distance, and a war at the far end, and falls on a road. Most mishaps are a robbery
the traveller survives, which may still cost them something they owned; the rest are a death
away from home, recorded with the place it happened and the cause. This is the one thing that
makes brigandage cost a person rather than a ledger, and the only way an artifact leaves a
settlement without an army arriving.

### Connection and conflict

Diplomacy is based on reach, contact, memory, faith, kinship, and recent events rather
than a static border table. War records declarations, campaigns, battles, sackings,
territorial settlements, and conclusions.

Trade routes are persistent economic topology. Routes can open, decline, close, and
later be replaced. Trade, tome circulation, carrying capacity, and plague movement
consume the same network.

A minority of that topology has physical geometry. A land route whose *sustained* traffic
crosses a threshold has a road cut for it: a least-cost path over the height, drainage and
pass grids the atlas already holds, computed once when the road is built or upgraded and
stored on the route the way a settlement's coordinate is. The route keeps its identity
throughout — a road is a fact about how a relationship is served, not a relationship of its
own — and outlives the commerce that paid for it. Coastal routes are sailed and carry no
road, because the engine models no ships.

One thing reads a road: how safe it is to travel. A cut track takes less of a journey's
hazard than open country and an engineered road less again, and then the ratio of the road's
length to the straight distance gives some of that back, because a way forced the long way
round is a measurement of how hard the country between the towns is. That is the condition
the geometry had to meet before anything consumed it — it tells the model something the
route's traffic cannot, since traffic says how much is carried and nothing about how far
round the carrying has to go. Capacity still does not read a road, and should not: the
traffic that paid for it is already in the number.

## Viewer boundary

The viewer reads a world export and derives presentation state client-side. It replays
territory and settlement existence for a selected year, then layers maps, timelines,
filters, summaries, and entity histories over the same facts. The overview titles the
history by the world's own designation and keeps the seed next to it, so a list of
exports is recognisable without opening a file and still reproducible from the number.

Development may invoke the CLI to generate a world for the viewer, but that is tooling,
not a runtime architecture. A deployed viewer remains a reader of exported histories.

A figure page leads with **Life at a glance** before family lists, campaigns, travels, and the raw
chronology. It shows the current undertaking, important relationships with their causal reading,
formative memories and present feelings, and wounds carried. These are engine-owned facts selected
and worded for reading; the viewer does not invent emotions or infer relationships from prose.
The chronology remains immediately below as the evidence behind the summary.

## Roadmap

| Milestone | Deliverable | State |
|---|---|---|
| M0–M9 | Engine foundation through raster terrain and viewer depth | Done |
| M10 | Site selection driven by meaningful ground | Done |
| M11 | Offices, appointments, governors, and founding parties | Done |
| M12 | Ruler dispositions and realm fortunes | Done |
| M13 | Seasons, dated events, and scheduled episodes | Done |
| M14 | Notable households and office succession | Done |
| M15 | Grievance made to bite: brigandage, revolt, secession, and usurpation | Done |
| M16 | Lives the chronicle follows: occupations, journeys, and campaign memory | Done |
| M17 | The seed's cosmology (host galaxy and local system), and tomes that draw on it | Done |
| M18 | Cultural drift: a people that changes over the centuries | Done |
| M19 | The road has a cost: journeys that can be robbed, drowned, or fatal, and say what they were for | Done |

M13 landed in independently reviewable stages: the clock and dated record first, seasonal
war and expansion next, then plague travel and outbreak clocks, and finally sieges as
scheduled episodes. The engine now has no declared docket kind without a consumer.

Beyond the numbered milestones:

- Build the Quarry and Harbour founding needs, the two the needs table still lists as unbuilt.
  The frontier post is the pattern to copy and the warning to read: its region search works and
  both of its siting terms were cut on measurement.
- Re-sweep site selection's river and coast premiums. They were calibrated in M10 against a
  river network that was substantially artefact — before depression filling, 42% of land sat
  within 128 units of a "river" and now 28% does — so the weights were set against a scarcity
  that did not exist.
- Widen the terrain manifest where it cannot describe somebody else's map: an ocean mask
  distinct from lakes, a flow layer so a river-aware generator is not rederived worse, and
  a declared east/west topology the loader can check against the seam.
- Let campaign movement or travel *time* consume road geometry as well, now that safety does.
  The same condition applies: it must read something the route's traffic does not already say.
- Let a journey end in staying — a merchant's family relocating to the partner city, a
  missionary who remains among his converts. It is a residence change across a realm boundary,
  so it touches houses, offices and membership, and wants reviewing as migration in its own right.
- Deepen ordinary social life with causal interaction episodes: friendships and lovers that
  actually begin in events, courtship, quarrels, favours, betrayals, and reconciliation. Give
  memories event-specific readable summaries, and widen non-travel undertakings toward masterworks,
  disputed claims, rescues, and religious disputes. These should consume existing bonds,
  memories, access, and disposition rather than add disconnected flavour rolls.
- Build the Phase 3 Vintage Story terrain adapter and revalidate framework, calendar,
  hydrology cost, and map-raster cost against the game version actually targeted.

These are directions, not promises of order. The decision log records the detailed open
questions and the evidence behind each proposal.

## Explicit non-goals

- No daily or hourly global tick.
- No parallel mutation in the simulation loop.
- No per-day weather model.
- No positional armies moving along paths yet.
- No claim that every logical trade route is a physical road: most are not, and a coastal one
  never is.
- No direct Vintage Story types or packages in `HistoryEngine`.
- No real-time coupling to a running game server in the current phases.
- No simulation of every household or every person.
- No requirement that every recorded figure receive a dramatic arc; honest sparse lives are valid.

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
