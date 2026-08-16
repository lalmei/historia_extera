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
  error?: string;
}

export interface DeletedWorld {
  name: string;
  permanent: boolean;
  recoveryPath?: string;
}

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

  return (body as { worlds: SavedWorld[] }).worlds;
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
