# CLI (`historia-extera`)

The console runner lives in `src/HistoryEngine.Cli`. Its executable is `historia-extera`.

## Run

```bash
dotnet run --project src/HistoryEngine.Cli -- [options]
# or
make generate [SEED=… YEARS=… CIVS=… SIZE=… RASTER=… OUT=… ARGS='…']
```

Pass `--help` for the built-in usage text.

## Options

| Flag | Default | Purpose |
|---|---|---|
| `--seed <n>` | `1` | Master seed |
| `--years <n>` | `300` | Years to simulate |
| `--civs <n>` | `8` | Starting civilizations |
| `--size <n>` | `4096` | World side length in units |
| `--raster <n>` | `256` | Map raster resolution per axis |
| `--out <path>` | `viewer/public/worlds/world.json` | Output JSON path |
| `--pretty` | off | Indent JSON (not the canonical form) |
| `--sample <n>` | `12` | Print *n* narrated chronicle events (`0` disables) |
| `--fingerprint` | off | Print only the export digest; write nothing |
| `--terrain <path>` | — | Run over a terrain raster set instead of noise |
| `--emit-terrain <dir>` | — | Bake this seed's noise world into a raster set and exit |
| `--terrain-res <n>` | `512` | Resolution per axis when baking |

Identical `--seed` and config always produce an identical file. The world's own name —
planet or moon, unique to that seed — is printed as the run starts, and again in the
summary, so a history can be recognised without opening the export. The seed remains the
input that reproduces it.

## Terrain from another generator

By default the world is built on the engine's own noise sampler. `--terrain` points it at
a set of rasters exported from somewhere else — Azgaar's Fantasy Map Generator, a GIS
export, a heightmap you painted — described by a JSON manifest:

```json
{
  "worldSize": 4096,
  "height":      { "file": "height.pgm", "min": -900, "max": 2400, "seaLevel": 0.2 },
  "temperature": { "file": "temperature.pgm", "min": -25, "max": 32 },
  "rainfall":    { "file": "rainfall.pgm" }
}
```

Layers are greyscale PGM. Most tools write one directly:

```bash
magick heightmap.png -colorspace Gray -depth 16 height.pgm
```

**Only `height` is required.** Every other field is modelled from elevation and latitude
when no raster supplies it, and left out of the capabilities the run reports — so a bare
heightmap produces a complete world that is honest about which of its measurements were
measured. The summary prints the split:

```
── Terrain ──────────────────────────────
  backend        raster:3b3b1cc1b7a4b603
  extent         4096 x 4096
  measured       Height
  modelled       Temperature, Rainfall, GeologicActivity, ForestDensity, ShrubDensity
```

Lakes appear in neither line for a bare heightmap, and that is deliberate. A depression in
a heightmap is not a lake — whether water stands in it depends on drainage and climate the
raster never carried — so the backend reports ocean below sea level and nothing above it
rather than inventing inland water. A field that is neither measured nor modelled is not
listed as either.

`min` and `max` say what the darkest and brightest values mean in the field's own units;
a PGM carries no units of its own. `seaLevel` is the normalised value that is the
shoreline — heights are metres relative to a sea level of exactly zero, so this is how a
generator's own convention is honoured. Layers with a natural 0–1 range may omit
`min`/`max`; temperature may not.

`--terrain` sets the world size from the manifest, overriding `--size`, and records a
digest of every file in the config hash — so two runs over different rasters can never
claim the same provenance.

## Output

The CLI writes a deterministic JSON export for the viewer. Parent directories are created
as needed. With `--fingerprint`, stdout is only the SHA-256 digest — used to refresh
the golden under `src/HistoryEngine.Tests/Goldens/`.

## Examples

```bash
# Default Make world (seed 42)
make generate

# Longer run, prettier JSON, more chronicle samples
dotnet run --project src/HistoryEngine.Cli -- \
  --seed 99 --years 800 --civs 15 --pretty --sample 24

# Regenerate the golden fingerprint for seed 42
make fingerprint

# Bake the noise world to rasters, then run a history over them
make terrain-bake
make generate ARGS='--terrain build/terrain/terrain.json'

# The same route, on terrain from an external generator
make terrain-worldengine
make generate ARGS='--terrain build/terrain-worldengine/terrain.json'
```

Baking is also the quickest way to get a reference manifest to compare your own against
when a raster set will not load. For an external generator's terrain,
`make terrain-worldengine` runs WorldEngine and converts its output; the
[terrain trial](../dev/terrain-trial.md) records what that conversion has to decide, and
what the abstraction got wrong when it did.
