import { useEffect, useMemo, useRef, useState } from 'react';
import { navigate } from '../router';
import type { World } from '../store';
import { FLAG_COAST, type Biome, type Settlement } from '../types';
import { Badge, Panel, PageTitle } from '../components/common';

/**
 * The 2D world map.
 *
 * Deliberately knows nothing about where terrain came from. It consumes the byte
 * planes in the export — height, biome, river/coast flags — and Phase 2's real
 * generated terrain or Phase 3's Vintage Story terrain will render here unchanged,
 * because the export shape does not change.
 *
 * Terrain goes to a canvas via ImageData at raster resolution and is then scaled;
 * settlements are drawn as vector overlays on top. That split matters because the
 * raster is a few hundred pixels square while there may be thousands of
 * settlements — and it means the colour ramp lives here, in the viewer, where it
 * can respond to theme rather than being baked into the file.
 */

type Layer = 'biome' | 'height' | 'habitability';

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

export function WorldMap({ world }: { world: World }) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const [layer, setLayer] = useState<Layer>('biome');
  const [showRivers, setShowRivers] = useState(true);
  const [showSettlements, setShowSettlements] = useState(true);
  const [showTerritory, setShowTerritory] = useState(false);
  const [hovered, setHovered] = useState<Settlement | null>(null);

  const { raster, export: data } = world;

  /** Stable colour per civilization, so territories and dots agree across layers. */
  const civColours = useMemo(() => {
    const colours = new Map<string, string>();
    data.civilizations.forEach((civ, index) => {
      const hue = (index * 137.508) % 360;
      colours.set(civ.id, `hsl(${hue} 62% 52%)`);
    });
    return colours;
  }, [data.civilizations]);

  const activeSettlements = useMemo(
    () => data.settlements.filter((s) => s.abandonedYear === undefined),
    [data.settlements],
  );

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

  return (
    <div>
      <PageTitle
        eyebrow="World"
        title="Map"
        meta={
          <>
            <Badge>
              {data.world.width.toLocaleString()} × {data.world.height.toLocaleString()} units
            </Badge>
            <Badge>raster {raster.resolution}²</Badge>
            <Badge>{activeSettlements.length} settlements</Badge>
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
            viewBox="0 0 100 100"
            preserveAspectRatio="none"
            className="absolute inset-0 h-full w-full"
          >
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

            {showTerritory &&
              data.regions
                .filter((region) => region.owner)
                .map((region) => (
                  <rect
                    key={region.id}
                    x={toWorld(region.minX, 'x')}
                    y={toWorld(region.minZ, 'z')}
                    width={(region.width / data.world.width) * 100}
                    height={(region.height / data.world.height) * 100}
                    fill={civColours.get(region.owner!) ?? 'transparent'}
                    fillOpacity={0.28}
                    stroke={civColours.get(region.owner!) ?? 'transparent'}
                    strokeOpacity={0.5}
                    strokeWidth={0.12}
                  />
                ))}

            {showSettlements &&
              activeSettlements.map((settlement) => {
                const radius =
                  settlement.tier === 'City'
                    ? 1.05
                    : settlement.tier === 'Town'
                      ? 0.75
                      : settlement.tier === 'Village'
                        ? 0.52
                        : 0.36;

                return (
                  <circle
                    key={settlement.id}
                    cx={toWorld(settlement.x, 'x')}
                    cy={toWorld(settlement.z, 'z')}
                    r={radius}
                    fill={civColours.get(settlement.civilizationId) ?? '#fff'}
                    stroke="rgba(12,12,12,0.75)"
                    strokeWidth={settlement.isCapital ? 0.3 : 0.14}
                    className="cursor-pointer"
                    onMouseEnter={() => setHovered(settlement)}
                    onMouseLeave={() => setHovered(null)}
                    onClick={() => navigate(`/${settlement.id}`)}
                  />
                );
              })}
          </svg>

          {hovered && (
            <div className="pointer-events-none absolute bottom-2 left-2 rounded border border-[var(--rule)] bg-[var(--panel)]/95 px-2.5 py-1.5 text-xs shadow-sm">
              <div className="font-serif text-sm">{hovered.name}</div>
              <div className="text-[var(--ink-faint)]">
                {hovered.tier} · {hovered.population.toLocaleString()} people ·{' '}
                {world.nameOf(hovered.civilizationId)}
              </div>
            </div>
          )}
        </div>

        <div className="mx-auto mt-4 max-w-3xl">
          <div className="mb-2 text-[0.7rem] font-semibold tracking-wide uppercase text-[var(--ink-faint)]">
            Civilizations
          </div>
          <div className="flex flex-wrap gap-x-4 gap-y-1.5 text-xs">
            {data.civilizations.map((civ) => (
              <span key={civ.id} className="inline-flex items-center gap-1.5">
                <span
                  className="inline-block h-2.5 w-2.5 rounded-full"
                  style={{ background: civColours.get(civ.id) }}
                />
                <a
                  href={`#/${civ.id}`}
                  className={`hover:text-[var(--accent)] ${
                    civ.endedYear !== undefined ? 'text-[var(--ink-faint)] line-through' : ''
                  }`}
                >
                  {civ.name}
                </a>
              </span>
            ))}
          </div>
        </div>
      </Panel>
    </div>
  );
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
