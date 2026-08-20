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

## Simulation systems

Order and cadence are part of the run's identity (hashed into the export). As built:

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

Most systems are annual, `unrest`, `travel` and `cultural-drift` among them. `expansion` and `war` are seasonal. `plague` also answers for
scheduled outbreak steps and arrivals through the docket, so an active outbreak can keep its own
clock without creating a daily global loop. `war` answers for scheduled siege decisions: the
seasonal campaign starts an investment, and the docket carries it to the day it is carried,
relieved, or overtaken by the season or the peace.

Causal chain: the crown settles the values governing this step → harvest and population →
pestilence and the land taking their share → settlement change → character → pressure → borders
→ faiths across them → opinion of the neighbours by both → the wars that follow → the risings
those wars and hard years leave behind → commerce responding to the resulting peace → who was
on the road that year → exceptional incidents → biological mortality → thrones
filled → households and offices against the line as it now stands → what the survivors made.

`unrest` follows `war` so the grievance a campaign earns can boil over the same year, and
precedes `trade-routes` so the brigandage a rising raises suppresses that year's traffic.
`travel` follows `trade-routes` so a merchant's journey uses a corridor that is actually open.
`cultural-drift` follows all of them because it moves a people's baseline against the relations,
wars and faiths the year has just settled. It is `crown`'s counterpart at the far end of the year:
crown reads the baseline and settles what the realm is governed by, drift writes the baseline for
next year's crown to read.

## Cultural drift

`Culture.Values` is a people's founding seed and never changes. What moves is
`Civilization.BaseValues` — that realm's own baseline, seeded from the culture at founding (or from
the parent realm's current baseline for a breakaway) and stepped once a year by `cultural-drift`.
`crown` bends *that* rather than the culture, so a ruler argues with the people as they now are.

Four terms, no random numbers: contact pulls a realm toward its neighbours' baselines by proximity
and the square root of their population; sustained weariness and grievance pull aggression toward a
war target; a state faith pulls piety toward its fervour; and a weak anchor pulls back toward the
founding culture. Contact is convergent by default and only an active war reverses it — culture
spreads down a border whether or not the neighbours are friendly. The anchor is what stops
convergence ending in one culture; see the decision log for the two calibrations that found both
failure modes.

Diplomacy follows expansion so opinions are formed about the frontier that exists rather
than last year's, and religion precedes diplomacy for the same reason. War precedes
`figure-incidents` and `figure-lifecycle` for the same reason deaths precede `succession`: a ruler killed at a
siege must be dead before the throne is filled, or the realm spends a year vacant for no
reason the chronicle can explain.

Plague and disaster follow `population` rather than preceding it, so a step's growth is
applied before its mortality — the other order lets a town regrow inside the tick that
emptied it. They precede the lifecycle so a settlement gutted this year is judged this
year, which is what lets a plague finish a place.

## Holy sites

`HolySite` represents temples, churches, shrines, monasteries and sanctuaries raised by a
congregation. A site within a settlement carries that settlement id and shares its coordinate. An
independent site carries no settlement id and has a permanent coordinate of its own within the
region. In either case it points to the faith for which it was founded and remains in the entity
table if that faith is forgotten.

The religion system creates one site with every faith and may create more when settlements adopt
it. Independent locations are selected from the exact four-per-axis terrain refinement already
used to site the settlement, keeping terrain work tied to founding decisions rather than yearly
ticks. The form of the house — shrine, temple, church, monastery, sanctuary — is weighted by
the faith's authority and wealth practice, not drawn uniformly. A church is refused by
animistic and pantheistic faiths: the word names a second theology. Dedication is admitted
the same way, so an animism does not raise a house to a saint and a monotheism does not
appease a nature spirit. Offerings follow dietary rules, and the description's remaining
lines are filtered so a dry congregation is not described leaving wine.

