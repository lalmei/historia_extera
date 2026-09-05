# Historia Extera

Historia Extera generates a completed medieval-style world history from a seed and a
configuration. It records settlements, peoples, rulers, families, faiths, wars, trade,
travel, plagues, disasters, artifacts, and the lives caught among them. The viewer lets
you read the result by place, person, event, and year.

It is not a strategy game and does not keep simulating after generation. Vintage Story
integration is a future terrain-source boundary, not a feature in the current build.
Historia Extera does not install a mod, replace the Vintage Story sky, add handbook pages,
or provide telescopes, constellations, and player observations.

![Historia Extera](https://raw.githubusercontent.com/lalmei/historia_extera/main/docs/historia_extera.png)

<div class="historia-feature-grid">
  <a href="guide/getting-started/">
    <span>Generate a world</span>
    <small>App or source checkout</small>
  </a>
  <a href="guide/viewer/">
    <span>Browse histories</span>
    <small>Map, timeline, and biographies</small>
  </a>
  <a href="reference/configuration/">
    <span>Choose settings</span>
    <small>Defaults, ranges, and effects</small>
  </a>
  <a href="dev/">
    <span>Develop and integrate</span>
    <small>Engine, export, and extension seams</small>
  </a>
</div>

## Quick start

The packaged macOS app contains its own engine and runtime. From a source checkout, install
the .NET 10 SDK and Node.js 22.12 or newer, then run:

```bash
make install
make viewer
```

Open the URL printed by Astro and choose **Generate new world**. The
[getting-started guide](guide/getting-started.md) covers saved-world locations, generation
settings, and the difference between the app, development server, and static build.

## Reading a history

Start with the map or timeline. Both have a year control, so borders, settlements, wars,
faiths, routes, and other dated facts are shown as they stood in that year. Open any linked
name for its record. A figure page hides events, relationships, memories, and revealed plots
that the selected year had not reached yet.

The **Cosmology** page describes the generated galaxy, local system, world, moons, and
comets. Its night-sky panel is a seeded, all-sky view from the world's galactic location. It
does not use a town's latitude, the selected civic year, a local horizon, or player
observations. See [Cosmology and scientific scope](dev/cosmology.md) for the model's
approximations.

If something appears absent, first check the selected year and active filters. A realm may
not have been founded yet, a settlement may have been abandoned, a route may have closed,
or a plot may still be secret. The [viewer guide](guide/viewer.md) lists the other common
cases.

## Documentation map

### Guide

- [Getting started](guide/getting-started.md): install, generate, save, and open worlds
- [Viewer](guide/viewer.md): read maps, timelines, entities, biographies, and cosmology
- [CLI](guide/cli.md): generate and inspect worlds from a terminal

### Reference

- [Configuration](reference/configuration.md): defaults, constraints, and practical effects
- [Export format](reference/export.md): the JSON boundary and compatibility policy

### Developer

- [Overview](dev/index.md): repository map and first checks
- [Architecture](dev/architecture.md): lifecycle, time, terrain, systems, and data flow
- [Engine integration](dev/integration.md): callable APIs and extension boundaries
- [Cosmology and scientific scope](dev/cosmology.md): implemented model and approximations
- [Viewer internals](dev/viewer.md): loading, compatibility, replay, routing, and native shell
- [Determinism](dev/determinism.md): reproducibility contracts
- [Testing](dev/testing.md): suite, viewer checks, and golden fingerprints
- [Decision log](dev/decision-log.md): historical rationale and milestone records

The current design lives in the repository root as `DESIGN.md`. The decision log contains
historical statements that are preserved as evidence and may no longer describe the current
checkout.

## License and packaged app

Historia Extera is licensed under the **GNU Affero General Public License, version 3
only** (`AGPL-3.0-only`). The full terms are in the repository's
[LICENSE](https://github.com/lalmei/historia_extera/blob/main/LICENSE).

The source may be used for any purpose, including commercial use, under the AGPL. If
you distribute a modified version, or let users interact with one over a network, the
AGPL requires you to offer its corresponding source under the same license.

An individual purchase covers the packaged macOS app and updates released during the
update period stated at purchase. When that period ends, you may keep using every
version you received; renewing is needed only for later updates. Buying the packaged
app does not change or restrict the rights granted by the AGPL.

Third-party dependencies retain their own licenses; the name corpora retain their
[CC0 1.0 dedication](https://github.com/lalmei/historia_extera/blob/main/src/HistoryEngine/Naming/Corpora/LICENSES.md).
