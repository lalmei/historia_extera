import type React from 'react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { NarratedEvent } from '../components/EventList';
import { EntityLink } from '../components/common';
import { IconChevronLeft, IconChevronRight, IconMinus, IconPlus, IconRefresh } from '../components/icons';
import {
  housesOnMap,
  landmarkMarks,
  fortifiedFromYear,
  waterMarks,
  wallsStanding,
  waterLabel,
  type HouseMark,
  type HouseOnMap,
} from '../mapLayers';
import { navigate } from '../router';
import type { World } from '../store';
import { buildGrid, buildRealms } from '../territory';
import type { Standing } from '../timeline';
import {
  FLAG_COAST,
  SITE_LABELS,
  type Biome,
  type Civilization,
  type EntityId,
  type HolySite,
  type Region,
  type Settlement,
} from '../types';

/**
 * The 2D world map, in any year of the run.
 *
 * Deliberately knows nothing about where terrain came from. It consumes the byte planes in
 * the export — height, biome, river/coast flags — and Phase 2's real generated terrain or
 * Phase 3's Vintage Story terrain will render here unchanged, because the export shape does
 * not change.
 *
 * Terrain goes to a canvas via ImageData at raster resolution and is then scaled; everything
 * political is a vector overlay on top. That split matters because the raster is a few hundred
 * pixels square while there may be thousands of settlements — and it means the colour ramp
 * lives here, in the viewer, where it can respond to theme rather than being baked into the
 * file. Zoom and pan are a view transform over that same canvas-plus-overlay, not a second
 * rendering of the world.
 *
 * <b>The terrain is the only part that is fixed in time.</b> Borders, towns and battles are
 * replayed from the chronicle for the selected year, so the map answers "what did this world
 * look like in 187?" rather than only "how did it end up?" — which is the question a history
 * of conquest invites and the flat final map cannot answer.
 */

type Layer = 'biome' | 'height' | 'habitability';

/**
 * What the settlement dots are coloured by.
 *
 * Realm and faith are the two political maps of the same world, and they disagree in the
 * interesting places: a frontier province that changed hands but not gods, a realm whose second
 * city follows the enemy's faith. Sharing the dots between them — rather than building a second
 * map — is what makes the disagreement visible in one glance.
 */
type Colouring = 'realm' | 'faith';

/** Years advanced per tick while playing. */
const PLAY_INTERVAL_MS = 110;

const ZOOM_MIN = 1;
const ZOOM_MAX = 8;
const ZOOM_STEP = 1.35;

const BIOME_COLOURS: Record<Biome, [number, number, number]> = {
  Ocean: [42, 74, 105],
  Lake: [74, 120, 158],
  Glacier: [236, 240, 244],
  Tundra: [168, 172, 158],
  Taiga: [70, 100, 82],
  TemperateForest: [70, 118, 74],
  Grassland: [140, 164, 90],
  Steppe: [176, 168, 112],
  Desert: [206, 186, 134],
  Savanna: [186, 170, 96],
  TropicalForest: [46, 106, 62],
  Wetland: [96, 122, 104],
  Alpine: [148, 146, 148],
};

type Hover =
  | { kind: 'settlement'; standing: Standing }
  | { kind: 'ruin'; settlement: Settlement }
  | { kind: 'holy-site'; site: HolySite }
  | { kind: 'harbour'; settlement: Settlement }
  | { kind: 'landmark'; settlement: Settlement }
  | { kind: 'house'; mark: HouseMark }
  | { kind: 'region'; region: Region; owner?: EntityId };

/** One drawn stroke in map units. A wrapped link needs two of them. */
type Stroke = { x1: number; y1: number; x2: number; y2: number };

/**
 * The stroke or strokes that join two points, taking the seam when the world wraps.
 *
 * A link between a town at the eastern edge and one at the western edge is a short hop in a
 * periodic world and the simulation has always treated it as one. Drawn naively it is a line
 * clean across the map — visually the single longest connection in the world, and the only
 * reading that is certainly false. Crossing the seam splits it in two: out through one edge,
 * back in through the other, at the latitude the link actually crosses.
 */
function link(from: Stroke, periodic: boolean): Stroke[] {
  const dx = from.x2 - from.x1;
  if (!periodic || Math.abs(dx) <= 50) return [from];

  // The short way round leaves through the nearer edge: east when the naive line ran west.
  const wrapped = from.x1 + (dx > 0 ? dx - 100 : dx + 100);
  const exit = wrapped < 0 ? 0 : 100;
  const crossing = (exit - from.x1) / (wrapped - from.x1);
  const y = from.y1 + (from.y2 - from.y1) * crossing;

  // A town sitting exactly on the seam leaves the map at the point it starts, so one half has
  // no length. Dropped rather than drawn: a round cap on a zero-length line is a stray dot on
  // the edge of the world.
  return [
    { x1: from.x1, y1: from.y1, x2: exit, y2: y },
    { x1: exit === 0 ? 100 : 0, y1: y, x2: from.x2, y2: from.y2 },
  ].filter((stroke) => stroke.x1 !== stroke.x2 || stroke.y1 !== stroke.y2);
}

function clampZoom(value: number) {
  return Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, value));
}

function clampPan(pan: { x: number; y: number }, zoom: number, size: number) {
  const min = size * (1 - zoom);
  return {
    x: Math.min(0, Math.max(min, pan.x)),
    y: Math.min(0, Math.max(min, pan.y)),
  };
}

function zoomToward(
  nextZoom: number,
  origin: { x: number; y: number },
  currentZoom: number,
  currentPan: { x: number; y: number },
  size: number,
) {
  const zoom = clampZoom(nextZoom);
  const worldX = (origin.x - currentPan.x) / currentZoom;
  const worldY = (origin.y - currentPan.y) / currentZoom;
  return {
    zoom,
    pan: clampPan(
      { x: origin.x - worldX * zoom, y: origin.y - worldY * zoom },
      zoom,
      size,
    ),
  };
}

