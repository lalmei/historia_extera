#!/usr/bin/env -S uv run --no-project --script
# /// script
# requires-python = ">=3.11"
# dependencies = ["worldengine==0.20.0", "numpy>=1.24"]
# ///
"""Convert a WorldEngine world into a Phase 2 terrain raster set.

WorldEngine (MIT, https://github.com/Mindwerks/worldengine) simulates plate tectonics,
erosion and climate and writes its result as a protobuf ``.world`` file. This script reads
that file and writes the interchange the engine consumes: 16-bit PGM planes plus a
``terrain.json`` manifest.

Nothing here is part of HistoryEngine. That is the point — the engine takes no dependency
on the generator, and the generator has never heard of the engine. Everything the two have
to agree on is in the manifest, and every place that agreement needed a human to decide
something is marked ``LEAK`` below, because those decisions are the trial's actual output.

Usage:

    uv run --no-project --script tools/terrain/worldengine_to_raster.py \\
        build/worldengine/trial.world --out build/terrain-worldengine
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys

import numpy as np

# ``worldengine`` reaches its recursion limit walking its own protobuf on large worlds.
sys.setrecursionlimit(10_000)

from worldengine.model.world import World  # noqa: E402

PGM_MAX = 65535


def write_pgm(path: pathlib.Path, plane: np.ndarray) -> None:
    """Write a normalised [0, 1] plane as a binary 16-bit PGM (netpbm P5).

    Big-endian, as netpbm requires regardless of the machine writing it. 16-bit because
    8 bits over a few thousand metres quantises the coastal gradient that decides where
    the engine puts a town.
    """
    if plane.ndim != 2:
        raise ValueError(f"expected a 2-D plane, got shape {plane.shape}")

    quantised = np.rint(np.clip(plane, 0.0, 1.0) * PGM_MAX).astype(">u2")
    height, width = quantised.shape

    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as handle:
        handle.write(f"P5\n{width} {height}\n{PGM_MAX}\n".encode("ascii"))
        handle.write(quantised.tobytes())


def sea_threshold(world: World) -> float:
    """WorldEngine's own shoreline value, in its own unitless elevation scale."""
    for name, value in world.layers["elevation"].thresholds:
        if name == "sea":
            return float(value)
    raise SystemExit("this world has no 'sea' elevation threshold; regenerate with -t full")


def build_height(world: World, args: argparse.Namespace) -> tuple[np.ndarray, dict]:
    """The height plane, plus the manifest spec that gives it units and a datum.

    LEAK — units. WorldEngine's elevation is unitless: this world runs -0.40 to 12.59 with
    the shoreline at 1.00. The manifest requires metres, so the metre scale below is a
    choice made *here*, by us, and no part of it came out of the generator. The defaults
    match Phase 1's procedural sampler so that the comparison isolates the shape of the
    terrain rather than its vertical exaggeration.

    LEAK — land below the datum. WorldEngine decides what is ocean by flooding inward from
    the map border, so a closed basin below the shoreline value is dry land in its model.
    The engine has no way to hear that: ``RasterTerrainSampler`` calls anything below zero
    metres ocean, and the optional water layer only marks *lakes on land*. So the ocean
    mask has to be flattened into the height, which is what ``--basins fill`` does.
    """
    elevation = np.asarray(world.layers["elevation"].data, dtype=np.float64)
    ocean = np.asarray(world.layers["ocean"].data, dtype=bool)
    shoreline = sea_threshold(world)

    drowned = int((~ocean & (elevation < shoreline)).sum())

    if args.basins == "fill":
        # Raise every dry cell to at least the shoreline and hold every ocean cell at or
        # below it, so that "below the datum" and "ocean" become the same statement — the
        # only statement the interchange can carry.
        elevation = np.where(ocean, np.minimum(elevation, shoreline), np.maximum(elevation, shoreline))

    low = float(elevation.min())
    high = float(elevation.max())

    if not low < shoreline < high:
        raise SystemExit(
            f"the shoreline ({shoreline}) is not inside the elevation range "
            f"({low} .. {high}); this world is all land or all ocean."
        )

    plane = (elevation - low) / (high - low)

    spec = {
        "file": "height.pgm",
        "min": args.abyss_metres,
        "max": args.peak_metres,
        "seaLevel": round((shoreline - low) / (high - low), 9),
    }

    report = {
        "rawRange": [low, high],
        "shoreline": shoreline,
        "landBelowShoreline": drowned,
        "basins": args.basins,
        "oceanFraction": float(ocean.mean()),
    }

    return plane, (spec, report)


