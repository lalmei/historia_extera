# Reading a world

The viewer reads a completed Historia Extera export. It can reconstruct earlier political
and biographical states from recorded events, but it never changes the simulated history.

## Worlds Library

The library lists saved JSON exports newest first. Each row shows the world name, seed,
simulated years, size, engine, and schema. Expand a row for a low-resolution biome map and
a summary of the final year.

A row is disabled when its export is outside the schema range the viewer can read. The
current viewer accepts schema 28 through 55. Older readable schemas can lack panels added in
later versions; the viewer leaves those facts absent and labels the export rather than
inventing defaults.

The development server and packaged app can generate and manage worlds. A static viewer
build only reads files already available to it.

## Overview, map, and timeline

**Overview** summarizes the whole export: entity and event counts, leading realms and
houses, large settlements, and the kinds of events recorded.

**Map** shows the world at the selected year. Move the year control to change political
ownership, standing settlements and their tiers, rulers' seats, faiths, active wars and
trade routes, roads, walls, and other dated overlays. The right inspector contains filters,
keys, and that year's chronicle.

Realm coloring and faith coloring answer different questions. A settlement can belong to
one realm while following a faith founded elsewhere.

Trade-route lines are logical connections. A road is a separate stored polyline earned by
sustained overland traffic. Most routes never gain a road, and coastal routes never do.
Roads remain visible after the route that created them closes.

**Timeline** is the exported event ledger. Filter it by event kind and year, then follow a
linked subject, object, or location to its record.

## Entity pages and lists

The left index opens lists and pages for realms, settlements, regions, people, houses, wars,
battles, routes, cultures, faiths, holy sites, artifacts, plagues, and disasters. Links use
typed ids, so a name in a war, journey, succession, dedication, or relationship returns to
the same exported entity.

List filters apply together. Their option counts are calculated against the other active
filters, which makes combinations such as living rulers of one culture or cities known for
mining inspectable without reading the whole chronicle.

Plagues and disasters are reconstructed from events rather than stored as durable entity
tables. Their list pages summarize the recorded outbreaks and calamities without assigning
viewer-only ids.

Artifacts include generated tomes. Their pages show the contents and recorded provenance or
circulation supplied by the simulation. They are not books a player crafts, writes in, or
carries through an active game world.

## Figure pages and historical knowledge

A figure page combines exported facts with the event ledger. Its year control is also a
knowledge boundary: later relationship changes, memories, journey outcomes, plot
revelations, and other future facts disappear when you select an earlier year.

The life arc shows years with recorded events and a small set of major turns. Click or drag
the strip, use the exact-year field, or focus it and use the arrow, Page Up, Page Down, Home,
and End keys. **Age / Year** changes how the scale is labeled. Shortcuts jump to youth,
highest office, and death when those years exist.

The page also reports:

- position, residence, household, children, close relationships, and disposition at the
  selected year;
- formative memories, wounds, concerns, undertakings, quarrels, plots, friendships,
  betrayals, and military service recorded for that life;
- evidence-backed significance and lifetime counts through the selected year;
- consequences after death, shown only when the selected year has reached them;
- the raw chronicle through the same year.

The portrait mark is a sigil. The simulation exports no physical appearance, so the viewer
does not fabricate one.

## Knowledge

The **Knowledge** section reads the claim record: what people in the world said about it, on
what evidence, and what became of the saying. It is separate from Cosmology on purpose.
Cosmology is the objective description of the generated system — the numbers the world was
built from, which nobody in the world knows. Knowledge is the opposite register.

Its overview is a matrix, not a tech tree and not a set of levels. Rows are the fields the
export has claims in; columns say what could settle a claim:

- **Observation** — what was written down, and what was derived from it. The sky can answer it.
- **Practice** — what could actually be done. Answered by the object or the harvest, never by a
  proposition. The engine does not record this yet, so the column does not appear.
- **Doctrine** — what was believed about why. Nothing settles it, and nothing here grades it.

Only rows and columns the export actually populates are shown.

A claim page gives one reading with its claimant, realm, year, register, the sightings it
rests on, the year it predicted if it named one, and the verdict with the year the sky settled
it — including whether the claimant lived to hear the answer. Where the export records them,
it also shows where the reading travelled: each acquisition and loss with the realm and the
carrier that explains it, the works that set it down, and where those were copied — with the
year and the cause where a copy did not survive the town that kept it.

Where the export records it, each realm that held a reading is also shown at the standing it
held it: had it, taught it, argued with it, or set it aside. Having a reading and holding with it
are two facts — a realm can keep a book it shelved for centuries — and neither standing is a mark
for or against the realm or the reading.

A verdict is not a belief. `Refuted` states how a claim stands to measurable reality in a named
year and says nothing about whether anyone stopped holding it, so a refuted reading with two
centuries of copies and patrons reads as exactly that. Doctrine is never graded, and the true
value of anything stays on the Cosmology page — no error is derived, scored, or attached to a
person or a people.

Exports written before schema 53 carry the claims but not their travels. The section says so
rather than showing an empty column, which would read as a world where nothing spread. In one
written before schema 54, every copy ever made is still standing: nothing destroyed a library,
so the only recorded way to lose a reading was for its author to die. Before schema 55 there is no
standing at all: such a file says which realms had a reading and nothing about what they made of
it, and the page says so rather than reading arrival as adoption.

## Cosmology and the night sky

The **Cosmology** page shows a deterministic host galaxy, star, habitable planet or moon,
companion bodies, comets, and deep-time chronology derived from the seed. The civic history
still begins at year 1; it does not begin billions of years ago.

The night-sky panel is an all-sky galactic view. Longitude runs from −180° to +180° and
latitude from +90° to −90°. It combines an analytic galactic glow and dust model with a
deterministic sample of resolved stars.

It is not a view from a settlement's ground:

- there is no local horizon, altitude, or azimuth;
- changing a map location does not change the sky;
- changing the civic year does not create daily or seasonal sky motion;
- there are no named-star or constellation catalogs;
- there is no telescope or player observation interaction.

Comet observations elsewhere in the history are actions of simulated figures. A realm may
miss returns, record an interval covering several returns, or make a claim the later sky
confirms or refutes. These records do not come from clicking the sky panel. The scientific
model and its approximations are documented in [Cosmology and scientific
scope](../dev/cosmology.md).

## Why something may be absent

Before treating an empty result as a rendering fault, check:

- **Year:** the entity may not be founded yet, may be abandoned or ended, or the event may
  not have happened.
- **Filters:** map, list, and timeline filters can hide valid records.
- **Historical knowledge:** a plot, relationship outcome, or death may still be in the
  selected figure's future.
- **Schema:** a readable older export may predate the relevant field. The compatibility
  banner says what is unavailable.
- **Simulation record:** sparse lives are valid. The engine does not manufacture an event
  for every figure in every year.
- **Astronomy:** a realm can fail to record a comet return because no eligible observer did
  so. The sky panel itself is not time- or location-dependent.

## Opening a particular export

In a source checkout, the CLI defaults to:

```text
viewer/public/worlds/world.json
```

Select another export with a query parameter before the hash route:

```text
http://localhost:4321/?world=worlds/custom.json#/civ:3
```

Relative paths resolve from the viewer root. Remote URLs must permit browser cross-origin
requests. Files copied into `viewer/public/worlds/` are picked up immediately by the
development middleware.

The [viewer internals](../dev/viewer.md) page documents static-build behavior, data loading,
historical replay, routing, and the native shell.
