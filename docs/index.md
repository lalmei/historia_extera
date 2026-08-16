# Historia Extera

A Dwarf Fortress-style **world history generator** aimed at a Medival like world along with a proper viewer. Same seed and config always produce the same history.

![Historia Extera](https://raw.githubusercontent.com/lalmei/historia_extera/main/docs/historia_extera.png)

<div class="historia-feature-grid">
  <a href="guide/getting-started/">
    <span>Generate a world</span>
    <small>CLI + Makefile</small>
  </a>
  <a href="guide/viewer/">
    <span>Browse legends</span>
    <small>Astro viewer</small>
  </a>
  <a href="dev/architecture/">
    <span>Architecture</span>
    <small>Engine boundaries</small>
  </a>
  <a href="dev/determinism/">
    <span>Determinism</span>
    <small>Why goldens matter</small>
  </a>
</div>

## Status

Milestones 0–12 and 14 are complete, while M13's sub-year clock is in progress: real naming
languages, a settlement lifecycle that can
decline and abandon, dynastic succession, diplomacy and war — named battles, sackings,
territory changing hands at the peace table, and realms conquered out of existence — a
map that replays to any year of the run, faiths that differ in gods, church and temper rather
than only in fervour, plagues, disasters and treasures that cross the borders the wars draw,
terrain that can come from somewhere else, rulers shaped by disposition and recent fortune,
and offices that raise notable households from the simulated population.

M9 built the first backend behind `ITerrainSampler` that the interface was not written
alongside. Worlds are still noise by default; pointing `--terrain` at a set of heightmap
and climate rasters runs the same simulation over a map from Azgaar's Fantasy Map
Generator, a GIS export, or a heightmap someone painted. Only `height` is required, and
the fields no raster supplied are modelled rather than claimed — so a world built on a
bare heightmap reports which of its measurements were measured.

| Phase | Terrain backing                  | State                                |
| ----- | -------------------------------- | ------------------------------------ |
| 1     | Noise-based placeholder          | **default**                          |
| 2     | Open-source 2D terrain generator | **available** via raster interchange |
| 3     | Vintage Story worldgen           | designed for, not built              |

See [the CLI guide](guide/cli.md) for the raster route and the
[decision log](dev/decision-log.md) for what M9 proved about the terrain boundary.

## Quick start

Needs a .NET 10 SDK, Node 22+, and [uv](https://docs.astral.sh/uv/) for docs.

```bash
make generate    # seed 42 → viewer/public/worlds/world.json
make viewer      # Astro at http://localhost:4321
make docs-serve  # this site
```

## Docs map

### Guide

- [Getting started](guide/getting-started.md) — generate and view a world
- [CLI](guide/cli.md) — `legends` options
- [Viewer](guide/viewer.md) — Astro + React island
- [Makefile](guide/makefile.md) — common targets

### Developer

- [Overview](dev/index.md) — layout and toolchain
- [Architecture](dev/architecture.md) — engine, CLI, viewer boundaries
- [Determinism](dev/determinism.md) — contracts and escapes
- [Testing](dev/testing.md) — suite and golden fingerprints
- [Decision log](dev/decision-log.md) — detailed rationale and milestone history

The concise living design lives in the repo root as `DESIGN.md`.
