# Getting started

Generate a procedural history, then open it in the Historia Extera viewer.

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

### Native macOS app

The SwiftUI app opens the same generator, saved-world library, maps, timelines and entity
pages in a native macOS window. It supervises the local Astro server and keeps the history
engine in C#; no simulation code is duplicated in Swift.

```bash
make macos-run
```

The app bundle is written to `build/Historia Extera.app`. It still uses this checkout as its
working data, so install the viewer dependencies first with `make install`. Node.js 22.12+
and the .NET 10 SDK must be discoverable in the shell path, Homebrew, Volta, NVM or the
standard .NET locations.

For a release archive that does not need the checkout, Node.js or .NET installed on the target
Mac, build the self-contained variant:

```bash
make macos-release
```

This writes `build/release/Historia-Extera-v<version>-macos-<architecture>.zip`, the same name
with a `.dmg` extension, and a SHA-256 file for each. The disk image mounts to the app beside an
`Applications` shortcut, so installing is a drag. Generated worlds live outside the signed app in
`~/Library/Application Support/Historia Extera/Worlds`. The local release build is ad-hoc
signed; a Developer ID signature and notarization are still required before public distribution.

### Browser

```bash
make viewer
```

Then open the URL Astro prints (usually `http://localhost:4321`). Under the
dev server that landing page is the Worlds Library; open `world.json` from the
list, or pass `?world=` for a named export.

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
