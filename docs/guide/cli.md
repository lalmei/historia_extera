# Command-line generator

The console runner lives in `src/HistoryEngine.Cli` and builds the `historia-extera`
executable.

```bash
dotnet run --project src/HistoryEngine.Cli -- [options]
```

Use `--help` for the usage text compiled into the current checkout.

## Options

| Flag | CLI default | Effect |
|---|---:|---|
| `--seed <n>` | `1` | Unsigned 64-bit master seed. |
| `--years <n>` | `300` | Number of years to simulate. The library permits zero; the viewer form requires at least one. |
| `--civs <n>` | `8` | Requested founding civilizations. The library permits zero. |
| `--size <n>` | `4096` | Side length of the square world in abstract units. |
| `--east-west-periodic` | off | Join east and west while leaving north and south bounded. |
| `--raster <n>` | `256` | Resolution per axis of the map raster in the export. |
| `--out <path>` | `viewer/public/worlds/world.json` | Output JSON path. Parent directories are created. |
| `--pretty` | off | Indent JSON. Pretty output is not the canonical byte form. |
| `--sample <n>` | `12` | Print that many narrated events after the summary; zero disables samples. |
| `--fingerprint` | off | Print only the export SHA-256 digest and write no world file. |
| `--terrain <path>` | none | Load a raster manifest instead of procedural terrain. |
| `--emit-terrain <dir>` | none | Bake procedural terrain to a manifest and PGM layers, then exit. |
| `--terrain-res <n>` | `512` | Resolution per axis for `--emit-terrain`; must be at least 2. |

All changes take effect on a new run. The CLI has no configuration file or live reload.

The Makefile supplies seed 42 rather than the CLI's seed 1:

```bash
make generate SEED=7 YEARS=500 CIVS=12 SIZE=4096 RASTER=256
make generate ARGS='--east-west-periodic --pretty --sample 20'
```

## Validation and reproduction

World size must be positive. The current defaults use 128-unit regions, a 256-unit terrain
lattice, and a 64-unit hydrology lattice. A periodic world size must be divisible by all
three so the east/west seam closes on each grid.

The region grid must also have at least 16 regions per requested civilization. This rejects
configurations that cannot seat the request at all. It is not a promise that every seed will
seat the full count; a world with too little habitable land can still found fewer, and the
summary reports the shortfall.

The same seed is not sufficient by itself. Exact reproduction also requires the same
simulation-affecting configuration, terrain contents, engine behavior, and system order.
The export records a configuration hash, system-order hash, engine version, and seed.
`--raster` affects the presentation payload and is deliberately excluded from the simulation
configuration hash.

## Raster terrain

`--terrain` accepts greyscale PGM layers described by a JSON manifest:

```json
{
  "worldSize": 4096,
  "height": { "file": "height.pgm", "min": -900, "max": 2400, "seaLevel": 0.2 },
  "temperature": { "file": "temperature.pgm", "min": -25, "max": 32 },
  "rainfall": { "file": "rainfall.pgm" }
}
```

Layers may have different pixel dimensions; each is sampled over the same extent. The format describes one square world
starting at `(0, 0)`; it does not declare wrapping topology. `worldSize` is the simulation
extent, not the pixel count.

`height` is required and must provide `min`, `max`, and a normalized `seaLevel` in the range
0–1. Temperature, when supplied, must also provide `min` and `max`. Rainfall, geology,
forest, and shrub layers are optional and use a 0–1 range when no range is declared.
The optional water layer uses raw normalized pixels, ignoring `min` and `max`. A value
above 0.5 marks an inland lake; height below sea level marks ocean.

When climate or ecology layers are absent, the sampler models temperature and rainfall from
map row and elevation, then derives geology, forest, and shrub density. Modeled fields are
not reported as measured capabilities. Missing lake data stays missing rather than turning
every height depression into a lake.

`--terrain` takes world size from the manifest and ignores a conflicting `--size` with a
note. The manifest bytes and every named PGM contribute to terrain provenance, so changing
the pixels changes the configuration hash even when the paths stay the same.

The format does not currently accept a separate ocean mask, a measured river-flow plane, or
a topology declaration. Hydrology derives rivers from height using a depression-filled
drainage surface; the exported elevation itself is not changed. The
[terrain trial](../dev/terrain-trial.md) explains the known interchange limits.

## Output modes

Normal generation writes one self-contained JSON export and prints a summary plus optional
sample events. `--pretty` changes whitespace and file size, not the history.

`--fingerprint` writes only the digest to standard output. The Makefile uses it to update the
seed-42 golden:

```bash
make fingerprint
```

Only update that golden after accounting for the behavioral or exported-data change that
moved it.

## Examples

```bash
# Default Makefile world: seed 42
make generate

# Longer periodic world
dotnet run --project src/HistoryEngine.Cli -- \
  --seed 99 --years 800 --civs 15 --size 4096 --east-west-periodic

# Bake procedural terrain, then read it through the raster backend
make terrain-bake
make terrain-generate TERRAIN=build/terrain

# Convert a WorldEngine world, then run a history over it
make terrain-worldengine
make terrain-generate TERRAIN=build/terrain-worldengine
```
