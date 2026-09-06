import { useEffect, useRef, useState } from 'react';
import type { ExportGalaxy } from '../types';
import { renderNightSky, SKY_HEIGHT, SKY_WIDTH } from '../nightSky';

/**
 * The sky as it is actually seen from the ground, with the two marks that make it readable: the
 * nucleus, which is a place a chronicler can point at, and the angle the band of light makes with
 * the horizon, which is why it looks the way it does from here rather than anywhere else.
 */
export function NightSky({
  galaxy,
  seed,
  planeInclinationDeg,
}: {
  galaxy: ExportGalaxy;
  seed: number;
  planeInclinationDeg?: number;
}) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [caption, setCaption] = useState('');

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const sky = renderNightSky(galaxy, seed);
    const image = ctx.createImageData(SKY_WIDTH, SKY_HEIGHT);
    image.data.set(sky.glow);
    ctx.putImageData(image, 0, 0);

    for (const star of sky.stars) {
      const brightness = Math.min(1, Math.max(0.05, (6.5 - star.mag) / 8));
      const warmth = Math.min(1.6, Math.max(-0.3, star.bv));
      const r = 200 + 55 * warmth;
      const g = 215 - 40 * warmth;
      const b = 245 - 120 * warmth;
      ctx.fillStyle = `rgba(${Math.round(r)}, ${Math.round(g)}, ${Math.round(b)}, ${brightness})`;
      const size = star.mag < 2 ? 1.6 : star.mag < 4 ? 1.15 : 0.85;
      ctx.beginPath();
      ctx.arc(star.x, star.y, size, 0, Math.PI * 2);
      ctx.fill();
    }

    setCaption(sky.caption);
  }, [galaxy, seed]);

  return (
    <div>
      <div className="relative overflow-hidden rounded-md border border-[var(--rule)]">
        <canvas
          ref={canvasRef}
          width={SKY_WIDTH}
          height={SKY_HEIGHT}
          className="block h-auto w-full bg-[#070b14]"
          role="img"
          aria-label="Night sky from this world in galactic coordinates"
        />
        <div className="pointer-events-none absolute inset-0">
          <span className="absolute left-1/2 top-1/2 h-3.5 w-3.5 -translate-x-1/2 -translate-y-1/2 rounded-full border border-[rgba(255,240,214,0.75)]" />
          <span className="absolute left-1/2 top-1/2 h-px w-14 translate-y-[-0.5px] bg-[rgba(255,240,214,0.5)]" />
          <div className="absolute left-[calc(50%+3.75rem)] top-1/2 -translate-y-1/2 rounded bg-[rgba(4,6,12,0.62)] px-2 py-1">
            <div className="he-label text-[rgba(255,240,214,0.95)]">Galactic centre</div>
            {planeInclinationDeg != null && (
              <div className="text-[11px] leading-tight text-[rgba(255,240,214,0.75)]">
                band {planeInclinationDeg.toFixed(0)}° to the horizon
              </div>
            )}
          </div>
        </div>
      </div>
      <p className="mt-2 text-xs text-[var(--ink-faint)]">
        {caption || 'Galactic longitude −180° to +180° · nucleus at 0° · latitude +90° (top) to −90°'}
      </p>
    </div>
  );
}
