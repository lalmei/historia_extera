# Viewer internals

The viewer is an Astro 7 shell containing a React 19 application and Tailwind 4 styles. It
loads one versioned export, derives lookup and replay structures in memory, and renders
hash-routed pages. The engine and viewer share no executable code.

## Loading

`viewer/src/app/store.ts` fetches the selected JSON, checks it through `compat.ts`, and builds
the `World` consumed by views. The store creates maps by typed id, event lookups, the
historical `Timeline`, and other derived structures once per load.

The world URL comes from the `world` query parameter or falls back to the default export.
Keep it before the hash:

```text
/?world=worlds/example.json#/fig:1204
```

Hash routes identify overview, map, timeline, cosmology, entity lists, and individual entity
pages. Typed ids map directly to routes such as `#/civ:3`, `#/fig:1204`, and `#/set:17`.

Remote export URLs are ordinary browser fetches and need appropriate CORS headers.

## Compatibility

`viewer/src/app/types.ts` mirrors the current C# export by hand. `SCHEMA_VERSION` is 51.
`compat.ts` accepts schema 21 through 51 and normalizes additive missing containers for older
files. It never fabricates missing scalar facts.

`compat.test.ts` loads retained sample exports through the real loader and derivations. Raising
the minimum readable schema is a test-coverage decision, not an inference that every
intermediate shape probably works.

When the engine adds a field, update:

- `WorldExport.cs` and `WorldExporter.cs`;
- `types.ts`;
- the schema constant and `ADDED_IN` description;
- compatibility normalization if an older file lacks a new container;
- views, tests, and the export reference.

## Historical replay

The export stores final entity state plus the event ledger. `timeline.ts` converts relevant
events into “from this year onward” spans for region ownership, settlement tier, capital,
ruler, and settlement faith. Binary search answers state at a selected year.

This avoids storing one snapshot per entity per year. It also means a new historically
changing field needs an event that fully describes each transition. Adding only a final-state
property will make past years wrong even if the final page looks correct.

`TerritoryTests` in the engine compare event replay with the exported final map. Viewer tests
cover the TypeScript reconstruction and compatibility path.

Figure pages have a separate historical-knowledge layer in `biography.ts`. It filters
relationships, memories, outcomes, and plot visibility through the selected year. The life
arc remains retrospective because it is the year navigation control; its caption states that
exception.

## Maps and rendering

The map consumes the exported raster and vector rivers, then overlays event-replayed
territory and dated entities. East/west-periodic metadata controls seam-aware lines and
geometry. Logical trade routes and physical road polylines remain separate layers.

The cosmology view consumes `world.cosmology`. Galaxy diagrams, system strips, bodies, and
the night-sky texture are presentation derived from exported parameters. `nightSky.ts`
renders an all-sky galactic map. It does not use `CelestialOrientation`, map location, or the
selected civic year.

The viewer should not infer facts the export did not state. Examples include physical
appearance for figures, a durable id for a disaster, local sky coordinates, and causes not
present in an event. A missing fact should produce a shorter description or a compatibility
notice.

## Development server

`viewer/dev/world-generator.mjs` is an Astro integration loaded only in development mode. It
adds:

- the `/new` page;
- endpoints for starting, polling, and cancelling one CLI run at a time;
- saved-world listing, header reads, streamed previews, and file management;
- no-store responses where an overwritten export would otherwise remain cached.

The generated page lives outside `viewer/src/pages`, so `astro build` excludes it and its
server endpoints. A static build can browse reachable exports but cannot generate or manage
local files.

Restart Astro after changing the integration module itself. Config reload can retain an
already-imported copy.

## Native macOS shell

`macos/HistoriaExteraApp` is a SwiftUI process supervisor around the same Astro development
server. `ViewerServer.swift` finds the runtime, uses local port 4321, starts Astro, waits for
health, and loads it in `WKWebView`. Requests bypass ordinary web caching so regenerated worlds
are visible.

The developer app built by `make macos-run` uses the checkout and installed Node/.NET tools.
The release package contains Node, a self-contained CLI, viewer source, and dependencies. At
first launch it copies the read-only bundled runtime to a versioned cache path without spaces;
worlds are kept separately in Application Support.

Closing the app stops the local server. Swift owns process lifetime only; all generation still
passes through the C# CLI and all history rendering stays in React.

## Adding a view or entity route

Start with the export contract, not the component. Confirm whether the page needs final state,
event replay, yearly series, or a new exported fact. Then:

1. add or verify the TypeScript type and id prefix;
2. add store lookup or replay derivation;
3. register the route and navigation entry;
4. render references through the shared entity-link path;
5. define older-schema behavior;
6. add focused tests and run TypeScript/Astro checks.

Keep engine facts out of viewer-only prose when they need to remain filterable, comparable, or
available to another consumer. Those belong in the export.
