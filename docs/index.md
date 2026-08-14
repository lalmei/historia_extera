# Historia Extera

A Dwarf Fortress-style **world history generator** aimed at Vintage Story, plus a
Legends-mode **viewer**. Same seed and config always produce the same history.

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

Milestones 0–8 are complete: real naming languages, a settlement lifecycle that can
decline and abandon, dynastic succession, diplomacy and war — named battles, sackings,
territory changing hands at the peace table, and realms conquered out of existence — a
map that replays to any year of the run, and faiths, plagues, disasters and treasures
that cross the borders the wars draw. Terrain is still Phase 1 (noise-based placeholder)
behind `ITerrainSampler`.

| Phase | Terrain backing | State |
|---|---|---|
| 1 | Noise-based placeholder | **current** |
| 2 | Open-source 2D terrain generator | designed for, not built |
| 3 | Vintage Story worldgen | designed for, not built |

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

The long decision log lives in the repo root as `DESIGN.md`.
