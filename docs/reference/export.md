# World export format

The engine and viewer communicate through one self-contained JSON document. The engine writes
it; the viewer never calls back into the simulation for missing state.

`WorldExporter.FromJson` can deserialize the transfer document. It does not reconstruct a
mutable `WorldState` or resume the simulation. The export also does not contain a complete
generation recipe: retain the original configuration and external terrain for reproduction.

The current engine writes schema 59. The current viewer reads schemas 28 through 59. There is
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
| `narration` | Event templates used to turn facts into linked prose. |

Schema 57 removed the `indices` section, which held denormalized event lookups by entity,
year, and kind. Every entry in it is reconstructible from `events` in one pass, and it grew
with the chronicle. A consumer that wants those lookups builds them on load: bucket each
event's index under its year, its kind, and each of `subject`, `object`, `location` and
`extra`. Measured in the viewer on a 310,746-event world, that pass takes 272 ms against the
3.0 s the file's own `JSON.parse` costs.

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
array indices, so an event's id is also its position — which is what makes an index of
integers into `events` possible for a consumer to build.

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

## Numbers and empty containers

Since schema 57, two things about how the document is written are part of the contract.

**Numbers are written at the precision they are read at.** A double is rounded to three
decimal places, extended below 0.1 until it carries three significant digits — so a
disposition of `0.7269980808848671` is written `0.727`, and a comet mass of `9.66e-11` keeps
its digits rather than becoming zero. No value moves by more than 0.0005, and below 0.1 by no
more than half a percent of itself. Rounding happens in the writer only; the simulation's own
state is untouched, and the values in the file are not what the engine computed with.

**Empty lists and dictionaries are omitted.** A figure who never marched carries no
`campaigns` key rather than an empty array. An absent container means none, exactly as an
empty one did, and every consumer must read it that way — this is the same rule that already
applied to a container an older export predates.

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

The viewer refuses exports below schema 28, above schema 59, or without a numeric schema
version. For readable versions, compatibility code fills missing containers with empty
containers but never invents missing facts. A banner lists the later additions the export
predates.

Filling containers is no longer only an older-file concern. From schema 57 any container can
be absent from any object because it was empty, so a consumer must handle a missing list
everywhere the schema allows one, not only where a later version introduced one.

For another consumer:

1. Check `schemaVersion` before reading the document.
2. Preserve unknown event kinds, series, and fields when possible.
3. Do not interpret an absent field as a default unless the schema contract says so — an
   absent *container* is the one case where it does, and means none.
4. Use `meta.seed`, `configHash`, `systemOrderHash`, and `engineVersion` together when
   comparing provenance.
5. Keep narration optional; structured event facts are the durable record.

Schema changes are additive within the viewer's current compatibility range, but the project
does not publish a separate long-term compatibility guarantee for third-party consumers. Pin
the schema range you test.

## Generation eligibility (schema 62)

`meta.generation` version 1 records `initialCivilizations`, `terrainSource` (the external
terrain content identity, or an empty string for procedural terrain), and an optional
`unsupportedReason`. The seed, year count, extent and topology remain in the existing header.
`meta.configHash` identifies custom configuration; it is not a recoverable configuration.
Only settings representable by the viewer form receive a supported recipe. Unsupported
settings require the original CLI or library invocation, and legacy exports have no recipe.
No reader may infer rerun eligibility or civilization count from a filename.

The numeric JSON seed remains an exact C# `ulong`. The viewer rejects values outside its
safe integer range before building a world or offering a rerun.

## Claim transition causes (schema 63)

`claimTransitions[].cause` explains why that realm acquired or lost the reading, in ordinary
words. It is recorded when the transition is made, not reconstructed from the carrier's
final fate. An absent cause in an older export means unknown; it does not mean that an
author died or a book was destroyed.

Losses distinguish author death and migration, a town leaving its realm or being abandoned,
a destroyed settlement copy, and the original work being lost or moved. The holding tracks
whether its current exemplar is the original or a copy, so a long-destroyed copy cannot
explain the later loss of an original at the same address. A dated loss is considered only
when it has already happened. If several changes coincide between observations, abandonment
and destruction take precedence over a border change; this describes the observed loss and
does not invent ordering within the year. Acquisitions distinguish the author's reading,
the written work, and the availability of a surviving copy. Availability does not assert
that a new copy was made in the acquisition year: a border can move around an old copy.

The claim page, knowledge overview and loss narration use this cause. The explicit
copy-loss test now checks the destruction date for copy-caused losses rather than guessing
causation from a book's eventual fate.

The five-seed 300-year comparison (2, 7, 11, 42, 99) preserved simulation state and all other
export data. Intentional differences were schema, transition causes, loss-event causes and
narration, plus one newly generated continuation in seed 11 that quoted the corrected loss
narration. Existing imported prose is unchanged. The export fingerprint changes; the
pre-existing golden mismatch recorded in the decision log remains unresolved, so the pin
has not been refreshed to accept other branches' changes.

Focused validation passed all 20 claim-transmission, causation, export-roundtrip and
determinism-guard tests. The composed seed-42 fingerprint is now
`7851133b3e7e2c9c1b554e7e32e5d1a1d6999e6f4a123aa9323a7a5c2b160349`;
the committed pin remains `eb7f8fcae207ec3931f07692bef400ca0f1e41989b437e6a862d13696faf3c2a`.

## Figure epithets (schema 64)

`figures[].epithet` is the byname posterity gave the figure at death, for example `"the Pious"`.
It is absent for the living, for most of the dead, and in every export older than 64. It is
earned from the record when the figure dies, never before, so a reader showing a figure at an
earlier year must not show it: the viewer shows it only once the selected year has reached
the death.

Rulers are judged on their record. In order: a fall in battle; murder of the pious; wars won
with none lost, or lost with none won; a crown worn in childhood; a reign of a year or less;
age; a reign of forty years or more; a long reign without war. Temper counts only with a deed
behind it, such as piety with a pilgrimage made, learning with a sky reading or a scribe's
career, or a fierce temper that went to war. A captain who never ruled is named only for three
fields commanded and none lost.

Each byname is announced by an `EpithetEarned` event (kind 352) in the year of the death,
written after the death line. Its data carries `epithet`, `styled` (name and byname together)
and `reason`, a clause saying why. Entity names elsewhere in the export are unchanged: the
byname is not part of `name`, so a data name compared against a figure's name (the role tests
`{as:…}`) still matches.

Data keys added to existing events in the same change, all optional:

| Event | Key | Meaning |
|---|---|---|
| `FigureDied` | `aged` | The age as a register gives it: `in infancy`, `as a child of 6`, `at the age of 50`. `age` keeps the number. |
| `FigureDied` | `reason`, `voice: executed` | Why an execution was carried out. `cause` is then `execution`. |
| `BattleFought` | `loser`, `odds` | The beaten side's name, and `though outnumbered` or `by weight of numbers` when strengths were far apart. |
| `BattleFought`, `SiegeBegan`, `SettlementSacked`, `SettlementOccupied`, `PlagueBegan`, `PlagueSpread` | `season` | The local season on that ground (`spring`, `high summer`, `the depth of winter`, …). Absent in the tropics and on non-four-season calendars. |
| `SettlementPromoted` | `reckoned` | The head count rounded as a chronicler would (`4,000`). `population` keeps the exact figure. |
| `OfficeGranted` | `voice: seated` | The office is over the town it sits in, so the line names the town once. |
| `JourneyMade` | `voice`, `years` | The leg opened or closed an undertaking (`pilgrimage`, `tradeopen`, `embassyclose`, …), and the undertaking writes no line of its own for that leg. |

