const WORLD_CATALOG = '/api/worlds';
const WORLD_FILES = `${WORLD_CATALOG}/files`;

export interface SavedWorld {
  name: string;
  /** Viewer-relative path accepted by the `?world=` selector. */
  world: string;
  bytes: number;
  modifiedAt?: string;
  schemaVersion: number | null;
  /** How the history names itself, when the file header carried it. */
  designation?: string | null;
  /** The world's proper name (planet or moon), when the file header carried it. */
  worldName?: string | null;
  kind?: 'Planet' | 'Moon' | null;
  /** Settings reconstructed from the file header and, for civs, the filename. */
  params?: {
    seed: number;
    years: number;
    civs: number;
    size: number;
    eastWestPeriodic: boolean;
  } | null;
  engineVersion?: string | null;
  eventCount?: number | null;
  error?: string;
}

export interface DeletedWorld {
  name: string;
  permanent: boolean;
  recoveryPath?: string;
}

export interface WorldPreview {
  size: number;
  minHeight: number;
  maxHeight: number;
  height: Uint8Array;
  biome: Uint8Array;
  flags: Uint8Array;
}

export interface WorldEndState {
  civilizations: Array<{
    name: string;
    population: number;
    foundedYear: number;
    endedYear?: number;
  }>;
  settlements: Array<{ tier?: string; abandonedYear?: number }>;
  dynasties: Array<{ endedYear?: number }>;
  wars: Array<{ endedYear?: number }>;
  tradeRoutes: Array<{ endedYear?: number }>;
  religions: Array<{ endedYear?: number }>;
  battles: number;
  artifacts: number;
  holySites: number;
}

export interface WorldExpansion {
  preview: WorldPreview;
  endState: WorldEndState;
}

const PREVIEW_SIZE = 128;
const IDENTITY_BYTES = 16 * 1024;

export async function listWorlds(): Promise<SavedWorld[]> {
  const response = await fetch(WORLD_CATALOG);
  const body: unknown = await response.json().catch(() => null);

  if (!response.ok) {
    const reason =
      body && typeof body === 'object' && 'error' in body
        ? String((body as { error: unknown }).error)
        : `the world catalog answered ${response.status}`;
    throw new Error(reason);
  }

  if (!body || typeof body !== 'object' || !('worlds' in body)) {
    throw new Error('the world catalog returned an invalid response');
  }

  const worlds = (body as { worlds: SavedWorld[] }).worlds;
  return Promise.all(worlds.map(enrichIdentity));
}

export async function deleteWorld(name: string, permanent = false): Promise<DeletedWorld> {
  const query = permanent ? '?permanent=true' : '';
  const response = await fetch(`${WORLD_FILES}/${encodeURIComponent(name)}${query}`, {
    method: 'DELETE',
  });
  const body: unknown = await response.json().catch(() => null);

  if (!response.ok) {
    const reason =
      body && typeof body === 'object' && 'error' in body
        ? String((body as { error: unknown }).error)
        : `the world catalog answered ${response.status}`;
    throw new Error(reason);
  }

  return body as DeletedWorld;
}

export function displayNameOf(world: SavedWorld): string {
  const name = world.worldName?.trim();
  if (name) return name;
  const designation = world.designation?.trim();
  if (designation) return designation;
  return 'Untitled world';
}

/** Low-resolution biome map for a library row, streamed from the export without loading the chronicle. */
export async function loadWorldPreview(worldPath: string): Promise<WorldPreview> {
  const raster = (await readStreamingJsonObject(worldUrl(worldPath), 'raster')) as {
    resolution?: number;
    minHeight?: number;
    maxHeight?: number;
    height?: string;
    biome?: string;
    flags?: string;
  };

  const resolution = raster.resolution;
  if (!resolution || resolution < 2) throw new Error('raster has no resolution');

  const height = decodePlane(raster.height, resolution);
  const biome = decodePlane(raster.biome, resolution);
  const flags = raster.flags
    ? decodePlane(raster.flags, resolution)
    : new Uint8Array(resolution * resolution);

  return {
    size: PREVIEW_SIZE,
    minHeight: raster.minHeight ?? 0,
    maxHeight: raster.maxHeight ?? 1,
    height: downsample(height, resolution, PREVIEW_SIZE),
    biome: downsample(biome, resolution, PREVIEW_SIZE),
    flags: downsample(flags, resolution, PREVIEW_SIZE),
  };
}

const END_STATE_KEYS = [
  'civilizations',
  'dynasties',
  'settlements',
  'tradeRoutes',
  'wars',
  'battles',
  'religions',
  'holySites',
  'artifacts',
] as const;

const SKIP_KEYS = ['regions', 'cultures', 'figures', 'events', 'series', 'narration'];

