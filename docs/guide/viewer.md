# Viewer

Astro shell with a React island (`viewer/`). Reads the world JSON the CLI writes.

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
file header. The world's proper name is the open link when the schema matches the viewer's
current one; older exports remain visible so it is clear which worlds need to be regenerated.
Click a row to expand it: designation, size, engine, a low-resolution biome map, and the
end of the history — standing civilizations and their populations, settlements, wars, trade
and faiths — streamed from the export without loading the chronicle. The seed stays in its
own column so a history can be recognised at a glance and still reproduced. **Run** and
**Regenerate** fill `/new` with that world's settings so it can be run again with different
parameters, through the current engine, or continued for more years. Continuing writes a new
file — the shorter history stays on disk — because the engine always starts from year one, and
the same seed is deterministic through the years already simulated.

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
than only as the world ended. Filters, the realm and faith legends, and the year's
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

## Schema

The viewer pins the export's `schemaVersion` (**32**) and refuses a file it does not
understand rather than misrendering it. Regenerate the world with the matching engine if
it complains. Version 32 adds the system's comets to cosmology. Version 31 adds the host galaxy to cosmology — morphology, the observer's
site, and whether the crust can hold iron and ores — which is what the cosmology page
draws face-on and edge-on. Version 30 records how a journey ended — most travellers came home, and the
ones who were robbed or never returned now say so on their own page. Version 28 added journeys and the official and scribe occupations. Version 27 added a figure's campaigns — battles a soldier or general stood in,
wars a sitting ruler led, and sieges endured by anyone living in an invested town. Version 20 added the world's designation — planet or moon, and the proper
names that go with it. Version 15 added what feeds each standing settlement — its carrying capacity
itemised into the site, its share of the surrounding fields and what the roads bring — which
is what the **What supports it** panel reads. Version 14 added a faith's character — gods,
church, clergy, observance and the dials besides fervour. Version 18 added the opening and ending
dates of engagements and the outcome of a siege, so the viewer can distinguish a place carried by
storm from one relieved, lifted, or still invested. An older world file will not load.

## Look

Dark-only. IBM Plex Sans for reading, JetBrains Mono for seeds, years, counts and logs.
Surfaces are layered by tone rather than shadow; hairline borders (`#26282C`) separate
adjacent panels. Primary actions are a desaturated steel blue (`#a6c9f8`). Every page
shares the same top bar: **Worlds** and **Reading**, with a 2px underline on the active
tab. Inside a loaded world, the chronicle index is a collapsible left sidebar (Overview,
Map, Timeline, and the entity lists). Entity pages sit in a 720px reading column. The map
fills the remaining viewport, with year and zoom controls floating on it and filters plus
legends in a right inspector.

## Stack

Astro 7, React 19, Tailwind 4. The React app under `viewer/src/app/` owns map and
entity browsing; Astro only hosts the page shell.
