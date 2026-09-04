# Viewer

Astro shell with a React island (`viewer/`). Reads the world JSON the CLI writes.

## macOS app

`macos/HistoriaExteraApp` is a SwiftUI shell around this existing viewer. It launches the
Astro development server on localhost, embeds it in `WKWebView`, and stops the server when
the app exits. The generator remains the C# CLI invoked by the dev middleware, so the native
app and browser workflow write and read the same exports in `viewer/public/worlds/`.

```bash
make install     # once, or whenever viewer dependencies change
make macos-run
```

`make macos-run` is the quick developer build tied to the checkout; it needs Node.js 22.12+,
the .NET 10 SDK and the repository. `make macos-release` instead bundles an official Node
runtime, a self-contained `historia-extera` engine binary, the viewer source and its dependencies.
The release app copies its packaged viewer runtime to a stable, writable user cache and stores worlds in
`~/Library/Application Support/Historia Extera/Worlds`, leaving the signed bundle read-only.
The dependency cache is reused on later launches. Both the zip and the disk image carry the same
bundle. The local release artifacts are ad-hoc signed because the build host has no Developer ID;
notarization remains a separate publication gate.

## Dev server

```bash
make viewer
# same as: npm run dev --prefix viewer
```

Requires Node **≥ 22.12.0** (see `viewer/package.json` `engines`).

## Generating a world from the viewer

Under the dev server, `/` with no `?world=` is the **Worlds Library**: every JSON export
already in `viewer/public/worlds/`, newest first, with seed, simulated years and
civilization count in the row. **Generate new world** opens `/new`, **Initialize Engine**:
a seed (hex or decimal), years, civilization count, a Small/Medium/Large world or an exact
size in units, and whether the map wraps east to west. While a run is in flight, **Initialize
Engine** opens a synthesis overlay with year progress and the CLI log; **Abort synthesis**
cancels the run and returns to the form. The dev server runs the CLI without a second terminal.
The "no world to show" screen links to both, so a fresh checkout can simulate its way out
of an empty `viewer/public/worlds/`. A world already open in the viewer has a **Worlds**
link back to the library.

Runs are one at a time and can be cancelled while in flight. The CLI's own summary is
shown as it arrives, which is what answers "was that seed worth looking at". Each run
writes `worlds/world-s<seed>-y<years>-c<civs>-z<size>.json`; when it finishes, `/new` hands that
path to the viewer through `?world=`. Reloading — or sharing the URL — therefore comes
back to the same history.

The overview titles the history by the world's proper name, with the designation above it.
`--raster` stays at the CLI's default.

The library reads the schema number, world name, seed, years and engine from each
file header. A world written by an older engine still opens, and carries a chip under its name
saying which schema wrote it; a file outside the readable range says so in the same place, and
its name is not a link. The page states the range it accepts above the table, so a greyed row
is never a mystery. Click a row to expand it: designation, size, engine, what the export can
and cannot show, a low-resolution biome map, and the end of the history — standing
civilizations and their populations, settlements, wars, trade and faiths — streamed from the
export without loading the chronicle. The seed stays in its own column so a history can be
recognised at a glance and still reproduced.

**Run** (the triangle) runs that world's settings again, here, through the engine as it stands
now: it asks first, naming the seed, the years, the civilizations and whether the new run
replaces the file or lands beside it, then shows the engine's progress and opens what comes
out. **Regenerate** (the arrows) opens `/new` with the same settings instead, which is where
they can be changed before the engine sees them, or the run continued for more years.
Continuing writes a new file — the shorter history stays on disk — because the engine always
starts from year one, and the same seed is deterministic through the years already simulated.

**Delete** (the bin) asks for confirmation, then permanently removes the export. This cannot
be undone.

**Development only.** An Astro integration (`viewer/dev/world-generator.mjs`) injects the
page and its Vite middleware only for `astro dev`. The page lives outside `src/pages/`, so
`astro build` sees neither its React island nor the endpoint. A built viewer is still a
static bundle with no server behind it.

After editing the integration module itself, stop and restart the dev server. Astro's
config reload can retain the already-imported module even though it notices the config
change.

## Where worlds live

Default CLI output:

