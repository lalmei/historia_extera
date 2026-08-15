/**
 * Talking to the dev server's generator.
 *
 * The endpoint exists only under `astro dev` (see `viewer/dev/world-generator.mjs`), so
 * everything here is gated on `CAN_GENERATE`. That constant is `import.meta.env.DEV`,
 * which Vite replaces with a literal at build time — the generator UI is therefore not
 * merely hidden in a production build, it is removed from the bundle along with this
 * module. A built viewer remains a static file that opens off disk.
 */

import type { WorldExport } from './types';

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

/**
 * The filename the generator writes for these settings.
 *
 * Kept in one place so the form, the catalog and the overwrite warning all agree on
 * whether a run will replace an export already on disk.
 */
export function worldFileName(params: RunParams): string {
  const boundary = params.eastWestPeriodic ? '-ewp' : '';
  return `world-s${params.seed}-y${params.years}-c${params.civs}-z${params.size}${boundary}.json`;
}

/**
 * Settings encoded in a generator filename.
 *
 * Older runs omitted `-z<size>`; those still parse, and size stays unknown so the
 * caller can fill it from the export header instead.
 */
export function paramsFromFilename(name: string): Partial<RunParams> | null {
  const match = /^world-s(\d+)-y(\d+)-c(\d+)(?:-z(\d+))?(-ewp)?\.json$/i.exec(name);
  if (!match) return null;

  return {
    seed: Number(match[1]),
    years: Number(match[2]),
    civs: Number(match[3]),
    ...(match[4] ? { size: Number(match[4]) } : {}),
    eastWestPeriodic: Boolean(match[5]),
  };
}

/** Rebuild the form from a loaded export, preferring the filename for the civ count. */
export function paramsFromExport(data: WorldExport, fileName?: string): RunParams {
  const named = fileName ? paramsFromFilename(fileName) : null;

  return {
    seed: data.meta.seed,
    years: data.meta.yearsSimulated,
    civs: named?.civs ?? DEFAULT_PARAMS.civs,
    size: named?.size ?? data.world.width,
    eastWestPeriodic: data.world.eastWestPeriodic,
  };
}

/**
 * Query string used to hand a previous world's settings to `/new`.
 *
 * `from` is the filename being reused, so the form can say which export it took
 * the numbers from and warn when Generate would overwrite it.
 */
export function generatorSearch(params: RunParams, from?: string): string {
  const query = new URLSearchParams();
  query.set('seed', String(params.seed));
  query.set('years', String(params.years));
  query.set('civs', String(params.civs));
  query.set('size', String(params.size));
  if (params.eastWestPeriodic) query.set('ewp', '1');
  if (from) query.set('from', from);
  return query.toString();
}

export function generatorUrl(params: RunParams, from?: string): string {
  return `${import.meta.env.BASE_URL}new/?${generatorSearch(params, from)}#simulate`;
}

export function paramsFromSearch(search: string | URLSearchParams): RunParams | null {
  const query = typeof search === 'string' ? new URLSearchParams(search) : search;
  if (!query.has('seed') && !query.has('years') && !query.has('from')) return null;

  const named = query.get('from') ? paramsFromFilename(query.get('from')!) : null;

  return {
    seed: readSearchNumber(query, 'seed', named?.seed ?? DEFAULT_PARAMS.seed),
    years: readSearchNumber(query, 'years', named?.years ?? DEFAULT_PARAMS.years),
    civs: readSearchNumber(query, 'civs', named?.civs ?? DEFAULT_PARAMS.civs),
    size: readSearchNumber(query, 'size', named?.size ?? DEFAULT_PARAMS.size),
    eastWestPeriodic: query.has('ewp')
      ? query.get('ewp') !== '0'
      : (named?.eastWestPeriodic ?? DEFAULT_PARAMS.eastWestPeriodic),
  };
}

function readSearchNumber(query: URLSearchParams, name: string, fallback: number): number {
  const raw = query.get(name);
  if (raw === null || raw === '') return fallback;
  const value = Number(raw);
  return Number.isSafeInteger(value) ? value : fallback;
}

/**
 * A plausible next end year: +100, then +200, then +500, never past the form's ceiling.
 *
 * Not a continuation of saved state — the engine always starts from year one — but the
 * same seed is deterministic, so the first N years of a longer run are the history
 * already on screen.
 */
export function suggestedContinueYears(years: number): number {
  const extra = years >= 1000 ? 500 : years >= 400 ? 200 : 100;
  return Math.min(BOUNDS.years.max, years + extra);
}

export function worldFileFromLocation(search = window.location.search): string | undefined {
  const requested = new URLSearchParams(search).get('world')?.trim();
  if (!requested) return undefined;
  const file = requested.split('/').pop();
  return file && file.endsWith('.json') ? file : undefined;
}
