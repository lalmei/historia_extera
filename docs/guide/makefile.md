# Makefile reference

The root Makefile wraps generation, tests, viewer development, documentation, macOS
packaging, and version updates. Run `make` or `make help` for the current target list.

## Targets

| Target | What it does |
|---|---|
| `generate` | Run the history engine with the selected generation variables. |
| `fingerprint` | Regenerate the standard seed-42 golden digest through a temporary file. |
| `terrain-bake` | Write procedural terrain as a reference raster set. |
| `terrain-worldengine` | Generate a WorldEngine map and convert it to the raster interchange format. |
| `terrain-generate` | Run a history over the raster set named by `TERRAIN`. |
| `test` | Run .NET tests with per-test progress; accepts `FILTER`. |
| `test-quiet` | Run the same .NET suite with summary output. |
| `build` | Build the .NET solution and static Astro viewer. |
| `viewer` | Start the Astro development server. |
| `install` | Install viewer npm dependencies. |
| `preview` | Serve the previously built static viewer. |
| `macos-app` | Build and ad-hoc sign the checkout-dependent SwiftUI app. |
| `macos-run` | Build and open that developer app. |
| `macos-release` | Build a self-contained zip and disk image with SHA-256 files. |
| `macos-release-upload` | Build and upload those files to the matching GitHub draft release. |
| `docs-build` | Build the ProperDocs site strictly into `site/`. |
| `docs-serve` | Run the ProperDocs development server. |
| `bump-patch`, `bump-minor`, `bump-major` | Update every configured version-bearing file. |
| `bump-dry` | Show a version bump without applying it; select the part with `PART`. |
| `clean` | Remove .NET, viewer, documentation, and package build output. |

`macos-release-upload` changes a GitHub release. The other build and package targets are
local operations.

## Generation variables

```bash
make generate SEED=7 YEARS=500 CIVS=12 SIZE=4096 RASTER=256
make generate OUT=viewer/public/worlds/alt.json SAMPLE=20
make generate ARGS='--east-west-periodic --pretty'
```

Make defaults to seed 42; the direct CLI defaults to seed 1. `ARGS` is appended after the
named flags and can contain any current CLI option.

## Terrain variables

```bash
make terrain-bake TERRAIN=build/terrain TERRAIN_RES=512
make terrain-generate TERRAIN=build/terrain

make terrain-worldengine WE_SEED=4242 WE_RES=512
make terrain-worldengine WE_ARGS='--basins drown --peak-metres 1800'
make terrain-generate TERRAIN=build/terrain-worldengine
```

`terrain-worldengine` runs WorldEngine 0.20.0 in an isolated uv environment, then invokes
`tools/terrain/worldengine_to_raster.py`. The [terrain trial](../dev/terrain-trial.md)
records the conversion decisions and current format limitations.

## Test and version examples

```bash
make test FILTER=Affinity
make test-quiet
make bump-dry PART=minor
make bump-patch
```

Version targets update files but do not commit or tag the result.