```text
viewer/public/worlds/world.json
```

That directory is gitignored — regenerate with `make generate`. To keep more than one
export, give it a distinct name and select it through the `world` query parameter:

```bash
make generate OUT=viewer/public/worlds/custom.json
make viewer
```

Then open `http://localhost:4321/?world=worlds/custom.json`. Relative paths are resolved
from the viewer root; remote URLs also need to permit cross-origin browser requests.

The parameter belongs before the `#`, where it survives navigation — routing is
hash-based, so `/?world=…#/civ:3` opens a specific page of a chosen world. Appending it
to a copied deep link instead (`/#/civ:3?world=…`) is read too, but only until the next
click.

A file dropped into `viewer/public/worlds/` is served straight away, including while the
dev server is running: that directory is handled by the same dev middleware as the
generator, because Vite otherwise serves only what was there when it started.

## Scripts

| Script | Command | Purpose |
|---|---|---|
| `dev` | `astro dev` | Local preview with HMR |
| `build` | `astro build` | Production static build → `viewer/dist/` |
| `preview` | `astro preview` | Serve the production build |

Via Make: `make build` (also builds the .NET solution), `make preview`.

## What it shows

Hash-based routing, one page per entity, and every reference is a link — realm → house
→ ruler → war → battle → city → trade route → region → faith → holy site → artifact.

**Plagues** and **disasters** have dedicated history indexes alongside the faith and artifact
indexes. Religion and artifacts are durable exported entities, so their names open full entity
pages. A plague ceases to be an engine entity when it burns out and a disaster is a single event;
their indexes therefore reconstruct outbreak summaries and disaster rows from the chronicle
without inventing viewer-only ids. Every exported place, realm and region remains a link.

The **map** is a terrain canvas with vector overlays, drawn for a selected year rather
than only as the world ended. Filters, the realm and faith keys, and the year's
chronicle sit in a right-hand inspector. Year playback and zoom stay as floating controls
on the map and remain quiet until hovered. Scroll the map to zoom toward the
cursor; drag to pan when zoomed; `+` / `-` / `0` (or Escape) also work when the map is
focused. The slider plays: borders move, towns appear and grow,
battles mark the year they were fought, and the dots can be coloured by realm or by
faith, which are two political maps of the same world and disagree in the interesting
places. Territory is one shape per realm with an outline only where it meets somebody
else. Trade routes are a separate time-aware overlay: their straight lines show logical
connections rather than the ground between two towns. **Roads** are a second overlay under them,
and are the ground: a solid line following the country for each land route whose traffic held up
long enough to earn one, drawn from the year it was cut, pale and thicker once it was bridged and
paved. Most routes never earn one and a coastal route never does, so the road network is the trunk
of the trade network rather than a copy of it. A road stays on the map after the route it served
has closed.
Independent holy sites appear as diamond markers and can be toggled separately; houses of worship
inside settlements are listed on their settlement and faith pages to avoid hiding the settlement
marker at the same coordinate. Four further overlays sit on the same year: **harbours** place an
anchor in the water a coastal or sheltered site was founded for (a wave for a river landing),
**houses** mark the throne with a banner and the ancestral seat with a house — focusing a realm
shows where that house's living members reside — **walls** ring towns after the year they were
fortified, and **landmarks** mark mines and passes.

Changing political state before the final year is **replayed from the chronicle** — the export
carries only final ownership, and `TerritoryTests` in the engine is what guarantees the replay
reproduces it. Entities with explicit lifespans, including wars and trade routes, are filtered by
their founding and ending years.

**Lists** carry faceted filters whose option counts are computed against every filter but
their own, so narrowing to cities tells you how many are known for mining. Faiths filter
by living/forgotten, root/schism, deity structure and church form.

Figure pages and lists retain both levels of mortality evidence: `deathCause` supplies stable
filters such as plague, disaster and poisoning, while `deathDetail` shows the named outbreak,
specific calamity or form of accident when the engine knows it. Disaster events link the named
court casualties they caused, just as battles already link their commanders.

