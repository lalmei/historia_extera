# Makefile

Root `Makefile` wraps the CLI and viewer. Run `make` (or `make help`) for the list.

## Targets

| Target | What it does |
|---|---|
| `generate` | Run the history engine with the configured seed/years/civs |
| `fingerprint` | Rewrite the seed-42 golden digest |
| `terrain-bake` | Bake the noise world out as a raster set (the reference manifest) |
| `terrain-worldengine` | Generate a WorldEngine world and convert it to a raster set |
| `terrain-generate` | Run a history over a raster set |
| `test` | `dotnet test` |
| `build` | `dotnet build` + viewer `astro build` |
| `viewer` | Viewer dev server |
| `install` | `npm install` in `viewer/` |
| `preview` | Viewer production preview |
| `docs-build` | ProperDocs → `site/` (`uv run`) |
| `docs-serve` | ProperDocs live reload (`uv run`) |
| `clean` | `dotnet clean` + remove viewer/docs build dirs |

## Generation knobs

```bash
make generate SEED=7 YEARS=500 CIVS=12 SIZE=4096 RASTER=256
make generate OUT=viewer/public/worlds/alt.json
make generate SAMPLE=20
make generate ARGS='--pretty'
```

`ARGS` is appended after the named flags, so it can add anything the CLI accepts.

## Terrain knobs

```bash
make terrain-bake TERRAIN=build/terrain TERRAIN_RES=512
make terrain-generate TERRAIN=build/terrain

make terrain-worldengine WE_SEED=4242 WE_RES=512
make terrain-worldengine WE_ARGS='--basins drown --peak-metres 1800'
make terrain-generate TERRAIN=build/terrain-worldengine
```

`terrain-worldengine` runs [WorldEngine](https://github.com/Mindwerks/worldengine)
through `uv run --no-project --with`, so nothing it needs is installed into this project,
and converts its output with `tools/terrain/worldengine_to_raster.py`. What the conversion
has to decide on the generator's behalf — and what those decisions cost — is written up in
[the terrain trial](../dev/terrain-trial.md).
