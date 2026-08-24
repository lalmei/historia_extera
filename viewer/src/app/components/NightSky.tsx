import { useEffect, useRef, useState } from 'react';
import type { ExportGalaxy } from '../types';
import { renderNightSky, SKY_HEIGHT, SKY_WIDTH } from '../nightSky';

export function NightSky({ galaxy, seed }: { galaxy: ExportGalaxy; seed: number }) {
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
      <div className="he-label mb-2">Night sky</div>
      <canvas
        ref={canvasRef}
        width={SKY_WIDTH}
        height={SKY_HEIGHT}
        className="h-auto w-full rounded-md border border-[var(--rule)] bg-[#070b14]"
        role="img"
        aria-label="Night sky from this world in galactic coordinates"
      />
      <p className="mt-2 text-xs text-[var(--ink-faint)]">
        {caption || 'Galactic longitude −180° to +180° · nucleus at 0° · latitude +90° (top) to −90°'}
      </p>
    </div>
  );
}
