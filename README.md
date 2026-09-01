# Historia Extera

A deterministic world-history generator and viewer. From a seed and a
config it builds centuries of settlements, peoples, rulers, faiths, wars, trade, disasters,
and artifacts, then writes a history you can read without running the simulation again.

Same seed and config always produce the same history.

![Historia Extera](docs/historia_extera.png)

The engine is a pure C# library. The CLI (`historia-extera`) runs it and writes JSON. The viewer
is a separate Astro app that reads that export — maps, timelines, and entity pages — and
does not own simulation rules.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 22.12+

From the repo root:

```bash
make install     # npm install in viewer/
```

## Run a simulation

```bash
make generate
```

Defaults: seed `42`, 300 years, 8 civilizations, world size `4096`, raster `256`.
The export lands at:

```text
viewer/public/worlds/world.json
```

Override the knobs as needed:

```bash
make generate SEED=7 YEARS=500 CIVS=12
make generate OUT=viewer/public/worlds/custom.json
make generate ARGS='--pretty --sample 20'
```

Equivalent without Make:

```bash
dotnet run --project src/HistoryEngine.Cli -- --seed 42 --years 300 --civs 8
```

Pass `--help` on that command for the full option list. `make` (or `make help`) lists
the other targets.

## Open the viewer

```bash
make viewer
```

Then open the URL Astro prints, usually `http://localhost:4321`.

With the dev server running, `/` is the **Worlds Library**: every JSON export already in
`viewer/public/worlds/`. Open `world.json` from the list, or pass a named file:

```text
http://localhost:4321/?world=worlds/custom.json
```

You can also generate from the viewer. **Generate new world** opens `/new` (**Initialize
Engine**): seed, years, civilization count, and world size. The dev server runs the CLI
for you; when the run finishes, the viewer opens that history.

A production static build (`make build`, then `make preview`) still reads existing
exports. Generating from the page is development-only.

## Tests

```bash
make test
```

## Docs

Fuller guides live under `docs/`:

```bash
uv sync
make docs-serve
```

- [Getting started](docs/guide/getting-started.md)
- [CLI](docs/guide/cli.md)
- [Viewer](docs/guide/viewer.md)
- [Makefile](docs/guide/makefile.md)
- [DESIGN.md](DESIGN.md) — living design
