# Cosmology and scientific scope

Historia Extera generates a host galaxy and local planetary system from the world seed. The
model supplies a coherent setting and gives simulated historians a repeatable comet sky. It is
a constrained procedural model, not an orbital, climate, or stellar-evolution simulation.

## Generated layers

`WorldCosmology.From(seed)` produces:

- a spiral or elliptical host galaxy and a site inside its modeled habitable region;
- an M-, K-, G-, or F-type main-sequence star;
- a habitable planet or a habitable moon of a giant;
- optional companion planets and moons;
- a set of eccentric comets;
- the world's spin-axis orientation relative to the galaxy;
- deep-time formation dates and a simplified stellar future.

The galaxy, orientation, and local system use separate deterministic random streams. Adding a
draw to one layer should not reshuffle the others. Cosmology depends on the seed, not run
length or starting-civilization count.

## Physical relations and selection

The local-system pipeline uses broad scaling relations rather than catalog data:

- stellar luminosity follows `L ≈ M^3.5` in solar units;
- stellar radius follows `R ≈ M^0.8`;
- main-sequence lifetime follows `10 / M^2.5` billion years;
- habitable-zone edges scale with the square root of luminosity;
- orbital periods use Kepler's mass-dependent relation;
- rocky-body radius, surface gravity, escape velocity, and mean density are derived from
  sampled mass and iron fraction;
- equilibrium temperature uses luminosity, orbital distance, and Bond albedo, then a sampled
  greenhouse offset is added.

The generator adjusts or rejects candidates until its own checks pass. A host star must have
at least 2 billion years on the main sequence. The inhabited orbit lies inside the generated
habitable zone, escape velocity is at least 7 km/s, and surface temperature is between 273 K
and 343 K. A habitable moon also stays outside its Roche limit and has a tidally locked day no
longer than seven Earth days.

These checks are necessary conditions chosen by this model, not proof of real habitability.
They omit atmospheric chemistry and loss, radiation, oceans, clouds, carbon cycling,
geological history, obliquity cycles, eccentricity-driven seasons, and biological evolution.

Companion placement uses snow-line and mutual-Hill-separation rules. It does not integrate
orbits to establish long-term stability. On a moon world the giant the world orbits is placed
as a companion in its own right, at the habitable orbit and carrying the moon family the world
belongs to; it is exempt from the separation rules against the world, which is inside its Hill
sphere by construction. Comet paths are stored as orbital elements and periods, but the engine
does not perform an n-body integration or calculate an ephemeris from a local observer.

## Galaxy and night-sky rendering

The galaxy model provides morphology, scale, spiral structure where applicable, a habitable
annulus or shell, metallicity, supernova-rate proxy, and the observer's galactocentric site.
These are analytic generated values, not observations of a real galaxy.

The viewer's 480 × 240 night-sky texture uses galactic coordinates:

- galactic longitude is horizontal, from −180° to +180°, with 0° toward the nucleus;
- galactic latitude is vertical, from +90° to −90°;
- unresolved light integrates an analytic stellar-density and dust model over 40 steps to
  24 kpc;
- resolved points are a deterministic sample from luminosity bins with a limiting-magnitude
  proxy of 6.5 and a display cap of 2,200 stars.

The result is a qualitative all-sky galaxy view. It is not a star catalog and does not place
named real stars. Dust, luminosity functions, spiral arms, and apparent brightness are
simplified for a fast seeded rendering.

## Galactic and equatorial coordinates

`CelestialOrientation` gives the world's north celestial pole in galactic longitude and
latitude and defines the zero of right ascension.

- **Galactic longitude and latitude** locate a direction relative to the host galaxy's plane
  and nucleus.
- **Right ascension** is the equatorial angle around the world's spin axis, expressed by the
  API in degrees from 0 to 360.
- **Declination** is angular distance north or south of the world's celestial equator, from
  −90° to +90°.

`ToEquatorial` and `ToGalactic` implement this rotation. The current night-sky renderer does
not use it; the displayed texture stays in galactic coordinates.

`GalacticPlaneInclinationDeg` is currently the complement of the folded pole tilt. Despite
its source comment, it is not a calculated angle to a local observer's horizon. Do not use it
as one; the local-horizon inputs described below are missing.

The engine has no transformation to a local horizon. **Altitude** would be angle above that
horizon, and **azimuth** the compass direction around it. Both would require a surface
observer, geographic latitude and longitude, and sidereal time. Those inputs are absent, so
the current application must not claim a settlement-specific or time-varying local sky.

## Map latitude and world coordinates

Procedural terrain treats the map's center row as an equator and the north and south edges as
poles when deriving temperature. This is a climate convention over abstract `(x, z)` units.
It is not connected to `CelestialOrientation`, the orbital period, or the night-sky panel.

The `x` coordinate wraps only when east/west periodic mode is enabled. The engine does not
export geographic degrees or a prime meridian. Calling map `x` “longitude” would imply a
contract that does not exist.

## Civic time and astronomical time

Recorded history uses a configurable civic calendar, defaulting to 360 days and four
seasons. The exported orbital period is measured in Earth days. The two are not synchronized.

Seasonal simulation cadence and local seasonal temperature are game abstractions. They do
not derive from the generated planet's axial tilt, orbital position, or solar day. Historia
Extera has no local solar time or sidereal-motion model.

Deep-time chronology is kept separately in billions of years. It places galaxy assembly,
stellar enrichment, star formation, world accretion, and the star's future around a fixed
13.8-billion-year universe age. Year 1 remains the beginning of the civic record, not the
formation of the planet.

## Comet returns and historical observations

The engine turns each comet's orbital period into local world years by dividing by the
habitable body's orbital period. A deterministic orbital phase selects return years.

The `Skywatch.Brightness` value is:

```text
nucleus radius / perihelion distance²
```

It is a ranking proxy, not apparent magnitude or photometry. Thresholds classify returns as
faint, notable, or great. Faint returns are only chronicled when their period is at least 25
local years, preventing a dim frequent visitor from filling the record.

An apparition occurs independently of historical events. A realm records it only when an
eligible living scribe or cleric exists and a deterministic chance based on learning,
brightness grade, and war state succeeds. Each realm keeps its own record. The interval in an
observation is time since that realm's previous recorded sighting, so it can cover several
physical returns that went unrecorded.

`SkyClaims` may turn an observation into a prediction or interpretation and later compare a
prediction with the fixed return schedule. The apparent observer, register continuity, and
claim are simulated history. There is no player observation action, telescope, journal UI,
meteor-shower model, or constellation system.

A claim names its subject and, where the claimant stated a number, the quantity they stated. The
true value is not copied onto the claim: the comet's period and the world's year are already on
this page, so the error in a stated period is a division a reader performs and the engine never
does. Nothing derives a score from it. See `DESIGN.md` → Core contracts → Knowledge.

## Coupling to the history model

Most cosmology is descriptive. Galaxy structure, star class, planet mass, and the all-sky
texture do not modify terrain, harvest, travel, politics, or rendering of the map.

Comet apparitions are the current coupling: `ArtifactSystem` asks `Skywatch` to record and
answer them during the opening season. Books can also contain cosmological subject matter.
Keep this distinction visible when extending the model; a value exported on the cosmology
page is not automatically a simulation input.
