# Configuration

Historia Extera has three configuration surfaces: the viewer form, command-line flags, and
the public `WorldConfig` record. There is no persistent config file and no live reload. Every
change requires a new generation run.

## Viewer form

| Setting | Fresh-form value | Accepted values | Practical effect |
|---|---:|---|---|
| Seed | random 32-bit value | 0 to 9,007,199,254,740,991; decimal or `0x` hex | Derives procedural terrain, names, cosmology, and random simulation choices. |
| Years | 300 | 1–5,000 | Sets the end of the civic record, starting from year 1. Runtime and export size grow with it. |
| Civilizations | 5 | 1–64 | Requests this many founding peoples. Low-habitability worlds can found fewer. |
| World size | 4,096 | 512–8,192 in steps of 256 | Sets the square map extent and number of 128-unit regions. |
| East/west periodic | on | on or off | Joins the left and right edges for terrain, drainage, adjacency, distance, and map rendering. |

The size presets are Small 2,048, Medium 4,096, and Large 8,192. The form also enforces a
minimum size based on 16 regions per requested civilization.

The shared `DEFAULT_PARAMS` used when reconstructing incomplete saved settings is seed 42,
300 years, 8 civilizations, size 4,096, and a bounded map. A completely fresh `/new` form
intentionally replaces the seed, uses 5 civilizations, and enables wrapping.

## CLI and Makefile

The direct CLI defaults to seed 1, while `make generate` passes seed 42. Both default to 300
years, 8 civilizations, size 4,096, and a 256-pixel export raster. East/west wrapping is off
unless `--east-west-periodic` is present.

The CLI also exposes output formatting, event samples, fingerprint-only mode, external
terrain, and terrain baking. See the [command-line guide](../guide/cli.md) for the exact
flags and manifest format.

## `WorldConfig`

These defaults apply when the C# library is called directly:

| Property | Default | Contract and effect |
|---|---:|---|
| `Seed` | `1` | Unsigned 64-bit master seed. Not part of `ConfigHash`; exported separately. |
| `Years` | `300` | Non-negative number of years. Zero builds year-zero state without annual simulation. |
| `StartYear` | `1` | First civic year and event label. |
| `WorldSize` | `4096` | Positive side length in world units. |
| `EastWestPeriodic` | `false` | Makes east and west identical boundaries. |
| `RegionSize` | `128` | Positive region side length, no larger than the world. |
| `TerrainStride` | `256` | Positive power-of-two coarse terrain spacing. |
| `HydrologyStride` | `64` | Positive river-derivation grid spacing. |
| `InitialCivilizations` | `8` | Non-negative founding count request. |
| `Calendar` | 360 days, 4 seasons | Days must be positive and divide evenly into a positive season count. |
| `UnitsPerTravelDay` | `12.0` | Finite positive conversion from abstract map units to travel days. |
| `Terrain` | defaults below | Parameters for the procedural sampler. |
| `TerrainSource` | empty | Provenance of a non-procedural backend. Use `WithTerrain` for raster inputs. |
| `MapRasterResolution` | `256` | Positive presentation resolution. Excluded from `ConfigHash`. |

A periodic world must be divisible by `RegionSize`, `TerrainStride`, and
`HydrologyStride`. The world must also have at least 16 regions per requested founding
civilization. `WorldConfig.MinimumWorldSize` calculates that floor for library callers.

`WorldConfig.WithTerrain(RasterTerrainSampler)` sets both `WorldSize` and `TerrainSource`
from the loaded raster set. Do not assign raster provenance by hand. A custom non-raster
backend must supply its own stable content identity; see [integration](../dev/integration.md).

## Procedural terrain settings

| Property | Default | Meaning |
|---|---:|---|
| `ContinentScale` | `6000.0` | Land/ocean wavelength in world units. Larger values create broader landforms. |
| `RidgeScale` | `1600.0` | Mountain-ridge wavelength. |
| `RainfallScale` | `2800.0` | Regional rainfall wavelength. |
| `TemperatureVarianceScale` | `7000.0` | Broad temperature-noise wavelength. |
| `GeologyScale` | `5200.0` | Geologic-activity wavelength. |
| `LakeScale` | `900.0` | Inland-water noise wavelength. |
| `BaseLandHeight` | `520.0 m` | Maximum base elevation before ridges. |
| `RidgeHeight` | `2400.0 m` | Additional elevation contributed by ridges. |
| `OceanDepth` | `900.0 m` | Deepest generated ocean floor below sea level. |
| `EquatorTemperature` | `29.0 °C` | Mean generated temperature at the map equator. |
| `PolarTemperature` | `−17.0 °C` | Mean generated temperature at the north and south edges. |
| `LapseRate` | `0.0065 °C/m` | Cooling applied above sea level. |

These are library-only settings. The current implementation does not apply dedicated range
validation to them, so the defaults are the supported baseline. A caller changing them is
responsible for finite, meaningful values and for treating the result as a different
simulation configuration.

## What participates in identity

`ConfigHash` covers every simulation-affecting `WorldConfig` field and procedural terrain
setting, plus external terrain provenance. It omits:

- `Seed`, because the export stores it separately;
- `MapRasterResolution`, because it changes presentation sampling rather than history.

System names, order, and cadence have their own `SystemOrderHash`. Exact reproduction needs
the seed, both identities, and compatible engine behavior. A config hash is provenance, not
a promise that different engine versions implement the same model.

## Coordinates and time

Map coordinates are abstract `(x, z)` world units. The generated climate treats the middle
row as the equator and the north and south edges as poles. Historia Extera does not export
geographic latitude and longitude, and `x` should not be described as longitude unless a
consumer explicitly defines that convention.

The civic calendar is an engine abstraction: 360 days divided into four 90-day seasons.
Seasonal temperature uses hemisphere and map position, but it is not calculated from the
exported planet's orbit or axial orientation. There is no local solar time.