def normalise(plane: np.ndarray) -> np.ndarray:
    """Rescale a plane onto [0, 1] by its own extremes."""
    low = float(plane.min())
    high = float(plane.max())
    if high <= low:
        return np.zeros_like(plane)
    return (plane - low) / (high - low)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Convert a WorldEngine .world file into a terrain raster set.")
    parser.add_argument("world", type=pathlib.Path, help="WorldEngine .world protobuf")
    parser.add_argument("--out", type=pathlib.Path, required=True, help="output directory")
    parser.add_argument(
        "--world-size", type=int, default=4096,
        help="side length of the square world the rasters cover, in engine units")
    parser.add_argument(
        "--peak-metres", type=float, default=2920.0,
        help="metres at the highest raster value (default matches the procedural sampler)")
    parser.add_argument(
        "--abyss-metres", type=float, default=-900.0,
        help="metres at the lowest raster value")
    parser.add_argument(
        "--temperature-range", type=float, nargs=2, metavar=("MIN", "MAX"),
        default=[-17.0, 29.0],
        help="degrees Celsius at WorldEngine's normalised temperature 0 and 1")
    parser.add_argument(
        "--basins", choices=("fill", "drown"), default="fill",
        help="what to do with dry land below WorldEngine's shoreline value "
             "(fill: raise it to the shoreline; drown: let the engine read it as ocean)")
    parser.add_argument("--no-temperature", action="store_true")
    parser.add_argument("--no-rainfall", action="store_true")
    parser.add_argument("--no-water", action="store_true")
    args = parser.parse_args()

    world = World.open_protobuf(str(args.world))

    if world.width != world.height:
        raise SystemExit(
            f"this world is {world.width}x{world.height}. The manifest carries one "
            "'worldSize', so the raster route only describes square worlds — regenerate "
            "with -x and -y equal.")

    height_plane, (height_spec, report) = build_height(world, args)
    manifest: dict = {"worldSize": args.world_size, "height": height_spec}

    write_pgm(args.out / "height.pgm", height_plane)

    if not args.no_temperature:
        # LEAK — units again. WorldEngine's temperature is a normalised 0..1 field whose
        # biome thresholds are quantiles, not degrees. The manifest will not accept a
        # temperature layer without a Celsius range (0..1 °C is not a world), so the
        # calibration below is ours. It is a real measurement of a modelled quantity
        # reported on an invented scale, which is the most honest thing available.
        raw = np.asarray(world.layers["temperature"].data, dtype=np.float64)
        write_pgm(args.out / "temperature.pgm", np.clip(raw, 0.0, 1.0))
        manifest["temperature"] = {
            "file": "temperature.pgm",
            "min": args.temperature_range[0],
            "max": args.temperature_range[1],
        }
        report["temperatureRaw"] = [float(raw.min()), float(raw.max())]

    if not args.no_rainfall:
        # Precipitation runs -1..1 in WorldEngine and 0..1 in the engine, and it is a
        # normalised density on both sides, so this one rescale needs no invented units.
        raw = np.asarray(world.layers["precipitation"].data, dtype=np.float64)
        write_pgm(args.out / "rainfall.pgm", normalise(raw))
        manifest["rainfall"] = {"file": "rainfall.pgm"}
        report["rainfallRaw"] = [float(raw.min()), float(raw.max())]

    if not args.no_water:
        # A plane of zeros is a different statement from a missing layer: it says this
        # generator looked for lakes, not that nobody can tell you. WorldEngine finds
        # almost none at this resolution — its lakes come out of erosion, which needs a
        # finer grid than a 512-cell world gives it.
        lakes = np.asarray(world.layers["lake_map"].data, dtype=np.float64) > 0.0
        write_pgm(args.out / "water.pgm", lakes.astype(np.float64))
        manifest["water"] = {"file": "water.pgm"}
        report["lakeCells"] = int(lakes.sum())

    manifest_path = args.out / "terrain.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    print(f"WorldEngine {world.width}x{world.height}, seed {world.seed}")
    print(f"  elevation      {report['rawRange'][0]:.3f} .. {report['rawRange'][1]:.3f} "
          f"(unitless), shoreline {report['shoreline']:.3f}")
    print(f"  ocean          {report['oceanFraction'] * 100:.1f}% of the map")
    print(f"  basins         {report['landBelowShoreline']:,} dry cells below the shoreline "
          f"({args.basins})")
    if "lakeCells" in report:
        print(f"  lakes          {report['lakeCells']:,} cells")
    print(f"  datum          {height_spec['seaLevel']:.6f} normalised = 0 m")
    print(f"  written        {manifest_path}")
    print()
    print(f"Run a history over it with:  historia-extera --terrain {manifest_path}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
