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
  type Region,
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
  | { kind: 'region'; region: Region; owner?: EntityId };

export function WorldMap({ world }: { world: World }) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const svgRef = useRef<SVGSVGElement | null>(null);

  const { raster, export: data, timeline } = world;
  const { startYear, endYear } = data.meta;

  const [layer, setLayer] = useState<Layer>('biome');
  const [showRivers, setShowRivers] = useState(true);
  const [showSettlements, setShowSettlements] = useState(true);
  const [showTerritory, setShowTerritory] = useState(true);
  const [year, setYear] = useState(endYear);
  const [playing, setPlaying] = useState(false);
  const [focus, setFocus] = useState<EntityId | null>(null);
  const [hovered, setHovered] = useState<Hover | null>(null);

  const grid = useMemo(() => buildGrid(data.world, data.regions), [data.world, data.regions]);
  const order = useMemo(() => data.civilizations.map((civ) => civ.id), [data.civilizations]);

  const owners = useMemo(() => timeline.ownersAt(year), [timeline, year]);
  const realms = useMemo(() => buildRealms(grid, owners, order), [grid, owners, order]);
  const standing = useMemo(() => timeline.settlementsAt(year), [timeline, year]);

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
   */
  const probe = (event: MouseEvent<SVGSVGElement>) => {
    const box = svgRef.current?.getBoundingClientRect();
    if (!box) return;

    const x = ((event.clientX - box.left) / box.width) * 100;
    const y = ((event.clientY - box.top) / box.height) * 100;

    if (showSettlements) {
      let nearest: Standing | null = null;
      let best = 1.6 * 1.6;

      for (const entry of standing) {
        const dx = toWorld(entry.settlement.x, 'x') - x;
        const dy = toWorld(entry.settlement.z, 'z') - y;
        const distance = dx * dx + dy * dy;
        if (distance < best) {
          best = distance;
          nearest = entry;
        }
      }

      if (nearest) {
        setHovered({ kind: 'settlement', standing: nearest });
        return;
      }
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
            <Badge>raster {raster.resolution}²</Badge>
            <Badge>
              {realms.length} {realms.length === 1 ? 'realm' : 'realms'} · {standing.length}{' '}
              settlements
            </Badge>
          </>
        }
      />

      <Panel
        actions={
          <div className="flex flex-wrap items-center gap-3 text-xs">
            <select
              value={layer}
              onChange={(event) => setLayer(event.target.value as Layer)}
              className="rounded border border-[var(--rule)] bg-[var(--page)] px-1.5 py-1 text-xs"
            >
              <option value="biome">Biome</option>
              <option value="height">Elevation</option>
              <option value="habitability">Habitability</option>
            </select>
            <Toggle label="Rivers" on={showRivers} onChange={setShowRivers} />
            <Toggle label="Settlements" on={showSettlements} onChange={setShowSettlements} />
            <Toggle label="Territory" on={showTerritory} onChange={setShowTerritory} />
          </div>
        }
      >
        <div className="relative mx-auto aspect-square w-full max-w-3xl overflow-hidden rounded border border-[var(--rule)]">
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

            {showSettlements &&
              standing.map((entry) => (
                <circle
                  key={entry.settlement.id}
                  cx={toWorld(entry.settlement.x, 'x')}
                  cy={toWorld(entry.settlement.z, 'z')}
                  r={radiusOf(entry)}
                  fill={world.colourOf(entry.civilizationId)}
                  fillOpacity={dimmed(entry.civilizationId) ? 0.35 : 1}
                  stroke="rgba(12,12,12,0.75)"
                  strokeWidth={entry.isCapital ? 0.3 : 0.14}
                  strokeOpacity={dimmed(entry.civilizationId) ? 0.35 : 1}
                />
              ))}

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
            <div className="pointer-events-none absolute bottom-2 left-2 max-w-[85%] rounded border border-[var(--rule)] bg-[var(--panel)]/95 px-2.5 py-1.5 text-xs shadow-sm">
              {hovered.kind === 'settlement' ? (
                <>
                  <div className="font-serif text-sm">{hovered.standing.settlement.name}</div>
                  <div className="text-[var(--ink-faint)]">
                    {hovered.standing.isCapital ? 'Seat of ' : ''}
                    {hovered.standing.tier} · {world.nameOf(hovered.standing.civilizationId)}
                  </div>
                </>
              ) : (
                <>
                  <div className="font-serif text-sm">{hovered.region.name}</div>
                  <div className="text-[var(--ink-faint)]">
                    {hovered.region.biome} ·{' '}
                    {hovered.owner ? world.nameOf(hovered.owner) : 'unclaimed'}
                  </div>
                </>
              )}
            </div>
          )}

          <div className="pointer-events-none absolute top-2 right-2 rounded border border-[var(--rule)] bg-[var(--panel)]/95 px-2 py-1 font-serif text-lg tabular-nums shadow-sm">
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

        <Legend
          world={world}
          year={year}
          focus={focus}
          onFocus={(id) => setFocus(id === focus ? null : id)}
        />
      </Panel>

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
    <div className="mx-auto mt-4 flex max-w-3xl items-center gap-3">
      <button
        type="button"
        onClick={onPlay}
        title={playing ? 'Pause' : 'Play the history through'}
        className="w-16 shrink-0 rounded border border-[var(--rule)] px-2 py-1 text-xs hover:border-[var(--accent)] hover:text-[var(--accent)]"
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
        className="shrink-0 rounded border border-[var(--rule)] px-2 py-1 text-xs disabled:opacity-40 enabled:hover:border-[var(--accent)] enabled:hover:text-[var(--accent)]"
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
    <div className="mx-auto mt-4 max-w-3xl">
      <div className="mb-2 flex items-baseline justify-between">
        <div className="text-[0.7rem] font-semibold tracking-wide uppercase text-[var(--ink-faint)]">
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
                <span className="tabular-nums text-[var(--ink-faint)]">{extent}</span>
              )}
            </button>
          );
        })}
      </div>
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