A figure page opens with a **header** for the person: a mark in their culture's colour whose ring
is the years their life covers set against the years the record covers, the position, house and
culture they are read under, and four facts taken at the selected year — position and where it was
held, household and children, the person standing closest to them, and the inclination running
strongest. The mark is a sigil rather than a portrait on purpose: the simulation never described a
face, and drawing one would be the only claim on the page nothing produced.

Under it comes the **Life arc**: the whole life as one strip, with a bar for every year
the chronicle recorded something — the taller the bar, the more it recorded — and a dot for each of
the turns that made it that life, read beneath as `Born → Marriage → Tribune → First of 7 →
Consul → Loss → Died` with the age at each. Repetition is collapsed rather than listed: eleven
children are one turn dated to the first of them, six re-elections are the first year the office
was reached. Clicking any turn, or anywhere on the strip, moves the year the rest of the page is
read at. Each turn is coloured by what it was about — household, office, arms, trade,
companionship — with a key under the strip, and **Age / Year** switches the whole strip between
counting in years of the life and years of the world. The arc is deliberately the one retrospective
thing on the page — it is the year control, and a control that hides where it can move to is
useless — and says so in its caption.

The year control carries a **standing readout** that changes with it: age and year, then a
sentence saying who they were in that year — position and place, household and children, the person
closest to them and what that bond reads as, the disposition running strongest, and how many
formative memories were still active. The sentence is composed from those fields in a fixed frame
and nothing else: a person with no recorded position gets a shorter sentence rather than a guessed
one, and pronouns come from the recorded sex. Beside it, **at youth / at
height / at death** jump to the three years worth comparing — the year adult life began, the year
they first reached their highest office, and the year they died. Past a person's death these panels
stand at the last year they were in the world rather than reporting a woman six centuries dead as
holding no office and knowing nobody; the readout says which year it is standing at.

**Known for** is what the record can say without composing a sentence: years under arms, offices
held, children raised, close relatives outlived, how long the longest friendship lasted — counted
through the selected year like everything else. Under it, **historical significance** places the
person on an Ordinary–Influential scale derived from offices, command, engagements, treasures,
conspiracies, undertakings, rulers known, descendants, readings of the sky and how often other
people's records name them. The curve is saturating and was tuned against a full run: about three
fifths of a world's people read Ordinary and a handful in several thousand read Influential, which
is what makes finding one mean something.

**Those around them** draws the five to eight people alive in the selected year as lines from the
person: weight is how much the bond carries, solid is kin, dashed a companion, dotted a rivalry.
Dragging the year is the point — a household with six lines at forty has two at seventy, and the
social model becomes something to watch rather than something to reconstruct from the ledger.
Kin outranks a grievance in the line style, since a son somebody fears is still their son.

Once a figure is dead, **After ⟨name⟩** reports what continued because they existed: which children
carried the line and how many children they had in turn, who took the seat they held and in what
year, how much longer a friend carried the friendship, and how many later recorded events their
descendants stood in, two generations down.

**Life at a glance** names the person's current or
last position, upbringing, important relationships, formative memories, wounds, open concerns, and
up to three completed episodes. Episode lines retain their supporting entity links, years, and
event ids. **Friendships** is the record behind the chronicle's lines about them: who the other
person was, what brought the two together, how many years they were known to each other, the rung
the tie reached and the dated acts that got it there, and how it ended. A friendship still standing
when one of them died is an ending like any other and is written as one; only a betrayal is
accented, and it is read from the record's `betrayerId`, so the same fact says "turned on" on one
page and "was turned on by" on the other. The year slider or exact year field turns the page into a
contemporary cut: later bond changes, memories, outcomes, plot revelations, and the rungs and
endings of a friendship disappear. Routine returns along the same route
are compressed without folding in a waylay or loss. The **Chronicle** column beside the page is
the complete raw event ledger through the same selected year, so a year in the ledger and the panel
it explains are on screen together; on a narrower display it falls back below the panels. On a
phone-sized display the world index collapses to its icon rail so the reading column remains
usable.

## Schema

