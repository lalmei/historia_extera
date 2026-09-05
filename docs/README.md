# Historia Extera documentation

Documentation sources for the `historia-extera` history generator and viewer.

The site builds with [ProperDocs](https://properdocs.org/) and MaterialX. Dependencies live
in the root `pyproject.toml` and `uv.lock` under the `docs` group.

```bash
uv sync
make docs-serve   # or: make docs-build
```

## Audiences

- `guide/` is for people generating and reading histories.
- `reference/` records configuration and export contracts.
- `dev/` explains architecture, integration seams, tests, and historical decisions.

Keep immediate usage instructions in the guides. Put implementation contracts in developer
or reference pages and link to them instead of making the viewer guide carry both audiences.

The [September 2026 audit](dev/documentation-audit.md) records the reviewed scope, corrected
claims, verification boundary, and implementation follow-ups.

## Source-of-truth map

| Subject | Check before editing docs |
|---|---|
| CLI flags and defaults | `src/HistoryEngine.Cli/Program.cs` |
| Library configuration | `src/HistoryEngine/World/WorldConfig.cs` |
| Simulation order and cadence | `src/HistoryEngine/Systems/Simulator.cs` and each system's `Cadence` |
| Terrain manifest | `src/HistoryEngine/Terrain/RasterTerrainSampler.cs` |
| Export schema | `src/HistoryEngine/Serialization/WorldExport.cs` |
| Viewer compatibility | `viewer/src/app/compat.ts` |
| Viewer routes and controls | `viewer/src/app/App.tsx`, `views/`, and `components/` |
| Development generator | `viewer/dev/world-generator.mjs` and `viewer/src/app/generate.ts` |
| macOS paths and process lifecycle | `macos/HistoriaExteraApp/` and `tools/build_macos_release.sh` |

`DESIGN.md` is the concise living design. `dev/decision-log.md` preserves the reasoning and
measurements recorded when decisions were made; it is not a second current-state specification.

Before committing documentation changes, run `make docs-build`. Use `make test` when a claim
depends on an engine contract, and the viewer checks listed in `dev/testing.md` when a claim
depends on TypeScript or rendering behavior.
