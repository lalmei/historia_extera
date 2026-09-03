# Contributing

Everything needed to build, run and test Historia Extera from source. What the project
*is* lives in [README.md](README.md); the design boundaries and contracts live in
[DESIGN.md](DESIGN.md).

## Layout

| Path | Responsibility |
|---|---|
| `src/HistoryEngine/` | Deterministic simulation library (no third-party runtime dependencies) |
| `src/HistoryEngine.Cli/` | Builds a config, runs the engine, writes an export |
| `src/HistoryEngine.Tests/` | Determinism, model, terrain-discipline and serialization tests |
| `viewer/` | Astro shell with a React client for browsing exported worlds |
| `macos/` | SwiftUI shell around the viewer |
| `docs/` | User and developer documentation |

The engine is a pure C# library. The CLI runs it and writes JSON. The viewer reads that
export and does not own simulation rules.

## Prerequisites

Needed to build and run from source only — the packaged macOS app ships its own
self-contained .NET CLI and Node runtime.

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 22.12+
- [uv](https://docs.astral.sh/uv/) (docs only)

From the repo root:

```bash
make install     # npm install in viewer/
```

`make` (or `make help`) lists every target.

## Run a simulation

```bash
make generate
```

Defaults: seed `42`, 300 years, 8 civilizations, world size `4096`, raster `256`. The
export lands at `viewer/public/worlds/world.json`.

```bash
make generate SEED=7 YEARS=500 CIVS=12
make generate OUT=viewer/public/worlds/custom.json
make generate ARGS='--pretty --sample 20'
```

Equivalent without Make:

```bash
dotnet run --project src/HistoryEngine.Cli -- --seed 42 --years 300 --civs 8
```

Pass `--help` for the full option list.

## Run the viewer

```bash
make viewer
```

Then open the URL Astro prints, usually `http://localhost:4321`. Select a named export
with the `world` query parameter:

```text
http://localhost:4321/?world=worlds/custom.json
```

Generating from the page (`/new`) is **development only** — an Astro integration injects
that page and its Vite middleware for `astro dev` alone, so `astro build` produces a
static bundle with no server behind it. `make build` then `make preview` serves the
production build, which reads existing exports only.

After editing the integration module itself, restart the dev server; Astro's config
reload can retain the already-imported module.

## macOS app

```bash
make macos-run       # developer build, tied to this checkout
make macos-release   # self-contained zip + .dmg with checksums
```

`make macos-run` writes `build/Historia Extera.app` and uses this checkout as its working
data, so run `make install` first; Node.js 22.12+ and the .NET 10 SDK must be discoverable
in the shell path, Homebrew, Volta, NVM or the standard .NET locations.

`make macos-release` bundles an official Node runtime, a self-contained `historia-extera`
binary and the viewer with its dependencies into
`build/release/Historia-Extera-v<version>-macos-<architecture>.{zip,dmg}` plus a SHA-256
file for each. The release app copies its packaged runtime to a writable user cache and
stores worlds in `~/Library/Application Support/Historia Extera/Worlds`, leaving the signed
bundle read-only. Local release artifacts are ad-hoc signed; notarization is a separate
publication gate.

## Tests

```bash
make test
```

## Docs

```bash
uv sync
make docs-serve      # or: make docs-build
```

- [Developer overview](docs/dev/index.md)
- [Architecture](docs/dev/architecture.md)
- [Determinism](docs/dev/determinism.md)
- [Testing](docs/dev/testing.md)
- [Decision log](docs/dev/decision-log.md)

## Before you send a change

**Determinism is the contract**: identical seed plus simulation-affecting config must
produce an identical history, byte for byte. That constrains how code is written —
forked `Pcg32` streams, ordered iteration (`DetMap`, `EntityTable` or an explicitly sorted
sequence, never `Dictionary`/`HashSet` order), ordinal string comparison and
`Hash.OfString`, `DetMath` instead of transcendental functions on decision paths, and
every simulation-affecting config field participating in `ConfigHash`. Read
[DESIGN.md](DESIGN.md) and [docs/dev/determinism.md](docs/dev/determinism.md) before
touching the engine.

- Run `make test` — the suite covers determinism, terrain discipline and serialization.
- New exported facts or changed behavior move the world fingerprint; regenerate the
  goldens in the same change and say so in the message.
- Adding an exported field means bumping `schemaVersion`; the viewer pins it and refuses
  files it does not understand.
- Keep `HistoryEngine` free of third-party runtime dependencies.
- Presentation belongs in the viewer, simulation rules in the engine. Narration templates
  turn flat event facts into prose; the facts stay filterable when the wording changes.
