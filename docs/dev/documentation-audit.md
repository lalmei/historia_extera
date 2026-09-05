# Documentation audit, 5 September 2026

This audit covers Historia Extera, the history generator and viewer in this repository.
It does not cover the separate Astra Terra mod. No runtime behavior was changed.

## Changes

The guides now distinguish packaged-app, source-checkout, and static-viewer workflows.
They explain generation defaults, library actions, historical replay, figure pages, and
why a missing fact is not necessarily an error. Continue is a longer rerun, not a saved-state
resume. Imported worlds do not necessarily contain enough information for reproduction.

Technical pages now describe initialization, system order and cadence, terrain caching,
coordinate conventions, civic time, export serialization, viewer derivation, native hosting,
and source-level integration seams. The cosmology page separates procedural approximations
from physical astronomy and explains how comet returns reach simulated historical records.

Corrected claims include annual-only scheduling, unsupported local skies and catalog
replacement, raster-layer dimensions and water thresholds, obsolete hydrology limitations,
configuration defaults, native generation availability, and export compatibility evidence.
The final source refresh found schema 51 in both producer and viewer.

Historical decision records remain historical, with a warning against treating them as the
current specification. A separate prose pass removed filler and unsupported claims while
retaining explanatory rationale.

## File inventory

Added:

- `docs/reference/configuration.md`
- `docs/reference/export.md`
- `docs/dev/integration.md`
- `docs/dev/cosmology.md`
- `docs/dev/viewer.md`
- `docs/dev/documentation-audit.md` (this report)

Updated documentation and navigation:

- `README.md`, `CONTRIBUTING.md`, `DESIGN.md`, `properdocs.yml`
- `docs/README.md`, `docs/index.md`
- `docs/guide/getting-started.md`, `docs/guide/viewer.md`, `docs/guide/cli.md`,
  `docs/guide/makefile.md`
- `docs/dev/index.md`, `docs/dev/architecture.md`, `docs/dev/determinism.md`,
  `docs/dev/testing.md`, `docs/dev/decision-log.md`, `docs/dev/terrain-trial.md`
- `src/HistoryEngine/Naming/Corpora/LICENSES.md`

Updated comments only:

- `src/HistoryEngine/Core/EntityId.cs`, `src/HistoryEngine/Core/Stamp.cs`
- `src/HistoryEngine/Events/HistoryEvent.cs`
- `src/HistoryEngine/Systems/ISystem.cs`, `src/HistoryEngine/Systems/Simulator.cs`
- `src/HistoryEngine/World/Cosmology.cs`, `src/HistoryEngine/World/CelestialOrientation.cs`,
  `src/HistoryEngine/World/Skywatch.cs`
- `src/HistoryEngine/Serialization/WorldExport.cs`, `src/HistoryEngine/HistoryEngine.csproj`
- `src/HistoryEngine.Tests/ClockTests.cs`, `viewer/src/app/nightSky.ts`

No documentation files were removed. Other concurrent implementation, test, and lockfile
changes were excluded from this audit's checkpoint.

## Verification and remaining work

The source-backed player and contributor passes were completed. The engine build succeeded,
the saved full-suite report records 526 passed and zero failures, and the viewer test run
records 25 passed. These results describe the checkout when each command ran; other work
continued in the shared checkout during the audit. They are not native-app acceptance tests.
The strict documentation build and static viewer build passed. Astro's check reported no
errors or warnings and three unused-code hints outside this audit's scope.

Implementation follow-ups, intentionally not changed here:

- [#195: preserve generation recipes and full-width seeds](https://github.com/lalmei/historia_extera/issues/195).
  Reruns recover civilization count from filenames, omit custom terrain/configuration, and
  cannot safely represent every CLI `ulong` seed in a JavaScript number.
- [#196: retain historical compatibility fixtures](https://github.com/lalmei/historia_extera/issues/196).
  The two available exports were schema 50, not evidence for the entire declared 21–51 range.
- [#197: clarify celestial inclination geometry](https://github.com/lalmei/historia_extera/issues/197).
  A source comment describes a local-horizon angle without observer or sidereal-time inputs.

Human review remains for native first launch, library interactions, and Gatekeeper behavior
on a packaged release. Scientific formulas are documented as the implemented model, not as
validated physical predictions. The horizon-angle contract needs the separate review above.
