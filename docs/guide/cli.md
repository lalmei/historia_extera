# CLI (`legends`)

The console runner lives in `src/HistoryEngine.Cli`. Assembly name is `legends`.

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

Identical `--seed` and config always produce an identical file.

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
```