/** Map thumbnail and end-of-history counts, from one streamed pass over the export. */
export async function loadWorldExpansion(worldPath: string): Promise<WorldExpansion> {
  const extracted = await extractJsonKeys(worldUrl(worldPath), ['raster', ...END_STATE_KEYS], SKIP_KEYS);
  const raster = extracted.raster as {
    resolution?: number;
    minHeight?: number;
    maxHeight?: number;
    height?: string;
    biome?: string;
    flags?: string;
  } | undefined;

  if (!raster?.resolution) throw new Error('raster has no resolution');

  const list = (key: (typeof END_STATE_KEYS)[number]) =>
    Array.isArray(extracted[key]) ? (extracted[key] as Record<string, unknown>[]) : [];

  const civilizations = list('civilizations').map((civ) => ({
    name: String(civ.name ?? '—'),
    population: Number(civ.population) || 0,
    foundedYear: Number(civ.foundedYear) || 0,
    endedYear: civ.endedYear === undefined || civ.endedYear === null ? undefined : Number(civ.endedYear),
  }));

  return {
    preview: previewFromRaster(raster),
    endState: {
      civilizations,
      settlements: list('settlements').map((settlement) => ({
        tier: typeof settlement.tier === 'string' ? settlement.tier : undefined,
        abandonedYear:
          settlement.abandonedYear === undefined || settlement.abandonedYear === null
            ? undefined
            : Number(settlement.abandonedYear),
      })),
      dynasties: list('dynasties').map((house) => ({
        endedYear:
          house.endedYear === undefined || house.endedYear === null ? undefined : Number(house.endedYear),
      })),
      wars: list('wars').map((war) => ({
        endedYear: war.endedYear === undefined || war.endedYear === null ? undefined : Number(war.endedYear),
      })),
      tradeRoutes: list('tradeRoutes').map((route) => ({
        endedYear:
          route.endedYear === undefined || route.endedYear === null ? undefined : Number(route.endedYear),
      })),
      religions: list('religions').map((faith) => ({
        endedYear:
          faith.endedYear === undefined || faith.endedYear === null ? undefined : Number(faith.endedYear),
      })),
      battles: list('battles').length,
      artifacts: list('artifacts').length,
      holySites: list('holySites').length,
    },
  };
}

function previewFromRaster(raster: {
  resolution?: number;
  minHeight?: number;
  maxHeight?: number;
  height?: string;
  biome?: string;
  flags?: string;
}): WorldPreview {
  const resolution = raster.resolution;
  if (!resolution || resolution < 2) throw new Error('raster has no resolution');

  const height = decodePlane(raster.height, resolution);
  const biome = decodePlane(raster.biome, resolution);
  const flags = raster.flags
    ? decodePlane(raster.flags, resolution)
    : new Uint8Array(resolution * resolution);

  return {
    size: PREVIEW_SIZE,
    minHeight: raster.minHeight ?? 0,
    maxHeight: raster.maxHeight ?? 1,
    height: downsample(height, resolution, PREVIEW_SIZE),
    biome: downsample(biome, resolution, PREVIEW_SIZE),
    flags: downsample(flags, resolution, PREVIEW_SIZE),
  };
}

async function enrichIdentity(world: SavedWorld): Promise<SavedWorld> {
  if (world.worldName?.trim() && world.eventCount != null) return world;

  try {
    const header = await readPrefix(worldUrl(world.world), IDENTITY_BYTES);
    const identity = parseIdentity(header);
    return {
      ...world,
      worldName: identity.worldName ?? world.worldName,
      designation: identity.designation ?? world.designation,
      kind: identity.kind ?? world.kind,
      eventCount: identity.eventCount ?? world.eventCount,
    };
  } catch {
    return world;
  }
}