export function WorldMap({ world }: { world: World }) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const svgRef = useRef<SVGSVGElement | null>(null);
  const viewportRef = useRef<HTMLDivElement | null>(null);
  const frameRef = useRef<HTMLDivElement | null>(null);
  const zoomRef = useRef(1);
  const panRef = useRef({ x: 0, y: 0 });
  const dragRef = useRef<{
    x: number;
    y: number;
    panX: number;
    panY: number;
    moved: boolean;
  } | null>(null);
  const pannedRef = useRef(false);

  const { raster, export: data, timeline } = world;
  const { startYear, endYear } = data.meta;

  const [layer, setLayer] = useState<Layer>('biome');
  const [colouring, setColouring] = useState<Colouring>('realm');
  const [showRivers, setShowRivers] = useState(true);
  const [showTradeRoutes, setShowTradeRoutes] = useState(true);
  const [showRoads, setShowRoads] = useState(true);
  const [showSettlements, setShowSettlements] = useState(true);
  const [showRuins, setShowRuins] = useState(true);
  const [showHolySites, setShowHolySites] = useState(true);
  const [showTerritory, setShowTerritory] = useState(true);
  const [showHarbours, setShowHarbours] = useState(true);
  const [showHouses, setShowHouses] = useState(true);
  const [showWalls, setShowWalls] = useState(true);
  const [showLandmarks, setShowLandmarks] = useState(true);
  const [year, setYear] = useState(endYear);
  const [playing, setPlaying] = useState(false);
  const [focus, setFocus] = useState<EntityId | null>(null);
  const [hovered, setHovered] = useState<Hover | null>(null);
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [side, setSide] = useState(0);

  zoomRef.current = zoom;
  panRef.current = pan;

  useEffect(() => {
    const frame = frameRef.current;
    if (!frame) return;

    const measure = () => {
      const box = frame.getBoundingClientRect();
      setSide(Math.floor(Math.max(0, Math.min(box.width, box.height))));
    };

    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(frame);
    return () => observer.disconnect();
  }, []);

  const grid = useMemo(() => buildGrid(data.world, data.regions), [data.world, data.regions]);
  const order = useMemo(() => data.civilizations.map((civ) => civ.id), [data.civilizations]);

  // A second wheel for faiths, offset from the realm hues so the two colourings are never
  // mistaken for each other at a glance.
  const faithColours = useMemo(() => {
    const colours = new Map<EntityId, string>();
    data.religions.forEach((faith, index) => {
      colours.set(faith.id, `hsl(${(40 + index * 137.508) % 360} 55% 58%)`);
    });
    return colours;
  }, [data.religions]);

  const owners = useMemo(() => timeline.ownersAt(year), [timeline, year]);
  const realms = useMemo(() => buildRealms(grid, owners, order), [grid, owners, order]);
  const standing = useMemo(() => timeline.settlementsAt(year), [timeline, year]);
  const independentHolySites = useMemo(
    () => data.holySites.filter((site) => !site.settlementId && site.foundedYear <= year),
    [data.holySites, year],
  );

  /**
   * Towns that stood in this year and do not any more.
   *
   * They used to leave the map the year they emptied, which quietly made every abandonment
   * invisible — a frontier that was settled and then given up reads exactly like one nobody
   * ever reached. A ruin is also the only way back to that settlement's page from the map.
   */
  const ruins = useMemo(
    () =>
      data.settlements.filter(
        (settlement) =>
          settlement.abandonedYear !== undefined && settlement.abandonedYear <= year,
      ),
    [data.settlements, year],
  );

  const harbours = useMemo(
    () => waterMarks(standing, data.world, raster),
    [standing, data.world, raster],
  );
  const landmarks = useMemo(
    () => landmarkMarks(standing, data.world),
    [standing, data.world],
  );
  const wallsRaised = useMemo(() => fortifiedFromYear(data.events), [data.events]);
  const walls = useMemo(
    () => wallsStanding(standing, year, wallsRaised),
    [standing, year, wallsRaised],
  );
  const houses = useMemo(() => housesOnMap(world, year, focus), [world, year, focus]);
  const ownerOf = useMemo(() => {
    const owners = new Map<EntityId, EntityId>();
    for (const entry of standing) owners.set(entry.settlement.id, entry.civilizationId);
    return (settlementId: EntityId, fallback: EntityId) => owners.get(settlementId) ?? fallback;
  }, [standing]);

  /**
   * Each route's traffic in the selected year, from the yearly series.
   *
   * The export's own `traffic` field is the final year's, and drawing an early map with it
   * would leak the future into the past — which is why these lines were a constant width until
   * the engine began sampling. A route with no series (an older file) simply has no width to
   * vary and falls back to the flat stroke.
   */
  const trafficAt = useMemo(() => {
    const now = new Map<EntityId, number>();

    for (const route of data.tradeRoutes) {
      const series = world.seriesFor(route.id).find((one) => one.metric === 'traffic');
      if (!series) continue;

      const index = year - series.fromYear;
      if (index >= 0 && index < series.values.length) now.set(route.id, series.values[index]);
    }

    return now;
  }, [data.tradeRoutes, world, year]);

  const tradeRoutes = useMemo(
    () =>
      data.tradeRoutes
        .filter(
          (route) =>
            route.foundedYear <= year &&
            (route.endedYear === undefined || route.endedYear >= year),
        )
        .map((route) => ({
          route,
          a: data.settlements.find((settlement) => settlement.id === route.settlementAId),
          b: data.settlements.find((settlement) => settlement.id === route.settlementBId),
        }))
        .filter((entry) => entry.a !== undefined && entry.b !== undefined),
    [data.tradeRoutes, data.settlements, year],
  );

  /**
   * The roads standing in the selected year.
   *
   * Replayed from the year the way was cut, exactly as territory is replayed from transfers: a
   * map of the second century must not show a road nobody had built yet. A road outlives the
   * commerce that paid for it — an abandoned way is still a way — so a closed route keeps its
   * road on the map and is merely drawn faint.
   *
   * The one thing that cannot be replayed is the *line* of a road before it was paved: the export
   * keeps the way as it now stands, not every line it ever took, so a road engineered in year 400
   * is drawn on its final course in year 300 as well. Its grade is replayed, which is the part a
   * reader can see.
   */
  const roads = useMemo(
    () =>
      data.tradeRoutes
        .filter((route) => route.road !== undefined && route.road.builtYear <= year)
        .map((route) => ({
          route,
          road: route.road!,
          paved: route.road!.pavedYear !== undefined && route.road!.pavedYear <= year,
          abandoned: route.endedYear !== undefined && route.endedYear < year,
        })),
    [data.tradeRoutes, year],
  );

  const battles = useMemo(
    () =>
      timeline.battlesIn(year).map((battle) => {
        // A battle is fought across a region rather than at a point, so it is marked at the
        // middle of one.
        const region = world.byId.get(battle.regionId) as Region | undefined;

        return {
          id: battle.id,
          x: region ? region.minX + region.width / 2 : data.world.minX,
          z: region ? region.minZ + region.height / 2 : data.world.minZ,
        };
      }),
    [timeline, year, world, data.world],
  );

  const yearEvents = useMemo(() => {
    const indices = data.indices.eventsByYear[String(year)] ?? [];
    return indices
      .map((index) => data.events[index])
      .filter((event) => event.significance !== 'Routine');
  }, [data, year]);

  // Playback stops itself at the end of the run rather than looping: a chronicle has an end,
  // and a map that silently restarts reads as one that never moved.
  useEffect(() => {
    if (!playing) return;

    const timer = setInterval(() => {
      setYear((current) => {
        if (current >= endYear) {
          setPlaying(false);
          return current;
        }

        return current + 1;
      });
    }, PLAY_INTERVAL_MS);

    return () => clearInterval(timer);
  }, [playing, endYear]);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const context = canvas.getContext('2d');
    if (!context) return;

    const { resolution } = raster;
    const image = context.createImageData(resolution, resolution);

    const span = Math.max(1e-6, raster.maxHeight - raster.minHeight);
    // Where sea level sits in the quantised 0–255 range.
    const seaByte = ((0 - raster.minHeight) / span) * 255;

    for (let i = 0; i < resolution * resolution; i++) {
      const heightByte = raster.height[i];
      const biome = raster.biomeAt(i);
      const submerged = heightByte < seaByte;

      let r: number;
      let g: number;
      let b: number;

      if (layer === 'height') {
        if (submerged) {
          const depth = seaByte <= 0 ? 0 : heightByte / seaByte;
          r = 18 + depth * 30;
          g = 40 + depth * 50;
          b = 78 + depth * 60;
        } else {
          const above = (heightByte - seaByte) / Math.max(1, 255 - seaByte);
          r = 96 + above * 150;
          g = 120 + above * 110;
          b = 84 + above * 100;
        }
      } else if (layer === 'habitability') {
        if (submerged) {
          [r, g, b] = [34, 52, 72];
        } else {
          // Painted from biome habitability as a stand-in, since per-pixel scores
          // are a region-level quantity rather than a raster plane.
          const habitable = !['Ocean', 'Lake', 'Glacier', 'Alpine'].includes(biome);
          const warm = ['Grassland', 'TemperateForest', 'Savanna', 'TropicalForest'].includes(
            biome,
          );
          if (!habitable) [r, g, b] = [110, 110, 116];
          else if (warm) [r, g, b] = [96, 152, 84];
          else [r, g, b] = [158, 150, 104];
        }
      } else {
        [r, g, b] = BIOME_COLOURS[biome] ?? [120, 120, 120];
        // Shade by elevation so relief reads through flat biome colour.
        if (!submerged) {
          const relief = 0.82 + ((heightByte - seaByte) / Math.max(1, 255 - seaByte)) * 0.42;
          r = Math.min(255, r * relief);
          g = Math.min(255, g * relief);
          b = Math.min(255, b * relief);
        }
      }

      if ((raster.flags[i] & FLAG_COAST) !== 0) {
        r = Math.min(255, r * 0.82);
        g = Math.min(255, g * 0.86);
        b = Math.min(255, b * 0.94);
      }

      const offset = i * 4;
      image.data[offset] = r;
      image.data[offset + 1] = g;
      image.data[offset + 2] = b;
      image.data[offset + 3] = 255;
    }

    // Draw the raster at its own resolution, then scale up with smoothing off so
    // the data stays legible rather than being blurred into mush.
    const scratch = document.createElement('canvas');
    scratch.width = resolution;
    scratch.height = resolution;
    scratch.getContext('2d')?.putImageData(image, 0, 0);

    context.imageSmoothingEnabled = false;
    context.clearRect(0, 0, canvas.width, canvas.height);
    context.drawImage(scratch, 0, 0, canvas.width, canvas.height);
  }, [raster, layer]);

  const toWorld = (value: number, axis: 'x' | 'z') =>
    axis === 'x'
      ? ((value - data.world.minX) / data.world.width) * 100
      : ((value - data.world.minZ) / data.world.height) * 100;

  /**
   * What lies under the cursor, resolved from the pointer position rather than from per-shape
   * handlers: a settlement dot sits on top of its own region, and two overlapping sets of
   * mouse handlers fight over which one is "hovered".
   *
   * <b>Nearest wins, across all three kinds of marker.</b> Taking each kind in turn and stopping
   * at the first hit inside its radius sounds equivalent and is not: the pick radii are several
   * times the size of the marks, so whichever kind is tested first swallows everything near it.
   * Ruins are the case that proves it — a town is usually abandoned in a district with other
   * towns in it, so every ruin on the map sits within a dot's radius and none of them could be
   * hovered or clicked at all. Ties go to the living, by the order these are considered.
   */
  const probe = (event: { clientX: number; clientY: number }) => {
    const box = svgRef.current?.getBoundingClientRect();
    if (!box) return;

    const x = ((event.clientX - box.left) / box.width) * 100;
    const y = ((event.clientY - box.top) / box.height) * 100;

    const within: { hover: Hover; distance: number }[] = [];

    const consider = (hover: Hover, worldX: number, worldZ: number, radius: number) => {
      const dx = toWorld(worldX, 'x') - x;
      const dy = toWorld(worldZ, 'z') - y;
      const distance = dx * dx + dy * dy;

      if (distance <= radius * radius) within.push({ hover, distance });
    };

    if (showSettlements) {
      for (const entry of standing) {
        consider({ kind: 'settlement', standing: entry }, entry.settlement.x, entry.settlement.z, 1.6);
      }
    }

    if (showRuins) {
      for (const settlement of ruins) {
        consider({ kind: 'ruin', settlement }, settlement.x, settlement.z, 1.4);
      }
    }

    if (showHolySites) {
      for (const site of independentHolySites) {
        consider({ kind: 'holy-site', site }, site.x, site.z, 1.5);
      }
    }

    const considerMap = (hover: Hover, mx: number, my: number, radius: number) => {
      const dx = mx - x;
      const dy = my - y;
      const distance = dx * dx + dy * dy;
      if (distance <= radius * radius) within.push({ hover, distance });
    };

    if (showHarbours) {
      for (const mark of harbours) {
        considerMap({ kind: 'harbour', settlement: mark.settlement }, mark.mx, mark.my, 1.35);
      }
    }

    if (showLandmarks) {
      for (const mark of landmarks) {
        considerMap({ kind: 'landmark', settlement: mark.settlement }, mark.mx, mark.my, 1.2);
      }
    }

    if (showHouses) {
      for (const house of houses) {
        for (const mark of house.marks) {
          considerMap({ kind: 'house', mark }, mark.mx, mark.my, 1.45);
        }
      }
    }

    if (within.length > 0) {
      let nearest = within[0];
      for (const candidate of within) {
        if (candidate.distance < nearest.distance) nearest = candidate;
      }

      setHovered(nearest.hover);
      return;
    }

    const region = grid.atPoint(x, y);
    setHovered(region ? { kind: 'region', region, owner: owners.get(region.id) } : null);
  };

  const click = () => {
    if (!hovered) return;

    if (hovered.kind === 'settlement') {
      navigate(`/${hovered.standing.settlement.id}`);
      return;
    }

    if (hovered.kind === 'ruin') {
      navigate(`/${hovered.settlement.id}`);
      return;
    }

    if (hovered.kind === 'holy-site') {
      navigate(`/${hovered.site.id}`);
      return;
    }

    if (hovered.kind === 'harbour' || hovered.kind === 'landmark') {
      navigate(`/${hovered.settlement.id}`);
      return;
    }

    if (hovered.kind === 'house') {
      navigate(`/${hovered.mark.house.id}`);
      return;
    }

    // Clicking bare ground focuses whoever holds it, and clicking the sea clears the focus —
    // which is the gesture people try first and otherwise does nothing at all.
    setFocus(hovered.owner && hovered.owner !== focus ? hovered.owner : null);
  };

  const applyZoom = (nextZoom: number, origin?: { x: number; y: number }) => {
    const viewport = viewportRef.current;
    const size = viewport?.clientWidth ?? 0;
    const point = origin ?? { x: size / 2, y: size / 2 };
    const next = zoomToward(nextZoom, point, zoomRef.current, panRef.current, size);
    setZoom(next.zoom);
    setPan(next.pan);
  };

  const resetView = () => {
    setZoom(1);
    setPan({ x: 0, y: 0 });
  };

  useEffect(() => {
    const viewport = viewportRef.current;
    if (!viewport) return;

    const onWheel = (event: WheelEvent) => {
      if ((event.target as HTMLElement | null)?.closest('.he-map-chrome')) return;
      event.preventDefault();
      const box = viewport.getBoundingClientRect();
      const factor = event.deltaY < 0 ? ZOOM_STEP : 1 / ZOOM_STEP;
      applyZoom(zoomRef.current * factor, {
        x: event.clientX - box.left,
        y: event.clientY - box.top,
      });
    };

    viewport.addEventListener('wheel', onWheel, { passive: false });
    return () => viewport.removeEventListener('wheel', onWheel);
  }, []);

  const onPointerDown = (event: React.PointerEvent<SVGSVGElement>) => {
    if (event.button !== 0) return;
    viewportRef.current?.focus({ preventScroll: true });
    if (zoomRef.current <= 1) return;
    dragRef.current = {
      x: event.clientX,
      y: event.clientY,
      panX: panRef.current.x,
      panY: panRef.current.y,
      moved: false,
    };
    pannedRef.current = false;
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const onPointerMove = (event: React.PointerEvent<SVGSVGElement>) => {
    probe(event);
    const drag = dragRef.current;
    if (!drag) return;

    const dx = event.clientX - drag.x;
    const dy = event.clientY - drag.y;
    if (!drag.moved && dx * dx + dy * dy < 16) return;

    drag.moved = true;
    pannedRef.current = true;
    const size = viewportRef.current?.clientWidth ?? 0;
    setPan(clampPan({ x: drag.panX + dx, y: drag.panY + dy }, zoomRef.current, size));
  };

  const onPointerUp = () => {
    dragRef.current = null;
  };

  const onMapClick = () => {
    if (pannedRef.current) {
      pannedRef.current = false;
      return;
    }
    click();
  };

  const dimmed = (civilizationId: EntityId) => focus !== null && focus !== civilizationId;
  const focused = focus ? data.civilizations.find((civ) => civ.id === focus) : undefined;

  return (
    <div className="flex h-full min-h-0 w-full flex-1 flex-col lg:flex-row">
      <h1 className="sr-only">Map</h1>
      <div
        ref={frameRef}
        className="relative flex min-h-0 min-w-0 flex-1 items-center justify-center overflow-hidden bg-[var(--canvas)]"
      >
      <div
        ref={viewportRef}
        tabIndex={0}
        style={side > 0 ? { width: side, height: side } : undefined}
        className="he-map-viewport relative overflow-hidden border border-[var(--rule)] bg-[var(--canvas)] outline-none"
        onKeyDown={(event) => {
          if (event.key === '+' || event.key === '=') {
            event.preventDefault();
            applyZoom(zoomRef.current * ZOOM_STEP);
          } else if (event.key === '-' || event.key === '_') {
            event.preventDefault();
            applyZoom(zoomRef.current / ZOOM_STEP);
          } else if (event.key === '0' || event.key === 'Escape') {
            event.preventDefault();
            resetView();
          }
        }}
      >
          <div
            className="absolute inset-0 origin-top-left will-change-transform"
            style={{ transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})` }}
          >
          <canvas
            ref={canvasRef}
            width={1024}
            height={1024}
            className="absolute inset-0 h-full w-full"
          />

          <svg
            ref={svgRef}
            viewBox="0 0 100 100"
            preserveAspectRatio="none"
            className={`absolute inset-0 h-full w-full ${
              zoom > 1 ? 'cursor-grab active:cursor-grabbing' : 'cursor-crosshair'
            }`}
            onPointerDown={onPointerDown}
            onPointerMove={onPointerMove}
            onPointerUp={onPointerUp}
            onPointerCancel={onPointerUp}
            onMouseLeave={() => {
              if (!dragRef.current) setHovered(null);
            }}
            onClick={onMapClick}
          >
            {showTerritory &&
              realms.map((realm) => (
                <path
                  key={`fill-${realm.civilizationId}`}
                  d={realm.fill}
                  fill={world.colourOf(realm.civilizationId)}
                  fillOpacity={dimmed(realm.civilizationId) ? 0.1 : 0.3}
                />
              ))}

            {showRivers &&
              data.world.rivers.map((reach, index) => (
                <line
                  key={index}
                  x1={toWorld(reach.x1, 'x')}
                  y1={toWorld(reach.z1, 'z')}
                  x2={toWorld(reach.x2, 'x')}
                  y2={toWorld(reach.z2, 'z')}
                  stroke="rgb(96 148 196)"
                  strokeWidth={0.22 + Math.sqrt(reach.strength) * 1.1}
                  strokeLinecap="round"
                  strokeOpacity={0.9}
                />
              ))}

            {/* Borders over the rivers: a frontier that follows a river is the interesting
                case, and underneath the water it is the one you cannot see. */}
            {showTerritory &&
              realms.map((realm) => (
                <path
                  key={`border-${realm.civilizationId}`}
                  d={realm.border}
                  fill="none"
                  stroke={world.colourOf(realm.civilizationId)}
                  strokeWidth={focus === realm.civilizationId ? 0.62 : 0.34}
                  strokeOpacity={dimmed(realm.civilizationId) ? 0.35 : 1}
                  strokeLinecap="round"
                  strokeLinejoin="round"
                />
              ))}

            {/* The made ways, under the links that paid for them. Drawn before the trade lines so
                a logical link sits over its own road rather than the other way round, and drawn
                solid because that is the whole distinction from the dashed links: these are
                geometry the engine calculated rather than demand it inferred. A road on a route
                that has since closed is still there — the ground does not forget a road — and is
                drawn faint. */}
            {showRoads &&
              roads.map(({ route, road, paved, abandoned }) => {
                const traffic = trafficAt.get(route.id);
                const carried = traffic === undefined ? 0.34 : 0.24 + traffic * 0.55;
                const width = carried * (paved ? 1.5 : 1);
                const strokes: Stroke[] = [];

                for (let i = 2; i < road.points.length; i += 2) {
                  strokes.push(
                    ...link(
                      {
                        x1: toWorld(road.points[i - 2], 'x'),
                        y1: toWorld(road.points[i - 1], 'z'),
                        x2: toWorld(road.points[i], 'x'),
                        y2: toWorld(road.points[i + 1], 'z'),
                      },
                      data.world.eastWestPeriodic,
                    ),
                  );
                }

                return strokes.map((stroke, index) => (
                  <line
                    key={`road-${route.id}-${index}`}
                    x1={stroke.x1}
                    y1={stroke.y1}
                    x2={stroke.x2}
                    y2={stroke.y2}
                    stroke={paved ? 'rgb(226 206 170)' : 'rgb(158 128 88)'}
                    strokeWidth={width}
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeOpacity={abandoned ? 0.3 : 0.9}
                    className="pointer-events-none"
                  >
                    <title>
                      {paved ? 'Paved road' : 'Road'} of the {world.nameOf(route.id)}, cut in{' '}
                      {road.builtYear}
                      {paved && road.pavedYear !== undefined && `, paved in ${road.pavedYear}`}
                      {` · ${Math.round(road.length)} units of made way`}
                      {abandoned && ' · the route it served has closed'}
                    </title>
                  </line>
                ));
              })}

            {/* Logical economic links, deliberately straight: they show demand between two towns,
                not the ground between them. Where the traffic earned a road, the road is drawn
                underneath and is the line that follows the country. Width is this year's traffic —
                from the yearly series, so scrubbing back never shows a corridor thriving before
                it did. A link across a periodic world's seam is drawn the short way, in two
                strokes, because that is the way the goods went. */}
            {showTradeRoutes &&
              tradeRoutes.map(({ route, a, b }) => {
                const traffic = trafficAt.get(route.id);

                return link(
                  {
                    x1: toWorld(a!.x, 'x'),
                    y1: toWorld(a!.z, 'z'),
                    x2: toWorld(b!.x, 'x'),
                    y2: toWorld(b!.z, 'z'),
                  },
                  data.world.eastWestPeriodic,
                ).map((stroke, index) => (
                  <line
                    key={`${route.id}-${index}`}
                    x1={stroke.x1}
                    y1={stroke.y1}
                    x2={stroke.x2}
                    y2={stroke.y2}
                    stroke={
                      route.mode === 'River'
                        ? 'rgb(82 126 168)'
                        : route.mode === 'Coastal'
                          ? 'rgb(72 142 150)'
                          : 'rgb(188 132 68)'
                    }
                    strokeWidth={traffic === undefined ? 0.38 : 0.18 + traffic * 0.62}
                    strokeDasharray={route.mode === 'Overland' ? '1.2 0.7' : undefined}
                    strokeLinecap="round"
                    strokeOpacity={0.72}
                    className="pointer-events-none"
                  >
                    <title>
                      {world.nameOf(route.id)} · logical {route.mode.toLowerCase()} connection
                      {traffic !== undefined && ` · traffic ${traffic.toFixed(2)} in ${year}`}
                      {route.road === undefined
                        ? '; no road was ever built for it'
                        : `; road cut in ${route.road.builtYear}`}
                    </title>
                  </line>
                ));
              })}

            {/* A ruling house that does not sit where it rose: the line is the distance
                between blood and the throne, and is the interesting case. Same wrap as
                trade, because a house whose homeland is across the seam is a neighbour. */}
            {showHouses &&
              houses.map((entry) => {
                if (!entry.link) return null;
                const faded = dimmed(entry.civId);

                return link(
                  {
                    x1: toWorld(entry.link.from.x, 'x'),
                    y1: toWorld(entry.link.from.z, 'z'),
                    x2: toWorld(entry.link.to.x, 'x'),
                    y2: toWorld(entry.link.to.z, 'z'),
                  },
                  data.world.eastWestPeriodic,
                ).map((stroke, index) => (
                  <line
                    key={`${entry.house.id}-home-${index}`}
                    x1={stroke.x1}
                    y1={stroke.y1}
                    x2={stroke.x2}
                    y2={stroke.y2}
                    stroke={world.colourOf(entry.civId)}
                    strokeWidth={0.22}
                    strokeDasharray="0.7 0.55"
                    strokeLinecap="round"
                    strokeOpacity={faded ? 0.18 : 0.45}
                    className="pointer-events-none"
                  />
                ));
              })}

            {/* Ruins, under the living dots. A mark rather than a circle, so an empty place can
                never be read as an inhabited one at a glance, and in plain ink rather than a
                realm colour, because a ruin belongs to nobody. */}
            {showRuins &&
              ruins.map((settlement) => {
                const x = toWorld(settlement.x, 'x');
                const y = toWorld(settlement.z, 'z');
                const arm = 0.52;

                return (
                  <g
                    key={settlement.id}
                    className="pointer-events-none"
                    stroke="rgb(152 146 138)"
                    strokeWidth={0.24}
                    strokeLinecap="round"
                    strokeOpacity={0.9}
                  >
                    <title>
                      {settlement.name} · abandoned in {settlement.abandonedYear}
                    </title>
                    <line x1={x - arm} y1={y - arm} x2={x + arm} y2={y + arm} />
                    <line x1={x - arm} y1={y + arm} x2={x + arm} y2={y - arm} />
                  </g>
                );
              })}

            {showSettlements &&
              standing.map((entry) => (
                <circle
                  key={entry.settlement.id}
                  cx={toWorld(entry.settlement.x, 'x')}
                  cy={toWorld(entry.settlement.z, 'z')}
                  r={radiusOf(entry)}
                  fill={
                    colouring === 'faith'
                      ? entry.religionId
                        ? faithColours.get(entry.religionId) ?? 'rgb(150 150 150)'
                        : 'rgb(120 120 124)'
                      : world.colourOf(entry.civilizationId)
                  }
                  fillOpacity={dimmed(entry.civilizationId) ? 0.35 : 1}
                  stroke="rgba(12,12,12,0.75)"
                  strokeWidth={entry.isCapital ? 0.3 : 0.14}
                  strokeOpacity={dimmed(entry.civilizationId) ? 0.35 : 1}
                />
              ))}

            {showWalls &&
              walls.map((entry) => {
                const x = toWorld(entry.settlement.x, 'x');
                const y = toWorld(entry.settlement.z, 'z');
                const r = radiusOf(entry) + 0.38;
                return (
                  <rect
                    key={`wall-${entry.settlement.id}`}
                    x={x - r}
                    y={y - r}
                    width={r * 2}
                    height={r * 2}
                    fill="none"
                    stroke="rgb(72 68 62)"
                    strokeWidth={0.2}
                    strokeOpacity={dimmed(entry.civilizationId) ? 0.3 : 0.9}
                    className="pointer-events-none"
                  />
                );
              })}

            {showHarbours &&
              harbours.map((mark) =>
                mark.water === 'sea' ? (
                  <AnchorMark
                    key={`harbour-${mark.settlement.id}`}
                    x={mark.mx}
                    y={mark.my}
                    faded={dimmed(ownerOf(mark.settlement.id, mark.settlement.civilizationId))}
                  />
                ) : (
                  <WaveMark
                    key={`river-${mark.settlement.id}`}
                    x={mark.mx}
                    y={mark.my}
                    faded={dimmed(ownerOf(mark.settlement.id, mark.settlement.civilizationId))}
                  />
                ),
              )}

            {showLandmarks &&
              landmarks.map((mark) =>
                mark.site === 'Mine' ? (
                  <MineMark
                    key={`mine-${mark.settlement.id}`}
                    x={mark.mx}
                    y={mark.my}
                    faded={dimmed(ownerOf(mark.settlement.id, mark.settlement.civilizationId))}
                  />
                ) : (
                  <PassMark
                    key={`pass-${mark.settlement.id}`}
                    x={mark.mx}
                    y={mark.my}
                    faded={dimmed(ownerOf(mark.settlement.id, mark.settlement.civilizationId))}
                  />
                ),
              )}

            {showHouses &&
              houses.map((entry) => (
                <g key={`house-${entry.house.id}-${entry.civId}`}>
                  {entry.marks.map((mark) => (
                    <HouseGlyph
                      key={`${mark.role}-${mark.settlement.id}`}
                      mark={mark}
                      colour={world.colourOf(entry.civId)}
                      faded={dimmed(entry.civId)}
                    />
                  ))}
                </g>
              ))}

            {showHolySites &&
              independentHolySites.map((site) => {
                const x = toWorld(site.x, 'x');
                const y = toWorld(site.z, 'z');
                return (
                  <rect
                    key={site.id}
                    x={x - 0.48}
                    y={y - 0.48}
                    width={0.96}
                    height={0.96}
                    rx={0.1}
                    fill={faithColours.get(site.religionId) ?? 'rgb(238 204 112)'}
                    stroke="rgba(12,12,12,0.85)"
                    strokeWidth={0.18}
                    transform={`rotate(45 ${x} ${y})`}
                    className="pointer-events-none"
                  />
                );
              })}

            {/* Battles fought this year. Only ever a handful, and they are the reason to
                scrub to a particular year at all. */}
            {battles.map((battle) => (
              <g key={battle.id} className="pointer-events-none">
                <circle
                  cx={toWorld(battle.x, 'x')}
                  cy={toWorld(battle.z, 'z')}
                  r={1.9}
                  fill="none"
                  stroke="rgb(214 96 84)"
                  strokeWidth={0.35}
                  strokeOpacity={0.9}
                />
                <circle
                  cx={toWorld(battle.x, 'x')}
                  cy={toWorld(battle.z, 'z')}
                  r={0.5}
                  fill="rgb(214 96 84)"
                />
              </g>
            ))}
          </svg>
          </div>

          {hovered && (
            <div className="pointer-events-none absolute top-2 left-2 z-10 max-w-[min(85%,20rem)] rounded border border-[var(--rule)] bg-[var(--input)] px-2.5 py-1.5 text-xs">
              {hovered.kind === 'settlement' ? (
                <>
                  <div className="text-sm font-medium">{hovered.standing.settlement.name}</div>
                  <div className="text-[var(--ink-faint)]">
                    {hovered.standing.isCapital ? 'Seat of ' : ''}
                    {hovered.standing.tier} · {world.nameOf(hovered.standing.civilizationId)}
                    {hovered.standing.religionId &&
                      ` · ${world.nameOf(hovered.standing.religionId)}`}
                  </div>
                </>
              ) : hovered.kind === 'ruin' ? (
                <>
                  <div className="text-sm font-medium">{hovered.settlement.name}</div>
                  <div className="text-[var(--ink-faint)]">
                    Ruins · abandoned in {hovered.settlement.abandonedYear} · once{' '}
                    {hovered.settlement.peakPopulation.toLocaleString()} people
                  </div>
                </>
              ) : hovered.kind === 'holy-site' ? (
                <>
                  <div className="text-sm font-medium">{hovered.site.name}</div>
                  <div className="text-[var(--ink-faint)]">
                    {hovered.site.kind} · {world.nameOf(hovered.site.religionId)} · independent site
                  </div>
                </>
              ) : hovered.kind === 'harbour' ? (
                <>
                  <div className="text-sm font-medium">{hovered.settlement.name}</div>
                  <div className="text-[var(--ink-faint)]">
                    {hovered.settlement.site === 'Harbour' ? 'Harbour' : hovered.settlement.site}
                    {' · '}
                    {waterLabel(hovered.settlement.site)}
                  </div>
                </>
              ) : hovered.kind === 'landmark' ? (
                <>
                  <div className="text-sm font-medium">{hovered.settlement.name}</div>
                  <div className="text-[var(--ink-faint)]">
                    {SITE_LABELS[hovered.settlement.site]}
                  </div>
                </>
              ) : hovered.kind === 'house' ? (
                <>
                  <div className="text-sm font-medium">{hovered.mark.house.name}</div>
                  <div className="text-[var(--ink-faint)]">
                    {houseHover(hovered.mark, world)}
                  </div>
                </>
              ) : (
                <>
                  <div className="text-sm font-medium">{hovered.region.name}</div>
                  <div className="text-[var(--ink-faint)]">
                    {hovered.region.biome} ·{' '}
                    {hovered.owner ? world.nameOf(hovered.owner) : 'unclaimed'}
                  </div>
                </>
              )}
            </div>
          )}

          <div
            className="he-map-chrome absolute right-2 bottom-2 z-10 flex flex-col overflow-hidden"
            onWheel={(event) => event.stopPropagation()}
          >
            <button
              type="button"
              title="Zoom in"
              aria-label="Zoom in"
              disabled={zoom >= ZOOM_MAX}
              onClick={() => applyZoom(zoomRef.current * ZOOM_STEP)}
              className="he-map-zoom-btn"
            >
              <IconPlus />
            </button>
            <button
              type="button"
              title="Reset view"
              aria-label="Reset view"
              disabled={zoom === 1}
              onClick={resetView}
              className="he-map-zoom-btn"
            >
              <IconRefresh />
            </button>
            <button
              type="button"
              title="Zoom out"
              aria-label="Zoom out"
              disabled={zoom <= ZOOM_MIN}
              onClick={() => applyZoom(zoomRef.current / ZOOM_STEP)}
              className="he-map-zoom-btn"
            >
              <IconMinus />
            </button>
          </div>

          <div
            className="he-map-chrome absolute bottom-2 left-2 z-10 w-[min(22rem,calc(100%-4.5rem))] px-2 py-1.5"
            onWheel={(event) => event.stopPropagation()}
          >
            <YearScrubber
              year={year}
              startYear={startYear}
              endYear={endYear}
              playing={playing}
              onYear={(next) => {
                setPlaying(false);
                setYear(next);
              }}
              onPlay={() => {
                if (year >= endYear) setYear(startYear);
                setPlaying(!playing);
              }}
            />
          </div>
        </div>
      </div>

      <aside className="he-map-inspector flex max-h-[42vh] w-full shrink-0 flex-col overflow-y-auto border-t border-[var(--rule)] bg-[var(--surface-container-low)] lg:max-h-none lg:w-80 lg:border-t-0 lg:border-l">
        <div className="border-b border-[var(--rule)] px-4 py-3">
          <div className="he-label">Map layers</div>
          <p className="mt-1 text-sm text-[var(--ink-soft)]">
            {data.world.width.toLocaleString()} × {data.world.height.toLocaleString()} units
            {data.world.eastWestPeriodic ? ' · east/west joined' : ''}
          </p>
          <p className="he-data mt-0.5 text-[11px] text-[var(--ink-faint)]">
            raster {raster.resolution}² · {realms.length}{' '}
            {realms.length === 1 ? 'realm' : 'realms'} · {standing.length} settlements
            {ruins.length > 0 ? ` · ${ruins.length} ruins` : ''}
          </p>
        </div>

        <div className="space-y-4 px-4 py-4 text-sm">
          <section>
            <div className="he-label mb-2">Terrain</div>
            <select
              value={layer}
              onChange={(event) => setLayer(event.target.value as Layer)}
              className="w-full rounded border border-[var(--rule)] bg-[var(--surface-container-high)] px-2 py-1.5 text-sm"
            >
              <option value="biome">Biome</option>
              <option value="height">Elevation</option>
              <option value="habitability">Habitability</option>
            </select>
          </section>

          <section>
            <div className="he-label mb-2">Settlement colour</div>
            <select
              value={colouring}
              onChange={(event) => setColouring(event.target.value as Colouring)}
              className="w-full rounded border border-[var(--rule)] bg-[var(--surface-container-high)] px-2 py-1.5 text-sm"
            >
              <option value="realm">By realm</option>
              <option value="faith">By faith</option>
            </select>
          </section>

          <section>
            <div className="he-label mb-2">Overlays</div>
            <div className="grid grid-cols-2 gap-x-3 gap-y-1.5">
              <Toggle label="Territory" on={showTerritory} onChange={setShowTerritory} />
              <Toggle label="Rivers" on={showRivers} onChange={setShowRivers} />
              <Toggle label="Settlements" on={showSettlements} onChange={setShowSettlements} />
              <Toggle label="Ruins" on={showRuins} onChange={setShowRuins} />
              <Toggle label="Holy sites" on={showHolySites} onChange={setShowHolySites} />
              <Toggle label="Harbours" on={showHarbours} onChange={setShowHarbours} />
              <Toggle label="Walls" on={showWalls} onChange={setShowWalls} />
              <Toggle label="Landmarks" on={showLandmarks} onChange={setShowLandmarks} />
              <Toggle label="Houses" on={showHouses} onChange={setShowHouses} />
              <Toggle label="Trade routes" on={showTradeRoutes} onChange={setShowTradeRoutes} />
              <Toggle label="Roads" on={showRoads} onChange={setShowRoads} />
            </div>
            {showHarbours && (
              <p className="mt-2 text-xs text-[var(--ink-faint)]">
                Anchors sit in the water a harbour was founded for, not on the town. A wave marks a
                river landing.
              </p>
            )}
            {showHouses && (
              <p className="mt-2 text-xs text-[var(--ink-faint)]">
                A banner is the throne; a house mark is where the line rose. Focus a realm to see
                where that house's living members reside. Residence is recorded as of the
                chronicle's end.
              </p>
            )}
            {showTradeRoutes && (
              <p className="mt-2 text-xs text-[var(--ink-faint)]">
                Dashed amber is overland demand, blue uses river access, teal the coast. Weight is
                traffic in {year}. These are logical links; the road under one, where there is one,
                is drawn separately.
              </p>
            )}
            {showRoads && (
              <p className="mt-2 text-xs text-[var(--ink-faint)]">
                Solid brown is a way worn between two towns; pale stone is one bridged and paved.
                Only land routes whose traffic held up for long enough are ever roaded, and a road
                stays on the map after the trade that paid for it has gone.
              </p>
            )}
          </section>

          <section>
            <div className="he-label mb-2">Key</div>
            <MarkerKey
              settlements={showSettlements}
              ruins={showRuins && ruins.length > 0}
              holySites={showHolySites && independentHolySites.length > 0}
              battles={battles.length > 0}
              harbours={showHarbours && harbours.some((mark) => mark.water === 'sea')}
              riversides={showHarbours && harbours.some((mark) => mark.water === 'river')}
              walls={showWalls && walls.length > 0}
              mines={showLandmarks && landmarks.some((mark) => mark.site === 'Mine')}
              passes={showLandmarks && landmarks.some((mark) => mark.site === 'Pass')}
              houses={showHouses && houses.length > 0}
            />
          </section>

          <Legend
            world={world}
            year={year}
            focus={focus}
            onFocus={(id) => setFocus(id === focus ? null : id)}
          />

          {showHouses && (
            <HouseLegend
              world={world}
              year={year}
              houses={houses}
              focus={focus}
              onFocus={(id) => setFocus(id === focus ? null : id)}
            />
          )}

          {colouring === 'faith' && (
            <FaithLegend world={world} year={year} colours={faithColours} />
          )}

          {focused && (
            <section>
              <div className="he-label mb-2">{focused.name} in {year}</div>
              <RealmCard world={world} civ={focused} year={year} />
            </section>
          )}

          <section>
            <div className="he-label mb-2">The year {year}</div>
            {yearEvents.length === 0 ? (
              <p className="text-xs text-[var(--ink-faint)]">Nothing was recorded in this year.</p>
            ) : (
              <ol className="space-y-1">
                {yearEvents.slice(0, 24).map((event) => (
                  <li key={event.id} className="text-xs leading-relaxed">
                    <NarratedEvent world={world} event={event} />
                  </li>
                ))}
                {yearEvents.length > 24 && (
                  <li className="text-xs text-[var(--ink-faint)]">
                    …and {yearEvents.length - 24} more.
                  </li>
                )}
              </ol>
            )}
          </section>
        </div>
      </aside>
    </div>
  );
}

/**
 * What the marks on the map mean.
 *
 * Shape carries meaning here and colour carries identity — a dot is a place with people in it,
 * a cross is one that had them — so the shapes need saying once. Drawn from the same primitives
 * as the map itself rather than described in words, and each entry appears only while that
 * layer is on and has something in it.
 */
function MarkerKey({
  settlements,
  ruins,
  holySites,
  battles,
  harbours,
  riversides,
  walls,
  mines,
  passes,
  houses,
}: {
  settlements: boolean;
  ruins: boolean;
  holySites: boolean;
  battles: boolean;
  harbours: boolean;
  riversides: boolean;
  walls: boolean;
  mines: boolean;
  passes: boolean;
  houses: boolean;
}) {
  const marks: { label: string; glyph: React.ReactNode }[] = [];

  if (settlements) {
    marks.push({
      label: 'settlement, by size',
      glyph: (
        <>
          <circle cx={4} cy={8} r={1.6} fill="var(--ink-soft)" />
          <circle cx={10} cy={8} r={3} fill="var(--ink-soft)" />
        </>
      ),
    });
  }

  if (ruins) {
    marks.push({
      label: 'abandoned',
      glyph: (
        <g stroke="rgb(152 146 138)" strokeWidth={1.6} strokeLinecap="round">
          <line x1={4} y1={5} x2={10} y2={11} />
          <line x1={4} y1={11} x2={10} y2={5} />
        </g>
      ),
    });
  }

  if (holySites) {
    marks.push({
      label: 'holy site',
      glyph: (
        <rect
          x={4}
          y={5}
          width={6}
          height={6}
          rx={0.6}
          fill="rgb(238 204 112)"
          transform="rotate(45 7 8)"
        />
      ),
    });
  }

  if (harbours) {
    marks.push({
      label: 'harbour',
      glyph: (
        <g
          transform="translate(7 8) scale(0.55)"
          fill="none"
          stroke="rgb(214 232 244)"
          strokeWidth={1.7}
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <circle cx={0} cy={-5.2} r={1.5} />
          <line x1={0} y1={-3.6} x2={0} y2={5.2} />
          <line x1={-3.2} y1={-1.6} x2={3.2} y2={-1.6} />
          <path d="M-4.4 2.2 Q 0 7.4 4.4 2.2" />
        </g>
      ),
    });
  }

  if (riversides) {
    marks.push({
      label: 'river landing',
      glyph: (
        <g fill="none" stroke="rgb(168 206 230)" strokeWidth={1.4} strokeLinecap="round">
          <path d="M2.5 6.5c1.2 1.4 2.4 1.4 3.6 0s2.4-1.4 3.6 0" />
          <path d="M2.5 10c1.2 1.4 2.4 1.4 3.6 0s2.4-1.4 3.6 0" />
        </g>
      ),
    });
  }

  if (walls) {
    marks.push({
      label: 'walled town',
      glyph: (
        <rect
          x={3.5}
          y={4.5}
          width={7}
          height={7}
          fill="none"
          stroke="rgb(72 68 62)"
          strokeWidth={1.4}
        />
      ),
    });
  }

  if (mines) {
    marks.push({
      label: 'mine',
      glyph: (
        <g stroke="rgb(168 150 118)" strokeWidth={1.5} strokeLinecap="round">
          <line x1={3.5} y1={11} x2={10.5} y2={5} />
          <line x1={10.5} y1={11} x2={3.5} y2={5} />
        </g>
      ),
    });
  }

  if (passes) {
    marks.push({
      label: 'pass',
      glyph: (
        <path
          d="M2 12 L5 6 L7 9 L9 5 L12 12"
          fill="none"
          stroke="rgb(148 146 148)"
          strokeWidth={1.4}
          strokeLinejoin="round"
          strokeLinecap="round"
        />
      ),
    });
  }

  if (houses) {
    marks.push({
      label: 'house rules here',
      glyph: (
        <g>
          <line x1={5} y1={13} x2={5} y2={4} stroke="var(--ink-soft)" strokeWidth={1.3} />
          <path d="M5 4 L11 6.5 L5 9Z" fill="var(--ink-soft)" />
        </g>
      ),
    });
    marks.push({
      label: 'house rose here',
      glyph: (
        <g fill="none" stroke="var(--ink-soft)" strokeWidth={1.3} strokeLinejoin="round">
          <path d="M3.5 9.5 L7 6 L10.5 9.5 V13 H3.5Z" />
        </g>
      ),
    });
  }

  if (battles) {
    marks.push({
      label: 'battle this year',
      glyph: (
        <>
          <circle cx={7} cy={8} r={4} fill="none" stroke="rgb(214 96 84)" strokeWidth={1.2} />
          <circle cx={7} cy={8} r={1.1} fill="rgb(214 96 84)" />
        </>
      ),
    });
  }

  if (marks.length === 0) {
    return <p className="text-xs text-[var(--ink-faint)]">Nothing on these layers.</p>;
  }

  return (
    <div className="flex flex-col gap-1.5 text-xs text-[var(--ink-faint)]">
      {marks.map((mark) => (
        <span key={mark.label} className="flex items-center gap-1.5">
          <svg viewBox="0 0 14 16" className="h-3.5 w-3.5 shrink-0" aria-hidden="true">
            {mark.glyph}
          </svg>
          {mark.label}
        </span>
      ))}
    </div>
  );
}

function AnchorMark({ x, y, faded }: { x: number; y: number; faded: boolean }) {
  const opacity = faded ? 0.35 : 0.95;
  const parts = (
    <>
      <circle cx={0} cy={-0.48} r={0.16} />
      <line x1={0} y1={-0.32} x2={0} y2={0.52} />
      <line x1={-0.32} y1={-0.12} x2={0.32} y2={-0.12} />
      <path d="M-0.48 0.18 Q 0 0.72 0.48 0.18" />
    </>
  );

  return (
    <g
      transform={`translate(${x} ${y})`}
      className="pointer-events-none"
      fill="none"
      strokeLinecap="round"
      strokeLinejoin="round"
      opacity={opacity}
    >
      <title>Harbour</title>
      <g stroke="rgba(12, 22, 34, 0.8)" strokeWidth={0.4}>
        {parts}
      </g>
      <g stroke="rgb(236 246 252)" strokeWidth={0.2}>
        {parts}
      </g>
    </g>
  );
}

function WaveMark({ x, y, faded }: { x: number; y: number; faded: boolean }) {
  const opacity = faded ? 0.3 : 0.9;
  const parts = (
    <>
      <path d="M-0.55 -0.18 C -0.2 0.12, 0.2 0.12, 0.55 -0.18" />
      <path d="M-0.55 0.18 C -0.2 0.48, 0.2 0.48, 0.55 0.18" />
    </>
  );

  return (
    <g
      transform={`translate(${x} ${y})`}
      className="pointer-events-none"
      fill="none"
      strokeLinecap="round"
      opacity={opacity}
    >
      <title>River landing</title>
      <g stroke="rgba(12, 22, 34, 0.75)" strokeWidth={0.38}>
        {parts}
      </g>
      <g stroke="rgb(186 220 240)" strokeWidth={0.2}>
        {parts}
      </g>
    </g>
  );
}

function MineMark({ x, y, faded }: { x: number; y: number; faded: boolean }) {
  const opacity = faded ? 0.3 : 0.92;
  const parts = (
    <>
      <line x1={-0.48} y1={0.38} x2={0.48} y2={-0.42} />
      <line x1={0.48} y1={0.38} x2={-0.48} y2={-0.42} />
    </>
  );

  return (
    <g
      transform={`translate(${x} ${y})`}
      className="pointer-events-none"
      strokeLinecap="round"
      opacity={opacity}
    >
      <title>Mine</title>
      <g stroke="rgba(12, 12, 12, 0.8)" strokeWidth={0.4}>
        {parts}
      </g>
      <g stroke="rgb(210 188 132)" strokeWidth={0.22}>
        {parts}
      </g>
    </g>
  );
}

function PassMark({ x, y, faded }: { x: number; y: number; faded: boolean }) {
  const opacity = faded ? 0.3 : 0.9;
  const parts = <path d="M-0.7 0.45 L-0.28 -0.35 L0 -0.02 L0.28 -0.4 L0.7 0.45" />;

  return (
    <g
      transform={`translate(${x} ${y})`}
      className="pointer-events-none"
      fill="none"
      strokeLinejoin="round"
      strokeLinecap="round"
      opacity={opacity}
    >
      <title>Pass</title>
      <g stroke="rgba(12, 12, 12, 0.8)" strokeWidth={0.4}>
        {parts}
      </g>
      <g stroke="rgb(226 224 220)" strokeWidth={0.22}>
        {parts}
      </g>
    </g>
  );
}

function HouseGlyph({
  mark,
  colour,
  faded,
}: {
  mark: HouseMark;
  colour: string;
  faded: boolean;
}) {
  const opacity = faded ? 0.28 : 1;
  const { mx: x, my: y, role } = mark;

  if (role === 'seat') {
    const flag = `M ${x} ${y - 0.15} L ${x + 1.05} ${y + 0.28} L ${x} ${y + 0.7} Z`;
    return (
      <g className="pointer-events-none" opacity={opacity}>
        <line
          x1={x}
          y1={y + 1.15}
          x2={x}
          y2={y - 0.15}
          stroke="rgba(12,12,12,0.85)"
          strokeWidth={0.34}
          strokeLinecap="round"
        />
        <line
          x1={x}
          y1={y + 1.15}
          x2={x}
          y2={y - 0.15}
          stroke={colour}
          strokeWidth={0.2}
          strokeLinecap="round"
        />
        <path d={flag} fill={colour} stroke="rgba(12,12,12,0.85)" strokeWidth={0.16} strokeLinejoin="round" />
      </g>
    );
  }

  const size = role === 'home' ? 0.55 : 0.42;
  const house = `M ${x - size} ${y} L ${x} ${y - size * 0.9} L ${x + size} ${y} V ${y + size * 0.85} H ${x - size} Z`;
  return (
    <g
      className="pointer-events-none"
      fill={role === 'home' ? colour : 'var(--canvas)'}
      stroke="rgba(12,12,12,0.85)"
      strokeWidth={0.16}
      strokeLinejoin="round"
      opacity={opacity}
    >
      <path d={house} />
      <path d={house} fill="none" stroke={colour} strokeWidth={0.18} />
    </g>
  );
}

function houseHover(mark: HouseMark, world: World): string {
  const town = mark.settlement.name;
  const realm = world.nameOf(mark.civId);

  if (mark.role === 'seat') {
    return `Rules ${realm} from ${town}`;
  }

  if (mark.role === 'home') {
    const living =
      mark.living > 0
        ? ` · ${mark.living} ${mark.living === 1 ? 'lives' : 'live'} here`
        : '';
    return `Rose at ${town}${living}`;
  }

  return `${mark.living} of the house ${mark.living === 1 ? 'lives' : 'live'} at ${town}`;
}

function HouseLegend({
  world,
  year,
  houses,
  focus,
  onFocus,
}: {
  world: World;
  year: number;
  houses: HouseOnMap[];
  focus: EntityId | null;
  onFocus: (id: EntityId) => void;
}) {
  return (
    <section>
      <div className="he-label mb-2">Houses in {year}</div>
      {houses.length === 0 ? (
        <p className="text-xs text-[var(--ink-faint)]">No house held a throne this year.</p>
      ) : (
        <div className="flex flex-col gap-1 text-xs">
          {houses.map((entry) => (
            <button
              key={`${entry.house.id}-${entry.civId}`}
              type="button"
              onClick={() => onFocus(entry.civId)}
              title={
                entry.seat
                  ? `${entry.house.name} rules from ${entry.seat.name}`
                  : entry.house.name
              }
              className={`inline-flex items-center gap-1.5 text-left hover:text-[var(--accent)] ${
                focus && focus !== entry.civId ? 'opacity-45' : ''
              }`}
            >
              <span
                className="inline-block h-2.5 w-2.5 shrink-0"
                style={{
                  background: world.colourOf(entry.civId),
                  clipPath: 'polygon(0 0, 100% 40%, 0 80%)',
                }}
              />
              <span className="min-w-0 truncate">{entry.house.name}</span>
              <span className="he-data ml-auto truncate text-[var(--ink-faint)]">
                {world.nameOf(entry.civId)}
              </span>
            </button>
          ))}
        </div>
      )}
    </section>
  );
}

function YearScrubber({
  year,
  startYear,
  endYear,
  playing,
  onYear,
  onPlay,
}: {
  year: number;
  startYear: number;
  endYear: number;
  playing: boolean;
  onYear: (year: number) => void;
  onPlay: () => void;
}) {
  return (
    <div className="space-y-1.5">
      <div className="flex items-center gap-0.5">
        <button
          type="button"
          title="Previous year"
          aria-label="Previous year"
          disabled={year <= startYear}
          onClick={() => onYear(year - 1)}
          className="inline-flex h-7 w-7 items-center justify-center rounded text-[var(--ink-soft)] hover:text-[var(--primary)] disabled:opacity-40"
        >
          <IconChevronLeft />
        </button>
        <button
          type="button"
          onClick={onPlay}
          title={playing ? 'Pause' : 'Play the history through'}
          className="inline-flex h-7 min-w-7 items-center justify-center rounded px-1.5 text-xs text-[var(--ink-soft)] hover:text-[var(--primary)]"
        >
          {playing ? '❚❚' : '▶'}
        </button>
        <button
          type="button"
          title="Next year"
          aria-label="Next year"
          disabled={year >= endYear}
          onClick={() => onYear(year + 1)}
          className="inline-flex h-7 w-7 items-center justify-center rounded text-[var(--ink-soft)] hover:text-[var(--primary)] disabled:opacity-40"
        >
          <IconChevronRight />
        </button>
        <div className="ml-1.5 min-w-0">
          <div className="he-label">Year</div>
          <div className="he-data text-lg leading-none">{year}</div>
        </div>
      </div>
      <input
        type="range"
        min={startYear}
        max={endYear}
        value={year}
        onChange={(event) => onYear(Number(event.target.value))}
        className="w-full accent-[var(--accent)]"
        aria-label="Year"
      />
    </div>
  );
}

/**
 * The realms, with the land each held in the selected year.
 *
 * Doubles as the focus control: clicking a realm dims the rest of the map, which is the only
 * way to follow one realm's frontier through a run in which eight of them are moving.
 */
function Legend({
  world,
  year,
  focus,
  onFocus,
}: {
  world: World;
  year: number;
  focus: EntityId | null;
  onFocus: (id: EntityId) => void;
}) {
  const { timeline } = world;

  return (
    <section>
      <div className="mb-2 flex items-baseline justify-between">
        <div className="he-label">Realms</div>
        {focus && (
          <button
            type="button"
            onClick={() => onFocus(focus)}
            className="text-xs text-[var(--ink-faint)] hover:text-[var(--accent)]"
          >
            clear focus
          </button>
        )}
      </div>

      <div className="flex flex-col gap-1 text-xs">
        {world.export.civilizations.map((civ) => {
          const extent = timeline.extentAt(civ.id, year);
          const yet = civ.foundedYear > year;
          const gone = civ.endedYear !== undefined && civ.endedYear < year;

          return (
            <button
              key={civ.id}
              type="button"
              onClick={() => onFocus(civ.id)}
              title={
                yet
                  ? `Founded in ${civ.foundedYear}`
                  : gone
                    ? `Ended in ${civ.endedYear}`
                    : `${extent} ${extent === 1 ? 'region' : 'regions'} in ${year}`
              }
              className={`inline-flex items-center gap-1.5 text-left ${
                focus && focus !== civ.id ? 'opacity-45' : ''
              } ${yet || gone ? 'text-[var(--ink-faint)]' : ''} hover:text-[var(--accent)]`}
            >
              <span
                className="inline-block h-2.5 w-2.5 shrink-0 rounded-full"
                style={{
                  background: yet || gone ? 'transparent' : world.colourOf(civ.id),
                  border: yet || gone ? `1px solid ${world.colourOf(civ.id)}` : undefined,
                }}
              />
              <span className={`min-w-0 truncate ${gone ? 'line-through' : ''}`}>{civ.name}</span>
              {!yet && !gone && (
                <span className="he-data ml-auto text-[var(--ink-faint)]">{extent}</span>
              )}
            </button>
          );
        })}
      </div>
    </section>
  );
}

/**
 * The faiths with a congregation in the selected year.
 *
 * Only the ones actually being followed then — a world's whole list of faiths includes those
 * preached two centuries later and those forgotten a century before, and neither belongs in a
 * key to what is on the map now.
 */
function FaithLegend({
  world,
  year,
  colours,
}: {
  world: World;
  year: number;
  colours: Map<EntityId, string>;
}) {
  const followed = world.export.religions
    .map((faith) => ({ faith, following: world.timeline.followingAt(faith.id, year) }))
    .filter((entry) => entry.following > 0)
    .sort((a, b) => b.following - a.following);

  return (
    <section>
      <div className="he-label mb-2">Faiths</div>

      {followed.length === 0 ? (
        <p className="text-xs text-[var(--ink-faint)]">
          Nothing was preached anywhere in the world this year.
        </p>
      ) : (
        <div className="flex flex-col gap-1 text-xs">
          {followed.map(({ faith, following }) => (
            <a
              key={faith.id}
              href={`#/${faith.id}`}
              className="inline-flex items-center gap-1.5 hover:text-[var(--accent)]"
            >
              <span
                className="inline-block h-2.5 w-2.5 shrink-0 rounded-full"
                style={{ background: colours.get(faith.id) }}
              />
              <span className="min-w-0 truncate">{faith.name}</span>
              <span className="he-data ml-auto text-[var(--ink-faint)]">{following}</span>
            </a>
          ))}
        </div>
      )}
    </section>
  );
}

