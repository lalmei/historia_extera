# Phase 2 terrain trial: a generator that never heard of this engine

Phase 2's raster route was built and round-trip tested against the engine's own baked
output, which proves the format is self-consistent and nothing else. This is the trial it
was actually for: a world built on terrain from an external generator, to separate what
the abstraction genuinely gets right from what it only gets right because Phase 1's noise
sampler wrote both sides of the conversation.

The headline: **it works, and the interesting part is the list of things it got away
with.** A 300-year history runs on foreign terrain with no engine code changes and the
full suite green. Six assumptions leaked, and one of them — hydrology has no depression
filling — is a real defect that eight milestones of procedural terrain never exposed.

## What was run

| | |
|---|---|
| Generator | [WorldEngine](https://github.com/Mindwerks/worldengine) 0.20.0 (MIT) — plate tectonics, erosion, climate |
| World | seed 4242, 512×512, 10 plates, `-t full` |
| Conversion | `tools/terrain/worldengine_to_raster.py` — PGM planes + `terrain.json` |
| Engine run | seed 42, 300 years, 8 civilizations, map raster 256 |
| Reference | `make terrain-bake TERRAIN_RES=512` on the same engine seed, same stride |

WorldEngine is not a dependency of anything. `make terrain-worldengine` runs it through
`uv run --no-project --with`, in a throwaway environment, and the converter is a
PEP 723 script with its own inline dependencies. `HistoryEngine` gained no package, and
the only thing crossing the boundary is four PGM files and a manifest.

```bash
make terrain-worldengine
make terrain-generate TERRAIN=build/terrain-worldengine
```

## What held

**No engine code changed, and nothing was tempted to.** The 347-test suite passes
unmodified, including `TerrainDisciplineTests`. The external world spends 7,934 simulation
samples against the 12,000 budget, 5% above the procedural reference's 7,574. Sampling
still scales with decisions rather than with years: the difference is the extra
settlements this world seats, not a change in access pattern.

**The datum survived contact.** WorldEngine's elevation is unitless and runs
−0.40 to 12.59 with its shoreline at 1.00; normalised, that puts sea level at 0.063915.
The two-piece map pinned it to exactly 0 m, and `IsSubmerged`, the ocean test and every
fertility calculation downstream meant in Phase 2 what they meant in Phase 1. This is the
one place the design anticipated the problem correctly and completely.

**`TerrainCapabilities` did its job.** The set declares `Height, Temperature, Rainfall,
Lakes` and the CLI reports `GeologicActivity, ForestDensity, ShrubDensity` as modelled.
That flag set was written in M1 against a fear that could not be tested until there was a
backend with a partial hand; this is the first one that came from outside, and the split
it printed was correct without anyone checking it by hand.

**Provenance is real, not decorative.** The raster set digests to `2b9dc53c66593848` and
the world's config hash to `1591b0a05f41c087`. Deleting the whole set and regenerating it
from the generator's seed reproduces the digest byte for byte. Flipping **one bit** in
`height.pgm` moves the digest to `80d1ff7ffec77d73`, the config hash to
`6f73283a253d8ded`, and the export fingerprint from `4418b6f7…` to `d87ff0a6…`. The
determinism contract covers the terrain, on terrain the engine did not make.

**PGM was the right unglamorous choice.** The converter writes the format in four lines of
numpy (`>u2`, big-endian, a five-token header) with no imaging library at all, and the
engine read it first time. There was no negotiation, no version, and nothing to debug.

## What leaked

### 1. The interchange cannot say "this is land, and it is below sea level"

WorldEngine decides what is ocean by flooding inward from the map border, so a closed
basin below its shoreline value is *dry land* in its model — 1,999 cells here, 0.76% of
the map and 3.8% of all land. `RasterTerrainSampler` has no way to hear that: it calls
anything below zero metres ocean, and the optional `water` layer only marks **lakes on
land**. A generator's ocean mask has to be flattened into the height plane, and the
converter has to pick which way to lie.

Both lies were measured. `--basins fill` raises the basins to the shoreline; `--basins
drown` lets them read as sea:

| | filled | drowned |
|---|---|---|
| Settlements recorded | 55 | 62 |
| Coastal sites | 14 | 23 |
| Harbour sites | 8 | 18 |
| River segments | 15 | 26 |
| Land cells that are D8 sinks | 35 | 20 |

Neither is right. Drowning invents inland seas and doubles the coastline; filling creates
flat plateaus at exactly 0 m, which are sinks with no downhill neighbour and which feed
directly into leak 3.

**For Phase 3:** an explicit water/ocean plane in the manifest — `WaterKind` already
distinguishes ocean from lake, and the format simply does not carry the distinction.
Vintage Story has the same shape of problem: its sea level is a world-generation setting
and its lakes are not derived from it.

### 2. The manifest demands physical units the generator does not have

WorldEngine's elevation is unitless and its temperature is a normalised 0..1 field whose
biome thresholds are quantiles, not degrees. The manifest requires metres and Celsius —
correctly, since a raster carries no units and `min`/`max` are the only place they can
live. So the conversion has to invent both, and there is no feedback anywhere telling you
that you invented them badly.

The same pixels, with only `max` changed from 2,920 m to 404 m:

| | peak 2,920 m | peak 404 m |
|---|---|---|
| Settlements recorded | 55 | 45 |
| Coastal / Riverside / Pass | 14 / 2 / 10 | 3 / 5 / 10 |
| Events | 9,251 | 7,475 |
| River segments | 15 | 15 |

River structure is untouched — D8 flow ranks heights and ranking is invariant under a
monotone rescale — but siting, which reads absolute metres, moves substantially. Note also
that "match the procedural sampler" is ambiguous in a way that bit: its *nominal* ceiling
is 2,920 m (`BaseLandHeight + RidgeHeight`) but seed 42's *realised* relief is
−318 m to 404 m. Choosing the nominal figure made the external world roughly seven times
more mountainous than any procedural world of the same seed, and nothing said so.

**For Phase 3:** the vertical scale is a first-class input, not a detail of the export.
It belongs in whatever a Phase 3 world records about how it was built, and it deserves the
same "same seed, same world" scrutiny the config hash gets.

### 3. `Hydrology` has no depression filling, and eight milestones of noise hid it

This is the one that matters. D8 flow accumulation on foreign terrain piles water into
undrained pits, and the cells with the most accumulated drainage — which is exactly how
`ClassifyRivers` picks the top 4% — turn out to be the pits themselves.

| at the 64-unit hydrology stride | procedural bake | WorldEngine |
|---|---|---|
| Land cells | 1,536 (37.5%) | 806 (19.7%) |
| Land cells with no downhill neighbour | 25 (1.6%) | 35 (4.3%) |
| River cells | 69 | 41 |
| …of those, sinks | 15 (22%) | **26 (63%)** |
| Segments exported | 54 | 15 |
| Connected components | 18 | 10 |

Two-thirds of the rivers the engine names on this terrain are the bottoms of closed
basins. They export no segment, because a sink has no downstream link — which is why the
network is not merely fragmented but sparse: 15 segments where the reference gets 54.

The mechanism was confirmed by reimplementing `ComputeFlowDirections` /
`ComputeAccumulation` / `ClassifyRivers` outside the engine; it reproduces 54, 15 and 26
segments exactly for the three raster sets, so this is the code's behaviour and not an
artefact of the comparison.

Phase 1's noise never produced enough sinks to notice, because it is smooth by
construction. Real eroded terrain is not, and Vintage Story's will not be either.

**For Phase 3:** priority-flood depression filling before flow directions, or a sink
resolution pass that routes pits to their spill point. Until then, the acceptance question
"are there disconnected fragments at this stride" has the honest answer *yes, on both
backends* — the reference fragments into 18 components too. The external terrain is not a
regression in kind; it yields a third of the segments, for a reason that is now identified.

### 4. A point-query contract has nowhere to say "average over this cell"

`Hydrology` reads height at a 64-unit stride from a raster whose pixels are 8 units — one
pixel in sixty-four, chosen by where the sample happened to land. `ITerrainSampler` is a
point query by design, so there is no way to ask for the mean over a cell, and
`RasterTerrainSampler`'s bilinear read does not help: it interpolates between neighbouring
pixels, it does not integrate over the stride.

Box-averaging the height over each hydrology cell before the D8 pass, same data otherwise:

| | procedural bake | WorldEngine |
|---|---|---|
| Land sinks, point-sampled → box-averaged | 25 → 17 | 35 → **20** |
| River cells that are sinks | 15 → 12 | 26 → **14** |
| Segments exported | 54 → 49 | 15 → **21** |

Aliasing costs the procedural world almost nothing — its ridge scale is 1,600 units, so it
has no energy at the pixel scale to alias — and costs the external world nearly half its
sinks. This is the same trap `TerrainAtlas` exists to guard against in the cost dimension,
appearing in the signal dimension where nothing was watching.

**For Phase 3:** Vintage Story's sampler is a point query too, and its terrain has
metre-scale structure. Hydrology should either sample at its own stride *and* prefilter, or
sample a small kernel per cell and pay for it — a decision worth making on a measurement
rather than inheriting.

### 5. The format cannot carry rivers, so a generator that has them gets them thrown away

WorldEngine simulates rivers: 1,050 land cells at or above its own "river" watermap
threshold. The manifest has no river layer, and `TerrainCapabilities.Rivers` is
consequently unreachable through the raster route — the design says a backend supplying
real rivers should declare it and demote hydrology to a fallback, and no raster set can.

So the engine rederived them, and only partly agreed. Measured over the 41 cells the engine
names as river, against every land cell on the same lattice as the baseline:

| Within | Derived river cells near a WorldEngine river | Any land cell |
|---|---|---|
| ±32 units | 26.8% | 15.6% |
| ±64 units | 36.6% | 23.4% |
| ±128 units | 46.3% | 35.0% |

Better than chance — about 1.5× enriched — and a long way short of agreement. Some of that
gap is legitimate and by design: `Hydrology`'s own docs say it locates river *valleys*
rather than channels, and WorldEngine's channels are carved by erosion at pixel scale, so
exact coincidence was never the target. Leaks 3 and 4 account for much of the rest. But the
interchange still threw away a measured answer in order to compute a worse one.

**For Phase 3:** a `river` layer in the manifest, read as accumulated flow rather than a
boolean, would let `Hydrology` seed or replace its accumulation from measured data. This is
the cheapest of these fixes and probably the most valuable, since Algernon's Watersheds
sampler is exactly this situation again.

### 6. Map topology is a claim the operator makes and the engine cannot check

WorldEngine fades all four map borders to ocean, so its worlds are flat maps and do not
wrap. `--east-west-periodic` accepted the set anyway and produced an entirely plausible
history — 59 active settlements against 48, and 64 active trade routes against 43, because
coastal routes now cross the seam over open water that is on the far side of the world.
Nothing in the manifest says whether a map is a cylinder, and nothing in the engine looks
at whether the east and west columns agree.

**For Phase 3:** the manifest should declare its own topology, and the loader should
reject a periodic claim whose seam does not match. Vintage Story worlds do not wrap either,
so the flag will be wrong there by default rather than by accident.

## Also noted, without a measurement

- **The format describes square worlds only.** The manifest carries one `worldSize`.
  WorldEngine's natural planetary aspect is 2:1 equirectangular, so a square export is a
  deliberate misuse of it; the converter rejects non-square worlds rather than stretching
  them. Any real GIS source will be worse — it will have a projection.
- **Latitude is an assumption, not a field.** `SynthesiseTemperature` and
  `SynthesiseRainfall` read `v` as latitude from pole to pole, which for a 4,096-unit
  square world is a fiction the noise sampler shares and never has to defend. Supplying
  real temperature and rainfall planes sidesteps it here; a heightmap-only foreign set
  would inherit it silently.
- **Lakes came back empty and that was correct.** WorldEngine found 1 lake cell at this
  resolution — its lakes come out of erosion, which needs a finer grid. The plane of zeros
  is written anyway, because "this generator looked and found none" is a different
  statement from "nobody can tell you", and the capability flags carry the difference.

## Reproducing it

```bash
make terrain-worldengine                                   # generate + convert
make terrain-generate TERRAIN=build/terrain-worldengine    # 300 years on it
```

None of this is in the test suite, deliberately: a test that downloads a generator to
prove a point about a format would be a slow, network-bound way to re-assert what
`RasterTerrainTests` already asserts from a seed. The trial is a thing you run, and this
page is its result.

`WE_SEED`, `WE_RES` and `WE_ARGS` control the generator and the conversion; the variants
above were `WE_ARGS='--peak-metres 404 --abyss-metres -318'` and `WE_ARGS='--basins
drown'`. The generated set is not committed — `build/` is ignored, and the point of the
digest is that the pixels can be reproduced from the generator's seed rather than stored.

## Verdict for Phase 3

The abstraction held where it was designed to: the datum, the capability declaration, the
provenance digest and the sample budget all worked on data the engine did not make, and
the simulation did not need to know. Everything that leaked leaked in one of two places —
**the manifest is too thin** (no ocean mask, no rivers, no topology, no units the
generator can supply honestly), or **`Hydrology` was tuned against smooth noise** (no
depression filling, no prefilter before a coarse stride).

The second is the one to fix before the Vintage Story adapter, because it is not a format
problem and it will not announce itself. It produced a world that looked entirely
reasonable — eight standing civilizations, seven cities, plausible coastal siting — whose
rivers were mostly puddles.