function parseIdentity(
  text: string,
): Pick<SavedWorld, 'worldName' | 'designation' | 'kind' | 'eventCount'> {
  const rasterAt = text.search(/"raster"\s*:/);
  const prefix = rasterAt === -1 ? text : text.slice(0, rasterAt);
  const worldAt = prefix.search(/"world"\s*:\s*\{/);
  const slice = worldAt === -1 ? prefix : prefix.slice(worldAt);

  return {
    worldName: stringField(slice, 'name'),
    designation: stringField(prefix, 'designation'),
    kind: kindField(slice),
    eventCount: intField(prefix, 'eventCount'),
  };
}

function stringField(text: string, name: string): string | null {
  const match = new RegExp(`"${name}"\\s*:\\s*"((?:\\\\.|[^"\\\\])*)"`).exec(text);
  if (!match) return null;
  try {
    return JSON.parse(`"${match[1]}"`) as string;
  } catch {
    return match[1];
  }
}

function kindField(text: string): 'Planet' | 'Moon' | null {
  const match = /"kind"\s*:\s*"(Planet|Moon)"/.exec(text);
  return match ? (match[1] as 'Planet' | 'Moon') : null;
}

function intField(text: string, name: string): number | null {
  const match = new RegExp(`"${name}"\\s*:\\s*(-?\\d+)`).exec(text);
  if (!match) return null;
  const value = Number(match[1]);
  return Number.isSafeInteger(value) ? value : null;
}

function worldUrl(worldPath: string): string {
  const base = import.meta.env.BASE_URL;
  const path = worldPath.replace(/^\//, '');
  return `${base}${path}`.replace(/\/{2,}/g, '/');
}

async function readPrefix(url: string, limit: number): Promise<string> {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`could not read ${url}`);
  if (!response.body) return response.text().then((text) => text.slice(0, limit));

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let text = '';
  try {
    while (text.length < limit) {
      const { done, value } = await reader.read();
      if (value) text += decoder.decode(value, { stream: true });
      if (done) break;
    }
  } finally {
    await reader.cancel();
  }
  return text.slice(0, limit);
}

async function readStreamingJsonObject(url: string, key: string, maxBytes = 8_000_000): Promise<unknown> {
  const extracted = await extractJsonKeys(url, [key], []);
  if (!(key in extracted)) throw new Error(`no ${key} object in world file`);
  return extracted[key];
}

async function extractJsonKeys(
  url: string,
  keep: readonly string[],
  skip: readonly string[],
  maxBytes = 16_000_000,
): Promise<Record<string, unknown>> {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`could not read ${url}`);
  if (!response.body) throw new Error(`no body for ${url}`);

  const wanted = new Set(keep);
  const found: Record<string, unknown> = {};
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let acc = '';

  try {
    while (wanted.size > 0 && acc.length < maxBytes) {
      const { done, value } = await reader.read();
      if (value) acc += decoder.decode(value, { stream: true });

      for (const key of skip) {
        const stripped = stripCompleteValue(acc, key);
        if (stripped !== null) acc = stripped;
      }

      for (const key of [...wanted]) {
        const raw = extractJsonValue(acc, key);
        if (!raw) continue;
        found[key] = JSON.parse(raw);
        wanted.delete(key);
        const stripped = stripCompleteValue(acc, key);
        if (stripped !== null) acc = stripped;
      }

      if (done) break;
    }
  } finally {
    await reader.cancel();
  }

  return found;
}

function stripCompleteValue(text: string, key: string): string | null {
  const found = new RegExp(`"${key}"\\s*:`).exec(text);
  if (!found) return null;
  const raw = extractJsonValue(text, key);
  if (!raw) return null;
  const valueStart = text.indexOf(raw, found.index + found[0].length);
  if (valueStart === -1) return null;
  return `${text.slice(0, found.index)}${text.slice(valueStart + raw.length)}`;
}

function extractJsonValue(text: string, key: string): string | null {
  const found = new RegExp(`"${key}"\\s*:`).exec(text);
  if (!found) return null;

  let i = found.index + found[0].length;
  while (i < text.length && /\s/.test(text[i])) i++;
  if (i >= text.length) return null;

  const open = text[i];
  if (open !== '{' && open !== '[') return null;
  const close = open === '{' ? '}' : ']';

  let depth = 0;
  let inString = false;
  let escape = false;
  for (; i < text.length; i++) {
    const c = text[i];
    if (inString) {
      if (escape) escape = false;
      else if (c === '\\') escape = true;
      else if (c === '"') inString = false;
      continue;
    }
    if (c === '"') inString = true;
    else if (c === open) depth++;
    else if (c === close) {
      depth--;
      if (depth === 0) {
        const start = text.indexOf(open, found.index + found[0].length);
        return text.slice(start, i + 1);
      }
    }
  }
  return null;
}

function extractJsonObject(text: string, key: string): string | null {
  return extractJsonValue(text, key);
}

function decodePlane(encoded: string | undefined, resolution: number): Uint8Array {
  if (!encoded) return new Uint8Array(resolution * resolution);
  const binary = atob(encoded);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}

function downsample(src: Uint8Array, resolution: number, outSize: number): Uint8Array {
  const out = new Uint8Array(outSize * outSize);
  for (let y = 0; y < outSize; y++) {
    const srcY = Math.min(resolution - 1, Math.floor(((y + 0.5) * resolution) / outSize));
    for (let x = 0; x < outSize; x++) {
      const srcX = Math.min(resolution - 1, Math.floor(((x + 0.5) * resolution) / outSize));
      out[y * outSize + x] = src[srcY * resolution + srcX] ?? 0;
    }
  }
  return out;
}
