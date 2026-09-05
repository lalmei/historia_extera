# Getting started

Historia Extera generates a complete history and saves it as a JSON world file. The viewer
opens that file; it does not continue the simulation while you read.

This repository is not the Astra Terra astronomy mod. The current application does not run
inside Vintage Story or add handbook entries, telescope controls, constellations, player
observations, or craftable astronomy journals.

## Packaged macOS app

Open **Historia Extera** and choose **Generate new world**. A packaged app includes the
history engine and Node runtime. It stores generated worlds here:

```text
~/Library/Application Support/Historia Extera/Worlds
```

Choose a world in the library to read it. Deleting a world from the current library is
permanent after confirmation.

Local packages built with `make macos-release` are ad-hoc signed and not notarized. macOS may
require Control-clicking the app and choosing **Open** the first time. A public distribution
still needs Developer ID signing and notarization.

## From a source checkout

Install:

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 22.12 or newer

Then run from the repository root:

```bash
make install
make viewer
```

Open the URL Astro prints, normally `http://localhost:4321`, and choose **Generate new
world**. The development server saves worlds in `viewer/public/worlds/`.

`make viewer` must stay running while the browser generates worlds. A production static
build can read existing exports but has no local process that can run the C# generator.

## Generation settings

The form starts a fresh world with a random 32-bit seed, 300 years, 5 civilizations, a
4096-unit map, and east/west wrapping enabled. These are form defaults, not the raw CLI
defaults.

| Setting | Meaning |
|---|---|
| Seed | Master random identity. Decimal and hexadecimal input are accepted. |
| Years | Number of civic years simulated from year 1. |
| Civilizations | Number of founding peoples requested. A wet or mountainous seed may seat fewer. |
| World size | Side length of the square map in abstract world units. |
| East/west periodic | Joins the left and right edges. North and south stay bounded. |

The form accepts 1–5,000 years, 1–64 civilizations, and world sizes from 512 to 8,192 in
256-unit steps. It rejects a map too small to give the requested civilizations enough
regions. [Configuration](../reference/configuration.md) explains the command-line and
library settings that are not exposed in the form.

Only one generation job runs at a time. **Abort synthesis** requests cancellation. Check the
library if the job was already finishing when it was cancelled.

## Run, regenerate, and continue

The library offers three related actions:

- **Run** repeats the reconstructed settings with the engine currently installed. Confirm whether
  the result replaces the same filename or is saved beside it.
- **Regenerate** opens the form with the reconstructed settings so you can change them.
- **Continue** proposes a longer run with the same settings. The engine starts again at year
  1 and writes another file; it does not resume mutable state from the shorter export.

These actions recover only the settings exposed in the form. The initial civilization count
comes from the generated filename and falls back to 8 when that name is missing. External
terrain and custom library configuration are not restored. For those worlds, rerun the
original CLI command or library invocation instead.

With the same engine, terrain, seed, simulation configuration, and system order, the first
years of a longer run reproduce the shorter history. A seed by itself is not enough if the
other settings or engine have changed.

## First reading

Open a world, then:

1. Use **Overview** for counts and prominent realms, cities, houses, and events.
2. Open **Map** and move the year control to watch settlements and borders change.
3. Open **Timeline** to filter the chronicle by year and event kind.
4. Follow linked names to entity pages. Figure pages have their own year control for reading
   a life without later knowledge.
5. Open **Cosmology** for the generated galaxy, local system, world, moons, and comets.

If a fact seems to be missing, check the selected year and filters. The entity may not yet
exist, may already have ended, or may still be secret at that point in the record. The
[viewer guide](viewer.md) explains the controls and the limits of historical replay.

## Terminal generation

`make generate` writes the Makefile's default world to
`viewer/public/worlds/world.json`:

```bash
make generate
make generate SEED=7 YEARS=500 CIVS=12
```

The Makefile defaults are seed 42, 300 years, 8 civilizations, size 4096, and a 256-pixel
map raster. See the [CLI guide](cli.md) for direct invocation and external terrain.

## Building the app or documentation

Developer builds use the checkout and installed tools:

```bash
make macos-run
```

A self-contained package is produced with:

```bash
make macos-release
```

Documentation contributors also need [uv](https://docs.astral.sh/uv/):

```bash
uv sync
make docs-serve
```
