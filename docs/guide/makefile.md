# Makefile

Root `Makefile` wraps the CLI and viewer. Run `make` (or `make help`) for the list.

## Targets

| Target | What it does |
|---|---|
| `generate` / `legends` | Run `legends` with the configured seed/years/civs |
| `fingerprint` | Rewrite the seed-42 golden digest |
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