The viewer reads exports from schema **21** to **50**, and refuses anything outside that
range rather than misrendering it. Inside the range, an older export is not a file the viewer
misunderstands — it is a file with less in it: every schema change since 21 has added fields
rather than moved or reinterpreted them, so the loader supplies the containers a later schema
introduced, empty, and leaves absent values absent rather than defaulting them. The Worlds
Library labels such a world with its version and what it predates, and the reading view
banners the same thing once, so an empty panel is never mistaken for a quiet century. A file
below 21, one written by a newer engine than the viewer, or one with no `schemaVersion` at
all, is refused with a message saying which. `viewer/src/app/compat.ts` holds the range and
the normalization; `compat.test.ts` loads every export in `public/worlds/` through the real
loader and derivations, so the floor is a tested claim rather than an intention.

Version 50 lets a marriage be turned on as well as a friendship, and gives a bond a durable mark
saying who turned on whom — the one fact about a betrayal that outlives the memory of it, since a
memory list is twelve long and fades.

Version 49 adds a fifth cause a quarrel can have: a post that went to somebody standing beside
them. It is the only one of the five between two people of comparable standing, and it is far the
commonest, because appointments happen every year while the other four need a crown to act.

Version 48 adds the friendships a person made: the other party, the town or the service that
brought them together, how far the tie was carried, and how it ended — cooled, parted by
distance, ended by a death, or ended by one of them turning on the other. Both people carry
the same record and each page reads it from their own side.

Version 47 adds a figure's military service: the rungs of their realm's army they were raised
to, each with the year, the realm and the name that realm gives the rung — so a soldier's page
shows a career rather than a trade taken at sixteen. Version 46 fills out the local system: every giant gets a face — tilt,
banding, a long-lived storm and, often, rings — along with its own moons, a planet world
gets the moons that cross its nights, the habitable body gets an iron budget its density
agrees with, and the world's spin axis gets a direction inside its galaxy. Version 45 adds
the chronicle line behind a holy site's dedication. Version 44 adds route-derived journey
durations and dated returns.
Version 42 adds a figure's residence history, so a page can tell a move from
a trip and answer where somebody lived in any year. Version 41 adds the hardship memory, carried by people who lived through a
famine, plague, sack or disaster in the town it fell on. Version 40 adds grounded
backgrounds, guardianships and mentorships. Version 39
adds an explicit leader/member/target viewpoint to exported plots and puts a revealed plot on its
target's page. Version 38 adds persistent conspiracies. Version 32 adds the system's comets to
cosmology. Version 31 adds the host galaxy to cosmology — morphology, the observer's
site, and whether the crust can hold iron and ores — which is what the cosmology page
draws face-on and edge-on. Version 30 records how a journey ended — most travellers came home, and the
ones who were robbed or never returned now say so on their own page. Version 28 added journeys and the official and scribe occupations. Version 27 added a figure's campaigns — battles a soldier or general stood in,
wars a sitting ruler led, and sieges endured by anyone living in an invested town. Version 20 added the world's designation — planet or moon, and the proper
names that go with it. Version 15 added what feeds each standing settlement — its carrying capacity
itemised into the site, its share of the surrounding fields and what the roads bring — which
is what the **What supports it** panel reads. Version 14 added a faith's character — gods,
church, clergy, observance and the dials besides fervour. Version 18 added the opening and ending
dates of engagements and the outcome of a siege, so the viewer can distinguish a place carried by
storm from one relieved, lifted, or still invested. A world file older than version 21 will
not load.

## Look

Dark-only. IBM Plex Sans for reading, JetBrains Mono for seeds, years, counts and logs.
Surfaces are layered by tone rather than shadow; hairline borders (`#26282C`) separate
adjacent panels. Primary actions are a desaturated steel blue (`#a6c9f8`). Every page
shares the same top bar: **Worlds** and **Reading**, with a 2px underline on the active
tab. Inside a loaded world, the chronicle index is a collapsible left sidebar (Overview,
Map, Timeline, and the entity lists). Entity pages sit in a 1480px column, wide enough to set
the panels and the chronicle side by side; below 1280px the chronicle drops beneath them. The map
fills the remaining viewport, with year and zoom controls floating on it and filters plus
map keys in a right inspector.

## Stack

Astro 7, React 19, Tailwind 4. The React app under `viewer/src/app/` owns map and
entity browsing; Astro only hosts the page shell.