/** What one realm was, in one year: who ruled it, what it held, what it was fighting. */
function RealmCard({ world, civ, year }: { world: World; civ: Civilization; year: number }) {
  const { timeline } = world;

  const ruler = timeline.rulerAt(civ.id, year);
  const capital = timeline.capitalAt(civ.id, year);
  const extent = timeline.extentAt(civ.id, year);
  const wars = timeline.warsIn(year).filter(
    (war) => war.attackers.includes(civ.id) || war.defenders.includes(civ.id),
  );

  if (civ.foundedYear > year) {
    return (
      <p className="text-sm text-[var(--ink-faint)]">
        Not yet founded — <EntityLink world={world} id={civ.id} /> rises in {civ.foundedYear}.
      </p>
    );
  }

  if (civ.endedYear !== undefined && civ.endedYear < year) {
    return (
      <p className="text-sm text-[var(--ink-faint)]">
        <EntityLink world={world} id={civ.id} /> ended in {civ.endedYear}.
      </p>
    );
  }

  return (
    <div className="space-y-2 text-sm">
      <div className="flex flex-wrap gap-x-6 gap-y-1">
        <span>
          <span className="text-[var(--ink-faint)]">Ruler </span>
          {ruler ? <EntityLink world={world} id={ruler} /> : <span>the throne stood empty</span>}
        </span>
        <span>
          <span className="text-[var(--ink-faint)]">Seat </span>
          <EntityLink world={world} id={capital} />
        </span>
        <span className="tabular-nums">
          <span className="text-[var(--ink-faint)]">Held </span>
          {extent} {extent === 1 ? 'region' : 'regions'}
        </span>
      </div>

      {wars.length > 0 && (
        <div>
          <span className="text-[var(--ink-faint)]">At war: </span>
          {wars.map((war, index) => (
            <span key={war.id}>
              {index > 0 && ', '}
              <EntityLink world={world} id={war.id} />
            </span>
          ))}
        </div>
      )}
    </div>
  );
}

function radiusOf(entry: Standing): number {
  switch (entry.tier) {
    case 'City':
      return 1.05;
    case 'Town':
      return 0.75;
    case 'Village':
      return 0.52;
    default:
      return 0.36;
  }
}

function Toggle({
  label,
  on,
  onChange,
}: {
  label: string;
  on: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <label className="inline-flex cursor-pointer items-center gap-1.5 select-none">
      <input
        type="checkbox"
        checked={on}
        onChange={(event) => onChange(event.target.checked)}
        className="accent-[var(--accent)]"
      />
      {label}
    </label>
  );
}
