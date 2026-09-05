# Developer overview

Historia Extera has four runtime pieces with one data boundary between the engine and the
reader.

```text
src/HistoryEngine/       deterministic C# simulation library
src/HistoryEngine.Cli/   command-line host and JSON writer
viewer/                  Astro and React export reader
macos/                   SwiftUI host for the local viewer server
```

The viewer consumes the JSON export. It does not call engine classes or own simulation
rules. The native app supervises local processes and embeds the viewer; it does not
reimplement the engine in Swift.

## Repository layout

| Path | Responsibility |
|---|---|
| `src/HistoryEngine/Core/` | deterministic ids, collections, math, and random streams |
| `src/HistoryEngine/World/` | configuration, mutable state, world creation, time, cosmology, and cross-system models |
| `src/HistoryEngine/Terrain/` | sampler contract, atlas, procedural and raster backends, hydrology, and landforms |
| `src/HistoryEngine/Systems/` | ordered annual and seasonal simulation behavior |
| `src/HistoryEngine/Events/` | factual event log, significance, and narration templates |
| `src/HistoryEngine/Serialization/` | versioned export and canonical fingerprint |
| `src/HistoryEngine.Cli/` | argument parsing, terrain loading, progress, summaries, and file output |
| `src/HistoryEngine.Tests/` | contract, calibration, determinism, terrain, and serialization tests |
| `viewer/src/app/` | export loading, compatibility, replay, routes, views, and renderers |
| `viewer/dev/` | development-only generator and world-library middleware |
| `macos/HistoriaExteraApp/` | process supervision, paths, and `WKWebView` shell |
| `tools/terrain/` | external-terrain conversion |
| `tools/build_macos_release.sh` | self-contained macOS packaging |
| `docs/` | player, reference, developer, and historical documentation |

`DESIGN.md` is the concise current design. `docs/dev/decision-log.md` preserves detailed
arguments and milestone-era snapshots, including statements that later work superseded.

## Toolchain

| Area | Requirement |
|---|---|
| Engine, CLI, tests | .NET 10 SDK |
| Engine compatibility build | `net7.0`, retained for the planned Vintage Story boundary and requiring revalidation before that integration |
| Viewer | Node.js 22.12 or newer and npm |
| Native shell | macOS and Swift Package Manager |
| Documentation | uv, ProperDocs, and MaterialX |

`HistoryEngine` targets `net7.0;net10.0`, uses C# 11, and has no NuGet runtime
dependencies. The CLI and tests use .NET 10. Vintage Story integration is not present in the
current repository; do not treat the `net7.0` target as proof of compatibility with a current
game release.

## First checks

```bash
make install
make build
make test
make docs-build
```

The .NET suite is intentionally long. Use `make test FILTER=NameFragment` while iterating and
run the full suite before changing determinism, export, time, terrain, or system behavior.

## Read next

- [Architecture](architecture.md): initialization, terrain, time, systems, and data flow
- [Engine integration](integration.md): current callable surfaces and stability limits
- [Cosmology and scientific scope](cosmology.md): physics, sky records, and approximations
- [Viewer internals](viewer.md): loading, replay, routing, and native hosting
- [Configuration](../reference/configuration.md): every `WorldConfig` default
- [Export format](../reference/export.md): the C#/TypeScript boundary
- [Determinism](determinism.md) and [Testing](testing.md): rules that protect that boundary
