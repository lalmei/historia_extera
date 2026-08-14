# Viewer

Astro shell with a React island (`viewer/`). Reads the world JSON the CLI writes.

## Dev server

```bash
make viewer
# same as: npm run dev --prefix viewer
```

Requires Node **≥ 22.12.0** (see `viewer/package.json` `engines`).

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

## Scripts

| Script | Command | Purpose |
|---|---|---|
| `dev` | `astro dev` | Local preview with HMR |
| `build` | `astro build` | Production static build → `viewer/dist/` |
| `preview` | `astro preview` | Serve the production build |

Via Make: `make build` (also builds the .NET solution), `make preview`.

## What it shows

Hash-based routing, one page per entity, and every reference is a link — realm → house
→ ruler → war → battle → city → region → faith → artifact.

The **map** is a terrain canvas with vector overlays, drawn for a selected year rather
than only as the world ended. The slider plays: borders move, towns appear and grow,
battles mark the year they were fought, and the dots can be coloured by realm or by
faith, which are two political maps of the same world and disagree in the interesting
places. Territory is one shape per realm with an outline only where it meets somebody
else.

Everything before the final year is **replayed from the chronicle** — the export carries
only final state, and `TerritoryTests` in the engine is what guarantees the replay
reproduces it.

**Lists** carry faceted filters whose option counts are computed against every filter but
their own, so narrowing to cities tells you how many are known for mining.

## Schema

The viewer pins the export's `schemaVersion` (**5**) and refuses a file it does not
understand rather than misrendering it. Regenerate the world with the matching engine if
it complains.

## Stack

Astro 7, React 19, Tailwind 4. The React app under `viewer/src/app/` owns map and
entity browsing; Astro only hosts the page shell.
