# Determinism

**Contract:** identical seed + config → identical history, byte for byte, across
processes and machines.

## Rules of the road

- **Forked RNG substreams** via `Pcg32.Fork(purpose, discriminator)` from the parent's
  immutable seed, never its position. One fork per system per year by convention.
- **No `Dictionary` / `HashSet` iteration** on decision paths — use `DetMap` /
  `EntityTable`.
- **Ordinal string comparers** always (`StringComparer.Ordinal`).
- **No transcendentals on decision paths** — `DetMath` polynomials instead of
  `Sin`/`Pow`/`Exp` where outcomes fork on comparisons.
- **No `string.GetHashCode()`** — use `Hash.OfString` (FNV-1a).
- **Strictly sequential tick loop** — no parallel mutation.
- **System order is hashed** — swapping two systems changes history as much as
  changing the seed.

`DeterminismGuardTests` scans engine source for these constructs. Escape hatch: a
trailing `// det:ok` comment — deliberate and annotated.

## Golden fingerprint

When the golden for seed 42 fails, the history changed. That is expected if you
intentionally changed growth rates, system order, or scoring. Regenerate:

```bash
make fingerprint
```

If it changes when you did *not* intend to change simulation behaviour, that is the
bug the test exists to find.

See [Testing](testing.md).
