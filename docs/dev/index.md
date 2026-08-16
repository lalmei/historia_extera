# Developer overview

## Repository layout

```text
src/HistoryEngine/         class library — pure simulation, zero NuGet deps
src/HistoryEngine.Cli/     console runner (`legends`)
src/HistoryEngine.Tests/   xunit — determinism, discipline, serialization
viewer/                    Astro + React + Tailwind Legends UI
docs/                      ProperDocs sources
DESIGN.md                  concise living design
docs/dev/decision-log.md   detailed rationale and milestone history
Makefile                   CLI + viewer + docs shortcuts
pyproject.toml / uv.lock   docs toolchain (ProperDocs)
```

`HistoryEngine` multi-targets `net7.0;net10.0`. net7.0 matches Vintage Story's mod
load TFM today; net10.0 is what the CLI and tests run on. `CheckEolTargetFramework`
is deliberately disabled for net7.

## Toolchain

| Concern | Tool |
|---|---|
| Engine / CLI / tests | .NET 10 SDK |
| Viewer | Node 22+, npm |
| Docs | uv + ProperDocs + MaterialX |

```bash
uv sync          # install docs deps into .venv
make docs-serve
make test
make generate && make viewer
```

## Next reading

- [Architecture](architecture.md)
- [Determinism](determinism.md)
- [Testing](testing.md)
- Repository-root `DESIGN.md` for the current design
- [Decision log](decision-log.md) for the full rationale and milestone history