Each site carries a description composed once at founding: architectural tradition (from the
culture's naming language, coloured by climate), dedication, fabric, atmosphere, scale, focal
point and offering. Real figures are used when the chronicle has a king, martyr or founder to
name; otherwise the dedicatee is legendary and named in the culture's own tongue. The text is
stored on the entity, like a tome's contents, so later growth of the town cannot rewrite the
church that was raised in a village.

## Faith character

A faith is more than fervour. `FaithCharacter` is rolled once at founding from the culture the
faith arose among and is never revised — a later congregation that believes something else is a
schism. Fervour remains how hard the faith presses outwards. Zealotry, tolerance, schism
proneness and syncretism are the other dials, and they are read by conversion and schism in the
religion tick. Deity structure, authority, clergy admission and wealth practice change holy-site
form, who may hold a temple, and whether a high priest marries. Cosmology, dogma and observance
are what two codices of one religion agree about.

See the [decision log](decision-log.md) for the terms deliberately *not* wired yet (tithes on the harvest, festival
trade, hereditary priesthood as true office succession, tolerance as a diplomatic standing
term). Those belong to other systems; the character stores them so those systems can read a
single vocabulary.

## Trade routes and roads

`TradeRoute` is a persistent, undirected connection between two settlements. It records its
founding and closure, preferred transport (`Overland`, `River`, or `Coastal`), current traffic,
peak traffic, and economic status. Closed routes remain entities, so reopening the same pair
later creates new history rather than rewriting the old route.

The route is **topology**; the road is the geometry hanging off it. River and coastal modes say
both endpoints have that access; an overland route records demand between its endpoints. A land
route whose peak traffic reaches `Roads.BuildThreshold` gains a `Road` — a stored polyline, the
year it was cut, and the year it was bridged and paved if its traffic later reached
`Roads.PaveThreshold`. The route's id, founding and traffic record survive both events, because a
road is a fact about how a relationship is served rather than a relationship of its own.

`TradeRouteSystem` decides *when*; `World/Roads.cs` decides *where*. That split is not tidiness:
finding a path reads terrain, and `TerrainDisciplineTests` fails the build if anything under
`Systems/` so much as names `ITerrainSampler`. The search is Dijkstra over the 64-unit grid
hydrology already primed at world creation, so a road costs **no terrain samples at all** — and it
is run once per construction, never per year, which is what `TradeRoute.RoadSurveyed` protects for
the pairs no road can reach. Every toll is an integer and the frontier is keyed on
`cost × cells + index`, so the minimum is unique and the path does not depend on a heap's
tie-breaking, which no framework guarantees.

Nothing consumes a road yet. Roads are economic geometry: they change no capacity, no travel time
and no campaign. Feeding them back into carrying capacity would double-count the traffic that
built them.

Tome circulation, plague spread and carrying capacity consume active routes. This keeps the
engine's different notions of ordinary travel on one shared network instead of letting each system
invent a fresh distance heuristic.

## Carrying capacity

How many people a settlement can support is the sum of three sourced terms, then modified:

| Term | Source |
|---|---|
| Site | `Specializations.SiteCapacity` — the ore body, the fishery, the spring |
| Land | Squared regional fertility × `FertilityWeight` × **hinterland share** |
| Trade | Live route traffic × `Specializations.ImportReliance` |

Then scaled by the harvest, distance from the seat of government, culture, capital status and
walls.

**The land is contested.** `Hinterland` gives each settlement its share of the ground within reach
— its own pull over the total pull on that ground, where pull is the square root of population.
That is preferential attachment held sublinear: a large place takes more of the fields between
them without extinguishing its neighbour. It is the only mechanism in the model by which a
settlement can be *kept* small rather than killed, and without it the tier ladder reported worlds
in which three quarters of every settlement was a city.

**Cities are made by connection, not by soil.** The land term alone tops out around a small town
on the best ground in the world. Anything larger got there through trade, a capital's
administration, or both — which is where cities historically came from, and what gives the size
distribution a tail instead of a hump. A trading port with no live route to anywhere is a village.

`SettlementHierarchyTests` asserts the resulting distribution across seeds, because the failure
this replaced was a property of a whole world that every per-entity unit test passed through.

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

The world itself is named once from the seed, in the same world-level language as its
regions: a planet ("The planet Borion") or a moon ("The 3rd moon of Endor"). That
designation is flavour — it does not feed any simulation decision — and is unique to the
seed, so stretching a run cannot rename it. The seed still travels in the export beside
it, which is what reproduces the history.

## Serialization

`WorldExporter` produces the viewer JSON and the fingerprint digest. Canonical form is
compact (not `--pretty`). Fingerprints pin simulation behaviour in golden tests.
