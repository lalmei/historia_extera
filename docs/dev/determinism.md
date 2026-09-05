# Determinism

The engine contract is:

> The same seed, simulation-affecting configuration, system list, and implementation produce
> the same canonical exported history byte for byte.

`meta.seed`, `meta.configHash`, `meta.systemOrderHash`, and `meta.engineVersion` identify
those inputs in an export. A seed alone does not identify the world when years, topology,
terrain, system order, or engine behavior differ.

## Decision-path rules

- Randomness comes from `Pcg32.Fork(purpose, discriminator)`, derived from an immutable
  parent seed rather than its current draw position.
- Simulation iteration uses `DetMap`, `EntityTable`, or an explicitly sorted sequence.
  `Dictionary` and `HashSet` enumeration must not choose outcomes.
- Strings use ordinal comparison and `Hash.OfString`; process-randomized and
  culture-sensitive operations stay off decision paths.
- `DetMath` and `DetSeries` provide controlled numerical functions where a platform-level
  difference could cross a decision threshold.
- The tick loop mutates state sequentially. Parallel work would need a deterministic
  collect-then-apply boundary.
- System name, order, and non-annual cadence contribute to `SystemOrderHash`.
- Every simulation-affecting `WorldConfig` field contributes to `ConfigHash`. The seed is
  exported beside it and map-raster resolution is presentation-only.

`DeterminismGuardTests` scans engine source for prohibited constructs. The narrow escape is
a trailing `// det:ok` comment on a reviewed use that cannot affect a decision.

## Independent streams

A system normally forks by its stable name and year. Entity- or episode-specific work adds a
stable discriminator. Naming, galaxy generation, local cosmology, and celestial orientation
also use independent streams.

This limits unrelated churn: adding a draw to a battle should not rename a settlement or
change the host star. It does not mean an implementation change inside one stream preserves
that subsystem's later choices.

## Split-run contract

`Simulator.Advance` exists so tests can compare a run completed in pieces with one completed
continuously. Any mutable state needed for continuation must live on `WorldState`; hidden
static or caller-owned state will make the comparison diverge.

The viewer's **Continue** action does not deserialize and resume `WorldState`. It reruns from
year 1 with a later end year. Determinism makes the shorter history a prefix when all
simulation inputs and the implementation match.

## Golden fingerprint

The standard seed-42 fingerprint pins the canonical export. When it changes unexpectedly,
find the first behavioral or exported-data difference before updating it.

```bash
make fingerprint
```

`WorldExporter.Fingerprint` clears three file-contract values before hashing:

- engine release version;
- `schemaVersion`;
- narration syntax version.

Changing those numbers alone does not change the simulated history. Adding an export field
does change the digest because the world now carries different facts, even if system decisions
are unchanged.

Canonical JSON is compact. `--pretty` writes the same data with different bytes and should not
be used as a byte-for-byte comparison target.

See [Testing](testing.md) for commands and [Configuration](../reference/configuration.md) for
the identity fields.
