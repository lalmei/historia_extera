# Engine integration and extension points

Historia Extera is usable as a C# library, a CLI, or a JSON-producing process. It is not
published as a NuGet package and has no plugin discovery mechanism. Public C# accessibility
therefore means “callable in the current source tree,” not a separately versioned stability
promise.

## Surface classification

| Surface | Status | Use |
|---|---|---|
| CLI flags and exit behavior | supported application interface | generate files or invoke the engine from another process |
| Versioned JSON export | intended integration boundary | build readers in another language or process |
| `HistoryRun.Execute` | normal library entry point | run the standard engine in-process |
| `ITerrainSampler` | intended extension point | provide terrain without adding backend references to simulation systems |
| `RasterTerrainSampler` and manifest | supported interchange path | integrate a map generator through files |
| `WorldCosmology.From` and `CelestialOrientation` | callable model API | derive the seed's cosmology or transform galactic and equatorial directions |
| `Simulator(IReadOnlyList<ISystem>)` | source-level/test seam | run a custom ordered system set; no discovery or compatibility guarantee |
| `INameGenerator` on `WorldBuilder.Create` | source-level seam | replace deterministic naming when manually assembling a world |
| `WorldState`, individual systems, `Skywatch` | internal architecture despite public types | avoid as cross-version dependencies |
| viewer React modules | internal presentation implementation | consume the export instead of importing these modules as an API |

## Run the standard engine in-process

```csharp
using HistoryEngine;
using HistoryEngine.Serialization;
using HistoryEngine.World;

var config = new WorldConfig
{
    Seed = 42,
    Years = 300,
    InitialCivilizations = 8,
    WorldSize = 4096,
    EastWestPeriodic = true,
};

HistoryRun run = HistoryRun.Execute(config);
WorldExport export = run.ToExport();
string json = WorldExporter.ToJson(export);
```

`HistoryRun.Execute` validates the configuration and uses procedural terrain when no sampler
is provided. The returned `WorldState` is mutable run state. Prefer the immutable export when
handing data to another component.

The optional progress callback receives `(0, endYear)` after world creation and then one call
per completed year. It is a synchronous callback on the simulation thread.

## Provide raster terrain

The file interchange route is the smallest integration surface:

```csharp
using HistoryEngine;
using HistoryEngine.Terrain;
using HistoryEngine.World;

var terrain = RasterTerrainSampler.Load("maps/example/terrain.json");
var config = new WorldConfig { Seed = 42, Years = 300 }.WithTerrain(terrain);
HistoryRun run = HistoryRun.Execute(config, terrain);
```

`WithTerrain` copies the square extent and content-derived provenance into the configuration.
Passing the sampler without updating the config would leave the wrong size and identity in the
export.

The [CLI guide](../guide/cli.md) specifies the manifest. Use that route when the terrain
producer is not written in C# or when file-level reproducibility matters.

## Implement `ITerrainSampler`

A direct backend must supply:

- square `Bounds` compatible with `WorldConfig.WorldSize`;
- an honest `TerrainCapabilities` mask;
- deterministic `Sample(x, z)` results for the same inputs;
- every field in `TerrainSample`, even when a capability is not measured;
- a stable provenance value in `WorldConfig.TerrainSource` when the result depends on data
  beyond the world seed and `TerrainSettings`.

Override `SampleBatch` when the backend can amortize setup. `TerrainAtlas` primes its coarse
lattice through that method. Do not add caching to simulation systems or let a system hold the
sampler; the atlas is the cache and cost boundary.

Coordinates are integers inside `Bounds`. If east/west periodic behavior is enabled, the
sampler and its source data must agree at the seam. The current raster manifest cannot declare
or validate that agreement.

There is no supported way to replace terrain on an existing `WorldState`. Build a new world
instead. Consequently, there is no derived-state invalidation or refresh API.

## Query cosmology and coordinates

For a seed-only calculation:

```csharp
using HistoryEngine.World;

WorldCosmology sky = WorldCosmology.From(42);
var (rightAscensionDeg, declinationDeg) =
    sky.Orientation.ToEquatorial(galacticLongitudeRad: 0.0, galacticLatitudeRad: 0.0);
```

`ToEquatorial` and `ToGalactic` transform directions between the generated galactic frame and
the world's equatorial frame. Right ascension is returned in degrees from 0 to 360;
declination is returned in degrees from −90 to +90.

These methods do not produce a local sky. Converting equatorial coordinates to altitude and
azimuth would also require observer latitude, longitude, and local sidereal time, none of
which the current engine defines.

After a run, query `run.World.Flavour.Cosmology` for model objects or
`export.World.Cosmology` for the serialization-safe representation. Prefer the latter across
component boundaries.

## Custom systems

`ISystem` exposes a stable name, cadence, and `Tick(WorldState, Stamp)` method. A custom
ordered list can be passed through `new Simulator(systems)` and then to
`HistoryRun.Execute`.

This is useful for tests and source-level experiments, but it is not a mod-loading API:

- there is no assembly scanning, registration file, dependency ordering, or conflict policy;
- a name or cadence change moves `SystemOrderHash`;
- order determines what state a system sees in the same step;
- event and export contracts still have to be extended manually;
- consumers may not recognize new entity or event shapes.

If a custom system answers scheduled work, implement `IEpisodic` and claim a `DocketKind`.
The enum is closed in the current source, and duplicate ownership is rejected by the
`Simulator` constructor.

## Custom naming

`WorldBuilder.Create(config, sampler, names)` accepts an `INameGenerator`. Names must remain
pure functions of ids and culture language seeds if callers expect request-order independence.

`HistoryRun.Execute` does not expose a naming argument. A caller replacing naming must assemble
the world with `WorldBuilder.Create`, then run a `Simulator` directly and build the export with
`WorldExporter`. That is a source-level seam, not the preferred application path.

The standard corpora are embedded resources. There is no runtime catalog directory or data
pack for adding name languages.

## No celestial catalog replacement

Stars, planets, moons, and comets are generated from formulas and the seed. Historia Extera
does not load star, planet, comet, meteor-shower, or constellation catalogs and provides no
catalog-provider interface. Do not describe replacing a celestial catalog as supported.

Adding such a boundary would require a provenance contract, deterministic ordering, export
schema changes, compatibility handling, and a decision about whether historical sky records
consume provider data. None of those contracts exists today.

## Export consumers

The JSON document is the safest cross-language boundary. Check `schemaVersion`, preserve
typed ids, distinguish final state from replayed history, and implement narration only if you
need the engine's prose. The [export reference](../reference/export.md) describes the current
shape and compatibility rules.

When adding an exported fact in this repository:

1. change the C# export record and builder;
2. bump `WorldExport.CurrentSchemaVersion`;
3. update the TypeScript type and compatibility notes;
4. decide whether old readable exports need an empty-container normalization;
5. update tests, reference documentation, and the golden if the exported history changed.
