import type React from 'react';
import { type MouseEvent, useEffect, useMemo, useRef, useState } from 'react';
import { NarratedEvent } from '../components/EventList';
import { Badge, EntityLink, PageTitle, Panel } from '../components/common';
import { navigate } from '../router';
import type { World } from '../store';
import { buildGrid, buildRealms } from '../territory';
import type { Standing } from '../timeline';
import {
  FLAG_COAST,
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
 * file.
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

export function WorldMap({ world }: { world: World }) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const svgRef = useRef<SVGSVGElement | null>(null);

  const { raster, export: data, timeline } = world;
  const { startYear, endYear } = data.meta;

  const [layer, setLayer] = useState<Layer>('biome');
  const [colouring, setColouring] = useState<Colouring>('realm');
  const [showRivers, setShowRivers] = useState(true);
  const [showTradeRoutes, setShowTradeRoutes] = useState(true);
  const [showSettlements, setShowSettlements] = useState(true);
  const [showRuins, setShowRuins] = useState(true);
  const [showHolySites, setShowHolySites] = useState(true);
  const [showTerritory, setShowTerritory] = useState(true);
  const [year, setYear] = useState(endYear);
  const [playing, setPlaying] = useState(false);
  const [focus, setFocus] = useState<EntityId | null>(null);
  const [hovered, setHovered] = useState<Hover | null>(null);

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
    return indices.map((index) => data.events[index]);
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
  const probe = (event: MouseEvent<SVGSVGElement>) => {
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

    // Clicking bare ground focuses whoever holds it, and clicking the sea clears the focus —
    // which is the gesture people try first and otherwise does nothing at all.
    setFocus(hovered.owner && hovered.owner !== focus ? hovered.owner : null);
  };

  const dimmed = (civilizationId: EntityId) => focus !== null && focus !== civilizationId;
  const focused = focus ? data.civilizations.find((civ) => civ.id === focus) : undefined;

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow="World"
        title="Map"
        meta={
          <>
            <Badge>
              {data.world.width.toLocaleString()} × {data.world.height.toLocaleString()} units
            </Badge>
            {data.world.eastWestPeriodic && (
              <Badge tone="accent">east/west edges joined</Badge>
            )}
            <Badge>raster {raster.resolution}²</Badge>
            <Badge>
              {realms.length} {realms.length === 1 ? 'realm' : 'realms'} · {standing.length}{' '}
              settlements
            </Badge>
            {ruins.length > 0 && <Badge tone="muted">{ruins.length} in ruins</Badge>}
            <Badge>{tradeRoutes.length} trade routes</Badge>
            <Badge>{independentHolySites.length} independent holy sites</Badge>
          </>
        }
      />

      <div className="he-map-viewport relative aspect-square w-full overflow-hidden rounded-lg border border-[var(--rule)] bg-[var(--canvas)]">
          <div className="he-map-chrome absolute top-2 left-2 z-10 flex max-w-[min(100%-1rem,32rem)] flex-wrap items-center gap-2 p-2 text-xs">
            <select
              value={layer}
              onChange={(event) => setLayer(event.target.value as Layer)}
              className="rounded border border-[var(--rule)] bg-[var(--surface-container-high)] px-1.5 py-1 text-xs"
            >
              <option value="biome">Biome</option>
              <option value="height">Elevation</option>
              <option value="habitability">Habitability</option>
            </select>
            <select
              value={colouring}
              onChange={(event) => setColouring(event.target.value as Colouring)}
              title="What the settlement dots are coloured by"
              className="rounded border border-[var(--rule)] bg-[var(--surface-container-high)] px-1.5 py-1 text-xs"
            >
              <option value="realm">Dots: realm</option>
              <option value="faith">Dots: faith</option>
            </select>
            <Toggle label="Rivers" on={showRivers} onChange={setShowRivers} />
            <Toggle label="Trade" on={showTradeRoutes} onChange={setShowTradeRoutes} />
            <Toggle label="Settlements" on={showSettlements} onChange={setShowSettlements} />
            <Toggle label="Ruins" on={showRuins} onChange={setShowRuins} />
            <Toggle label="Holy sites" on={showHolySites} onChange={setShowHolySites} />
            <Toggle label="Territory" on={showTerritory} onChange={setShowTerritory} />
          </div>
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
            className="absolute inset-0 h-full w-full cursor-crosshair"
            onMouseMove={probe}
            onMouseLeave={() => setHovered(null)}
            onClick={click}
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

            {/* Logical economic links, deliberately straight. Physical road and water paths
                belong to the later transport-network layer; these lines show demand, not
                geometry that the simulation has not calculated. Width is this year's traffic —
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
                      ; physical path not yet modelled
                    </title>
                  </line>
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

          {hovered && (
            <div className="pointer-events-none absolute bottom-2 left-2 max-w-[85%] rounded border border-[var(--rule)] bg-[var(--input)] px-2.5 py-1.5 text-xs">
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

          <div className="he-data pointer-events-none absolute top-2 right-2 rounded border border-[var(--rule)] bg-[var(--input)] px-2 py-1 text-lg">
            {year}
          </div>
        </div>

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

        <MarkerKey
          settlements={showSettlements}
          ruins={showRuins && ruins.length > 0}
          holySites={showHolySites && independentHolySites.length > 0}
          battles={battles.length > 0}
        />

        {showTradeRoutes && (
          <p className="mt-2 text-xs text-[var(--ink-faint)]">
            Trade overlay: dashed amber is overland demand, blue uses river access, and teal uses
            the coast. Line weight is the traffic the route carried in {year}. These are logical
            connections between markets; physical roads and paths are not modelled yet.
            {data.world.eastWestPeriodic &&
              ' Links between towns either side of the seam leave one edge and return at the other, which is the short way round in a world whose east and west edges are the same meridian.'}
          </p>
        )}

        <Legend
          world={world}
          year={year}
          focus={focus}
          onFocus={(id) => setFocus(id === focus ? null : id)}
        />

        {colouring === 'faith' && (
          <FaithLegend world={world} year={year} colours={faithColours} />
        )}

      {focused && (
        <Panel title={`${focused.name} in ${year}`}>
          <RealmCard world={world} civ={focused} year={year} />
        </Panel>
      )}

      <Panel title={`The year ${year}`}>
        {yearEvents.length === 0 ? (
          <p className="text-sm text-[var(--ink-faint)]">Nothing was recorded in this year.</p>
        ) : (
          <ol className="space-y-1">
            {yearEvents.slice(0, 24).map((event) => (
              <li key={event.id} className="text-sm leading-relaxed">
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
      </Panel>
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
}: {
  settlements: boolean;
  ruins: boolean;
  holySites: boolean;
  battles: boolean;
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

  if (marks.length === 0) return null;

  return (
    <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-[var(--ink-faint)]">
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
    <div className="mt-4 flex items-center gap-3">
      <button
        type="button"
        onClick={onPlay}
        title={playing ? 'Pause' : 'Play the history through'}
        className="he-btn-secondary w-16 shrink-0 px-2 py-1 text-xs"
      >
        {playing ? '❚❚ Pause' : '▶ Play'}
      </button>

      <input
        type="range"
        min={startYear}
        max={endYear}
        value={year}
        onChange={(event) => onYear(Number(event.target.value))}
        className="w-full accent-[var(--accent)]"
        aria-label="Year"
      />

      <button
        type="button"
        onClick={() => onYear(endYear)}
        disabled={year === endYear}
        className="he-btn-secondary shrink-0 px-2 py-1 text-xs disabled:opacity-40"
      >
        End
      </button>
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
    <div className="mt-4">
      <div className="mb-2 flex items-baseline justify-between">
        <div className="he-label">
          Realms
        </div>
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

      <div className="flex flex-wrap gap-x-4 gap-y-1.5 text-xs">
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
              className={`inline-flex items-center gap-1.5 ${
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
              <span className={gone ? 'line-through' : ''}>{civ.name}</span>
              {!yet && !gone && (
            <span className="he-data text-[var(--ink-faint)]">{extent}</span>
              )}
            </button>
          );
        })}
      </div>
    </div>
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
    <div className="mt-3">
      <div className="he-label mb-2">
        Faiths
      </div>

      {followed.length === 0 ? (
        <p className="text-xs text-[var(--ink-faint)]">
          Nothing was preached anywhere in the world this year.
        </p>
      ) : (
        <div className="flex flex-wrap gap-x-4 gap-y-1.5 text-xs">
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
              {faith.name}
              <span className="tabular-nums text-[var(--ink-faint)]">{following}</span>
            </a>
          ))}
        </div>
      )}
    </div>
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
