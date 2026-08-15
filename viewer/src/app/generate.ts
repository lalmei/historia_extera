/**
 * Talking to the dev server's generator.
 *
 * The endpoint exists only under `astro dev` (see `viewer/dev/world-generator.mjs`), so
 * everything here is gated on `CAN_GENERATE`. That constant is `import.meta.env.DEV`,
 * which Vite replaces with a literal at build time — the generator UI is therefore not
 * merely hidden in a production build, it is removed from the bundle along with this
 * module. A built viewer remains a static file that opens off disk.
 */

export const CAN_GENERATE: boolean = import.meta.env.DEV;

const RUNS = '/api/worlds/runs';

/** How often a run in flight is asked how it is doing. */
export const POLL_MS = 600;

/** What the form can set. Everything else stays at the CLI's defaults. */
export interface RunParams {
  seed: number;
  years: number;
  civs: number;
  size: number;
  eastWestPeriodic: boolean;
}

export type RunStatus = 'running' | 'done' | 'failed' | 'cancelled';

export interface Run {
  id: string;
  params: RunParams;
  status: RunStatus;
  /** The CLI's own output, tail-first-in, as far as it has got. */
  log: string[];
  /** Viewer-relative path to the finished export. Present once `status` is `done`. */
  world?: string;
  bytes?: number;
  error?: string;
  elapsedMs: number;
}

export const DEFAULT_PARAMS: RunParams = {
  seed: 42,
  years: 300,
  civs: 8,
  size: 4096,
  eastWestPeriodic: false,
};

/** The bounds the endpoint enforces, mirrored here so the form can say so before asking. */
export const BOUNDS = {
  seed: { min: 0, max: Number.MAX_SAFE_INTEGER },
  years: { min: 1, max: 5000 },
  civs: { min: 1, max: 64 },
  size: { min: 512, max: 8192 },
} as const;

/**
 * The engine's siting floor, mirrored: a world below it seats fewer civilizations than asked
 * for, and far below it seats none at all and still reports a successful run.
 */
const REGION_SIZE = 128;
const REGIONS_PER_CIV = 16;

/** Smallest world size on the form's 256-unit step that can seat this many civilizations. */
export function minimumSizeFor(civs: number): number {
  let size = BOUNDS.size.min;
  while (Math.floor(size / REGION_SIZE) ** 2 < REGIONS_PER_CIV * civs) size += 256;
  return size;
}

export async function startRun(params: RunParams): Promise<Run> {
  return request(RUNS, { method: 'POST', body: JSON.stringify(params) });
}

export async function readRun(id: string): Promise<Run> {
  return request(`${RUNS}/${encodeURIComponent(id)}`);
}

export async function cancelRun(id: string): Promise<Run> {
  return request(`${RUNS}/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

async function request(url: string, init?: RequestInit): Promise<Run> {
  const response = await fetch(url, {
    ...init,
    headers: { 'content-type': 'application/json' },
  });

  const body: unknown = await response.json().catch(() => null);

  if (!response.ok) {
    // The endpoint names what it refused; a bare status code would leave the form
    // guessing which of three numbers it got wrong.
    const reason =
      body && typeof body === 'object' && 'error' in body
        ? String((body as { error: unknown }).error)
        : `the generator answered ${response.status}`;

    throw new Error(reason);
  }

  return body as Run;
}

/** A fresh seed worth trying. 32 bits, so it stays short enough to read out loud. */
export function randomSeed(): number {
  return Math.floor(Math.random() * 2 ** 32);
}
