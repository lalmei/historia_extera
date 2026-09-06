# World export format

The engine and viewer communicate through one self-contained JSON document. The engine writes
it; the viewer never calls back into the simulation for missing state.

`WorldExporter.FromJson` can deserialize the transfer document. It does not reconstruct a
mutable `WorldState` or resume the simulation. The export also does not contain a complete
generation recipe: retain the original configuration and external terrain for reproduction.

The current engine writes schema 52. The current viewer reads schemas 21 through 52. There is
no checked-in JSON Schema file: the authoritative writer shape is
`src/HistoryEngine/Serialization/WorldExport.cs`, and the matching consumer shape is
`viewer/src/app/types.ts` plus `viewer/src/app/compat.ts`.

## Top-level document

| Field | Contents |
|---|---|
| `schemaVersion` | Integer contract version for the JSON shape. |
| `meta` | Seed, config and system-order identities, engine and narration versions, year range, event count, and terrain-sampling costs. |
| `world` | Name, cosmology, bounds, topology, terrain capabilities, map raster, and rivers. |
| `regions`, `cultures` | Static geography and cultural records. |
| `civilizations`, `dynasties`, `settlements` | Political, family, and settlement state at the end of the run, with dated fields where supplied. |
| `tradeRoutes`, `figures`, `wars`, `battles` | Durable entities and their recorded histories. |
| `religions`, `holySites`, `artifacts` | Religious and material records. |
| `events` | Flat chronological facts carrying typed references and optional structured data. |
| `series` | One value per year for changing metrics. |
| `indices` | Denormalized event lookups by entity, year, and kind. |
| `narration` | Event templates used to turn facts into linked prose. |

Plagues and disasters do not have top-level entity arrays. The viewer reconstructs their
list entries from events. Consumers must not invent durable ids for them.

The terrain timing fields are estimates, calculated from sample counts at a fixed 1.5 ms per
sample. They are planning figures for the future game adapter, not measured runtime in a
current Vintage Story integration.

## Entity references

References serialize as a short kind and numeric index, for example `civ:3`, `fig:1204`, or
`set:17`. The prefixes currently used are:

| Prefix | Kind |
|---|---|
| `cul` | culture |
| `civ` | civilization |
| `set` | settlement |
| `fig` | figure |
| `dyn` | dynasty or house |
| `war` | war |
| `bat` | battle |
| `reg` | region |
| `art` | artifact |
| `rel` | religion |
| `rte` | trade route |
| `hol` | holy site |

Treat the complete string as the key. Numeric indices are only meaningful inside their kind.

## Events and historical replay

Each event contains an integer id, civic year, day within the year, kind, significance, and
optional `subject`, `object`, `location`, `extra`, and string-valued `data`. `Notable` events
form the default narrative spine; `Routine` events remain available on entity pages and in
the full record.

The event list is chronological. Event ids are assigned in append order and equal their
array indices. `indices.eventsByEntity` and `indices.eventsByYear` contain indices into that
same array, not copies of event objects.

Most top-level entity records describe final state. The viewer replays ownership,
settlement-tier, capital, reign, and faith-change events to answer earlier-year questions.
`TerritoryTests` verifies that replaying the event log reaches the exported final political
map. A third-party consumer that displays earlier state needs equivalent replay logic or must
label final-state fields honestly.

## Series

A series names an entity, metric, chart group, unit, first year, and value array. Values are
rounded to three decimals. Consumers can plot an unknown metric from its group and unit
without hard-coding its name. The first value belongs to `fromYear`; it does not necessarily
start with the world's first year.

## Narration

Narration templates ship with the export so new event kinds do not require a second prose
switch in the viewer. The template language has its own `narrationSyntaxVersion`.

Templates can resolve subject, object, location, typed extras, per-event data, and a figure
page's current subject. Square-bracketed segments are optional and disappear if a required
placeholder is absent. Consumers that do not implement this grammar should render structured
event facts instead of partially substituting templates.

## Canonical JSON and fingerprints

`WorldExporter.ToJson` writes compact JSON by default. `--pretty` changes whitespace, so it
is not the byte-canonical form.

`WorldExporter.Fingerprint` hashes canonical JSON after clearing the engine version, schema
version, and narration syntax version. Those values describe the file contract rather than
the simulated history. Adding exported facts still changes the fingerprint even if the
underlying system decisions do not.

The export deliberately has no generation timestamp. Identical inputs can therefore produce
byte-identical canonical output.

## Compatibility rules

The viewer refuses exports below schema 21, above schema 52, or without a numeric schema
version. For readable older versions, compatibility code fills missing containers with empty
containers but never invents missing facts. A banner lists the later additions the export
predates.

For another consumer:

1. Check `schemaVersion` before reading the document.
2. Preserve unknown event kinds, series, and fields when possible.
3. Do not interpret an absent field as a default unless the schema contract says so.
4. Use `meta.seed`, `configHash`, `systemOrderHash`, and `engineVersion` together when
   comparing provenance.
5. Keep narration optional; structured event facts are the durable record.

Schema changes are additive within the viewer's current compatibility range, but the project
does not publish a separate long-term compatibility guarantee for third-party consumers. Pin
the schema range you test.
