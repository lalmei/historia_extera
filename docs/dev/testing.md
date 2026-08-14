# Testing

```bash
make test
# same as: dotnet test
```

Tests live in `src/HistoryEngine.Tests` (xunit) and run against the net10 build of
the engine.

## What the suite guards

| Area | Intent |
|---|---|
| Determinism | Same seed/config → same export / fingerprint |
| Determinism guards | Ban non-deterministic BCL patterns in engine source |
| Terrain discipline | Sample budgets; no accidental exact-sample storms |
| Lifecycle | Decline, abandonment, specialization actually fire |
| Dynasties / succession | Houses, reigns, ballot behaviour |
| Diplomacy / war | Wars occur and settle; every grievance is reachable; territory and its settlements move together; truces hold; war costs no terrain samples |
| Territory | The event log alone replays to the exported map, across seeds — what the viewer's year slider depends on |
| Flavour | Plague, disaster, faith and artifacts each fire; no plague takes the world; disasters match the ground they struck; provenance agrees with where a thing is |
| Naming / narration | Stable names and chronicle wording |
| Export / goldens | Fingerprint for the standard seed-42 config |

## Regenerating the golden

Standard config: seed `42`, 300 years, 8 civs, size `4096`, raster `64`.

```bash
make fingerprint
# writes src/HistoryEngine.Tests/Goldens/standard-seed42.sha256
```

Or:

```bash
dotnet run --project src/HistoryEngine.Cli -- \
  --seed 42 --years 300 --civs 8 --size 4096 --raster 64 --fingerprint \
  > src/HistoryEngine.Tests/Goldens/standard-seed42.sha256
```

Only regenerate when you understand *why* the digest moved.
