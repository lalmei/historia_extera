# Getting started

Generate a procedural history, then open it in the Legends viewer.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 22.12+
- [uv](https://docs.astral.sh/uv/) (docs only)

## Install viewer deps

```bash
make install
# same as: npm install --prefix viewer
```

## Generate a world

From the repo root:

```bash
make generate
```

Defaults: seed `42`, 300 years, 8 civilizations, world size `4096`, raster `256`.
Output is written to:

```text
viewer/public/worlds/world.json
```

That path is what the viewer loads. Override parameters as needed:

```bash
make generate SEED=7 YEARS=500 CIVS=12
make generate ARGS='--pretty --sample 20'
make generate OUT=viewer/public/worlds/custom.json
```

When using a custom output name, select it with the viewer's `world` query parameter:

```text
http://localhost:4321/?world=worlds/custom.json
```

Equivalent without Make:

```bash
dotnet run --project src/HistoryEngine.Cli -- --seed 42 --years 300 --civs 8
```

## Open the viewer

```bash
make viewer
```

Then open the URL Astro prints (usually `http://localhost:4321`).

## Run tests

```bash
make test
# same as: dotnet test
```

## Build the docs

```bash
uv sync
make docs-serve
```

See [CLI](cli.md), [Viewer](viewer.md), and [Makefile](makefile.md) for details.
