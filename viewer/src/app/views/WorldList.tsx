import type React from 'react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { IconChevronRight, IconPlay, IconRefresh, IconTrash } from '../components/icons';
import { generateHref } from '../components/SiteChrome';
import {
  DEFAULT_PARAMS,
  POLL_MS,
  cancelRun,
  generatorUrl,
  paramsFromFilename,
  readRun,
  startRun,
  worldFileName,
  type Run,
  type RunParams,
} from '../generate';
import { SynthesisModal } from './NewWorld';
import { schemaVerdict, type SchemaVerdict } from '../compat';
import { BIOME_ORDER, FLAG_COAST, type Biome } from '../types';
import {
  displayNameOf,
  listWorlds,
  loadWorldExpansion,
  deleteWorld,
  type SavedWorld,
  type WorldEndState,
  type WorldPreview,
} from '../worlds';

export function WorldList({ query = '' }: { query?: string }) {
  const [worlds, setWorlds] = useState<SavedWorld[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [run, setRun] = useState<Run | null>(null);
  const [opening, setOpening] = useState(false);

  const running = run?.status === 'running';
  const synthesizing = run !== null && run.status !== 'cancelled';

  // Poll while the CLI is working. Keyed on id and status rather than the run object, so an
  // answer that only moved the clock forward does not tear the timer down and rebuild it.
  useEffect(() => {
    if (run?.status !== 'running') return;

    const id = run.id;
    let stopped = false;

    const timer = setInterval(async () => {
      try {
        const next = await readRun(id);
        if (!stopped) setRun(next);
      } catch (cause) {
        if (stopped) return;
        setActionError(messageOf(cause));
        setRun(null);
      }
    }, POLL_MS);

    return () => {
      stopped = true;
      clearInterval(timer);
    };
  }, [run?.id, run?.status]);

  // Then open what came out. Running a seed and staying on the catalog would leave the reader
  // to find the row again and guess which of two identical-looking exports was the new one.
  useEffect(() => {
    if (run?.status !== 'done' || !run.world) return;
    setOpening(true);
    window.location.assign(`${import.meta.env.BASE_URL}?world=${run.world}`);
  }, [run?.status, run?.world]);

  useEffect(() => {
    if (!synthesizing) return;
    const previous = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = previous;
    };
  }, [synthesizing]);

  useEffect(() => {
    let stopped = false;

    listWorlds()
      .then((found) => {
        if (!stopped) setWorlds(found);
      })
      .catch((cause: unknown) => {
        if (!stopped) setError(cause instanceof Error ? cause.message : String(cause));
      });

    return () => {
      stopped = true;
    };
  }, []);

  const visible = useMemo(() => {
    if (!worlds) return [];
    const needle = query.trim().toLowerCase();
    if (!needle) return worlds;
    return worlds.filter((world) => matchesQuery(world, needle));
  }, [worlds, query]);

  /**
   * Runs a saved world's settings again, here, rather than opening a form about them.
   *
   * The Run and Regenerate buttons used to be the same link to `/new`, so pressing Run ran
   * nothing: it filled in a form the reader then had to submit, and — when the settings were
   * unchanged — confirm an overwrite of. Run now means run. Regenerate still opens the form,
   * which is the one place these numbers can be changed before the engine sees them.
   */
  async function rerun(world: SavedWorld) {
    const params = worldParams(world);
    if (!params) return;

    // The generator names its output from the settings, so an older export whose filename
    // predates the size suffix is reproduced beside itself rather than over itself. Say which.
    const output = worldFileName(params);
    const fate =
      output === world.name
        ? `This replaces ${world.name} on disk.`
        : `This writes ${output}; ${world.name} stays on disk.`;

    const confirmed = window.confirm(
      `Run ${displayNameOf(world)} again?\n\n` +
        `Seed ${params.seed}, ${params.years.toLocaleString()} years, ${params.civs} ` +
        `civilization${params.civs === 1 ? '' : 's'}, ${params.size.toLocaleString()} units — ` +
        `through the engine as it stands now.\n\n${fate}`,
    );
    if (!confirmed) return;

    setActionError(null);
    setNotice(null);

    try {
      setRun(await startRun(params));
    } catch (cause) {
      setActionError(messageOf(cause));
    }
  }

  async function abandon() {
    if (!run) return;

    try {
      await cancelRun(run.id);
    } catch (cause) {
      setActionError(messageOf(cause));
    } finally {
      setRun(null);
      setOpening(false);
      setNotice('Synthesis aborted.');
    }
  }

  function dismissFailed() {
    setActionError(run?.error ?? 'The generator failed');
    setRun(null);
    setOpening(false);
  }

  async function remove(world: SavedWorld) {
    const confirmed = window.confirm(
      `Permanently delete ${world.name}?\n\nThis cannot be undone. The file will not be moved to trash.`,
    );
    if (!confirmed) return;

    setDeleting(world.name);
    setActionError(null);
    setNotice(null);

    try {
      const deleted = await deleteWorld(world.name, true);
      setWorlds((current) => current?.filter((item) => item.name !== world.name) ?? null);
      setNotice(`${deleted.name} permanently deleted`);
    } catch (cause) {
      setActionError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setDeleting(null);
    }
  }

  return (
    <div>
      {notice && (
        <p aria-live="polite" className="mb-3 text-sm text-[var(--ink-soft)]">
          {notice}
        </p>
      )}
      {actionError && (
        <p aria-live="assertive" className="mb-3 text-sm text-[var(--error)]">
          Could not delete world: {actionError}
        </p>
      )}

      {error ? (
        <p className="text-sm text-[var(--error)]">Could not list worlds: {error}</p>
      ) : worlds === null ? (
        <p className="text-sm text-[var(--ink-faint)]">Looking for earlier exports…</p>
      ) : worlds.length === 0 ? (
        <EmptyLibrary />
      ) : visible.length === 0 ? (
        <p className="border border-[var(--rule)] px-4 py-10 text-center text-sm text-[var(--ink-faint)]">
          No worlds match “{query.trim()}”.
        </p>
      ) : (
        <div className="overflow-x-auto border border-[var(--rule)]">
          <table className="w-full min-w-[64rem] border-collapse text-left text-sm">
            <thead>
              <tr>
                <th className="he-label px-5 pt-4 pb-3">World</th>
                <th className="he-label px-5 pt-4 pb-3">Exported</th>
                <th className="he-label px-5 pt-4 pb-3">Seed hash</th>
                <th className="he-label px-5 pt-4 pb-3">Sim years</th>
                <th className="he-label px-5 pt-4 pb-3">Civilizations</th>
                <th className="he-label px-5 pt-4 pb-3 text-right">Directives</th>
              </tr>
            </thead>
            <tbody>
              {visible.map((world) => (
                <WorldRow
                  key={world.name}
                  world={world}
                  deleting={deleting === world.name}
                  deleteDisabled={deleting !== null || running}
                  runDisabled={running || opening}
                  onRerun={() => rerun(world)}
                  onDelete={() => remove(world)}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}

      {synthesizing && run && (
        <SynthesisModal
          run={run}
          opening={opening}
          onAbort={abandon}
          onDismiss={dismissFailed}
        />
      )}
    </div>
  );
}

function messageOf(cause: unknown): string {
  return cause instanceof Error ? cause.message : String(cause);
}

function EmptyLibrary() {
  return (
    <div className="border border-[var(--rule)] px-4 py-12 text-center">
      <p className="text-sm text-[var(--ink-faint)]">
        No generated worlds yet. The first completed run will appear here.
      </p>
      <a href={generateHref()} className="he-btn-primary mt-4 inline-block px-3 py-1.5 text-sm font-medium">
        Generate a world
      </a>
    </div>
  );
}

function WorldRow({
  world,
  deleting,
  deleteDisabled,
  runDisabled,
  onRerun,
  onDelete,
}: {
  world: SavedWorld;
  deleting: boolean;
  deleteDisabled: boolean;
  runDisabled: boolean;
  onRerun: () => void;
  onDelete: () => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const [expansion, setExpansion] = useState<{
    preview: WorldPreview;
    endState: WorldEndState;
  } | null>(null);
  const [expansionError, setExpansionError] = useState(false);
  const [expansionLoading, setExpansionLoading] = useState(false);
  // Three outcomes, not two: current, older-but-readable, and refused. Collapsing the middle
  // one into "incompatible" is what used to hide a perfectly readable world behind a greyed
  // name, and a title attribute nobody hovers is not a way to say which of the three it is.
  const verdict = schemaVerdict(world.schemaVersion);
  const params = worldParams(world);
  const title = displayNameOf(world);
  const openUrl = verdict.readable ? viewerUrl(world.world) : undefined;
  const openHint = verdict.readable
    ? `Open ${title}`
    : verdict.state === 'unreadable'
      ? (world.error ?? verdict.summary)
      : verdict.summary;

  const toggle = () => setExpanded((current) => !current);

  useEffect(() => {
    if (!expanded || expansion) return;
    let cancelled = false;
    setExpansionLoading(true);
    setExpansionError(false);
    loadWorldExpansion(world.world)
      .then((loaded) => {
        if (!cancelled) setExpansion(loaded);
      })
      .catch(() => {
        if (!cancelled) setExpansionError(true);
      })
      .finally(() => {
        if (!cancelled) setExpansionLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [expanded, expansion, world.world]);

  return (
    <>
      <tr
        className="cursor-pointer border-t border-[var(--rule)] hover:bg-[var(--hover)]"
        onClick={(event) => {
          if ((event.target as HTMLElement).closest('a, .he-row-actions')) return;
          toggle();
        }}
      >
        <td className="max-w-sm px-5 py-4">
          <div className="flex items-start gap-2">
            <button
              type="button"
              aria-expanded={expanded}
              aria-label={expanded ? `Collapse ${title}` : `Expand ${title}`}
              // The row toggles too, so without stopping here the chevron toggled twice and
              // therefore never at all — which left the compatibility detail below reachable
              // only by clicking somewhere other than the control that says it opens it.
              onClick={(event) => {
                event.stopPropagation();
                toggle();
              }}
              className="mt-0.5 inline-flex h-6 w-6 shrink-0 items-center justify-center rounded text-[var(--ink-faint)] hover:text-[var(--primary)]"
            >
              <IconChevronRight
                className={`h-4 w-4 transition-transform ${expanded ? 'rotate-90' : ''}`}
              />
            </button>
            <div className="min-w-0">
              {openUrl ? (
                <a href={openUrl} className="font-medium text-[var(--ink)] hover:text-[var(--primary)]">
                  {title}
                </a>
              ) : (
                <div className="font-medium text-[var(--ink-soft)]" title={openHint}>
                  {title}
                </div>
              )}
              {world.designation && world.worldName && (
                <div className="mt-0.5 truncate text-xs text-[var(--ink-faint)]">
                  {world.designation}
                </div>
              )}
              <SchemaChip verdict={verdict} />
            </div>
          </div>
        </td>
        <td className="he-data whitespace-nowrap px-5 py-4 text-[var(--ink-soft)]">
          {formatExportDate(world.modifiedAt)}
        </td>
        <td className="he-data px-5 py-4 text-[var(--ink-soft)]">
          {params ? params.seed : '—'}
        </td>
        <td className="he-data px-5 py-4 text-[var(--ink-soft)]">
          {params ? params.years.toLocaleString() : '—'}
        </td>
        <td className="he-data px-5 py-4 text-[var(--ink-soft)]">
          {params ? params.civs : '—'}
        </td>
        <td className="px-5 py-4">
          <div className="he-row-actions flex items-center justify-end gap-0.5">
            <IconAction
              disabled={!params || runDisabled}
              onClick={onRerun}
              label={
                params
                  ? `Run ${title} again through the current engine`
                  : 'Settings unknown'
              }
            >
              <IconPlay className="h-4 w-4" />
            </IconAction>
            <IconAction
              href={params ? generatorUrl(params, world.name) : undefined}
              disabled={!params || runDisabled}
              label={
                params
                  ? `Open ${title}'s settings in the generator`
                  : 'Settings unknown'
              }
            >
              <IconRefresh className="h-4 w-4" />
            </IconAction>
            <IconAction
              disabled={deleteDisabled}
              destructive
              label={deleting ? 'Deleting…' : `Permanently delete ${title}`}
              onClick={onDelete}
            >
              <IconTrash className="h-4 w-4" />
            </IconAction>
          </div>
        </td>
      </tr>
      {expanded && (
        <tr className="border-t border-[var(--rule)] bg-[var(--surface-container-low)]">
          <td colSpan={6} className="px-5 py-4">
            <div className="flex flex-wrap gap-6 lg:flex-nowrap">
              <WorldThumb
                preview={expansion?.preview}
                loading={expansionLoading}
                error={expansionError && !expansion}
              />
              <dl className="grid w-full max-w-sm shrink-0 grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-sm lg:w-72">
                {world.designation && (
                  <>
                    <dt className="text-[var(--ink-faint)]">Designation</dt>
                    <dd>{world.designation}</dd>
                  </>
                )}
                <dt className="text-[var(--ink-faint)]">Kind</dt>
                <dd>{world.kind ?? '—'}</dd>
                <dt className="text-[var(--ink-faint)]">Extent</dt>
                <dd>
                  {params
                    ? `${params.size.toLocaleString()} × ${params.size.toLocaleString()} units`
                    : '—'}
                  {params?.eastWestPeriodic ? ' · east/west joined' : ''}
                </dd>
                <dt className="text-[var(--ink-faint)]">Engine</dt>
                <dd className="he-data">
                  {world.engineVersion ?? '—'}
                  {world.schemaVersion !== null ? ` · schema v${world.schemaVersion}` : ''}
                </dd>
                <dt className="text-[var(--ink-faint)]">Reads</dt>
                <dd>
                  <p className="text-[var(--ink-soft)]">{verdict.summary}</p>
                  {verdict.missing.length > 0 && (
                    <ul className="mt-1.5 list-disc space-y-0.5 pl-4 text-xs text-[var(--ink-faint)]">
                      {verdict.missing.map((feature) => (
                        <li key={feature}>{feature}</li>
                      ))}
                    </ul>
                  )}
                </dd>
                <dt className="text-[var(--ink-faint)]">File</dt>
                <dd className="he-data min-w-0">
                  <span className="block truncate" title={world.name}>
                    {world.name}
                  </span>
                  <span className="text-[var(--ink-faint)]">{formatBytes(world.bytes)}</span>
                </dd>
                {openUrl && (
                  <>
                    <dt className="text-[var(--ink-faint)]">Read</dt>
                    <dd>
                      <a href={openUrl} className="text-[var(--primary)] hover:underline">
                        Open chronicle
                      </a>
                    </dd>
                  </>
                )}
              </dl>
              <EndStatePanel
                eventCount={world.eventCount}
                endState={expansion?.endState}
                loading={expansionLoading}
                error={expansionError && !expansion}
              />
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

/**
 * The row's compatibility, in the row.
 *
 * This used to live only in a `title` attribute on a greyed-out name, which meant the library
 * looked like it had lost half the worlds on disk and said why only to a reader who happened
 * to rest a pointer on one. A world that still opens says so; one that does not says why, in
 * the place the eye already is.
 */
function SchemaChip({ verdict }: { verdict: SchemaVerdict }) {
  if (verdict.state === 'current') return null;

  const label =
    verdict.state === 'older'
      ? `schema v${verdict.version} · older engine`
      : verdict.state === 'too-new'
        ? `schema v${verdict.version} · newer engine`
        : verdict.state === 'too-old'
          ? `schema v${verdict.version} · too old to read`
          : 'unreadable export';

  const tone =
    verdict.state === 'older'
      ? 'border-[var(--rule)] text-[var(--ink-faint)]'
      : 'border-[var(--error)] text-[var(--error)]';

  return (
    <span
      title={verdict.summary}
      className={`mt-1 inline-block rounded border px-1.5 py-0.5 text-[11px] tracking-wide uppercase ${tone}`}
    >
      {label}
    </span>
  );
}

const PREVIEW_COLOURS: Record<Biome, [number, number, number]> = {
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

function EndStatePanel({
  eventCount,
  endState,
  loading,
  error,
}: {
  eventCount?: number | null;
  endState?: WorldEndState;
  loading: boolean;
  error: boolean;
}) {
  if (error) {
    return (
      <p className="min-w-0 flex-1 text-sm text-[var(--ink-faint)]">Could not read the end of this history.</p>
    );
  }

  if (loading || !endState) {
    return (
      <p className="min-w-0 flex-1 text-sm text-[var(--ink-faint)]">Reading how it ended…</p>
    );
  }

  const standing = endState.civilizations.filter((civ) => civ.endedYear === undefined);
  const inhabited = endState.settlements.filter((settlement) => settlement.abandonedYear === undefined);
  const cities = inhabited.filter((settlement) => settlement.tier === 'City');
  const extant = endState.dynasties.filter((house) => house.endedYear === undefined);
  const openWars = endState.wars.filter((war) => war.endedYear === undefined);
  const activeRoutes = endState.tradeRoutes.filter((route) => route.endedYear === undefined);
  const livingFaiths = endState.religions.filter((faith) => faith.endedYear === undefined);
  const realms = [...endState.civilizations].sort((a, b) => {
    const aGone = a.endedYear !== undefined;
    const bGone = b.endedYear !== undefined;
    if (aGone !== bGone) return aGone ? 1 : -1;
    return b.population - a.population;
  });

  const stats: { label: string; value: string }[] = [
    {
      label: 'Civilizations',
      value: `${standing.length} / ${endState.civilizations.length}`,
    },
    {
      label: 'Settlements',
      value: `${inhabited.length} · ${cities.length} ${cities.length === 1 ? 'city' : 'cities'}`,
    },
    {
      label: 'Houses',
      value: `${extant.length} / ${endState.dynasties.length}`,
    },
    {
      label: 'Wars',
      value: `${openWars.length} / ${endState.wars.length}`,
    },
    {
      label: 'Trade',
      value: `${activeRoutes.length} / ${endState.tradeRoutes.length}`,
    },
    {
      label: 'Faiths',
      value: `${livingFaiths.length} / ${endState.religions.length}`,
    },
    { label: 'Battles', value: endState.battles.toLocaleString() },
    { label: 'Artifacts', value: endState.artifacts.toLocaleString() },
    { label: 'Holy sites', value: endState.holySites.toLocaleString() },
  ];

  if (eventCount != null) {
    stats.unshift({ label: 'Events', value: eventCount.toLocaleString() });
  }

  return (
    <div className="min-w-0 flex-1">
      <div className="he-label mb-2">At year’s end</div>
      <div className="grid grid-cols-2 gap-x-6 gap-y-1.5 text-sm sm:grid-cols-3">
        {stats.map((stat) => (
          <div key={stat.label}>
            <div className="text-[11px] text-[var(--ink-faint)]">{stat.label}</div>
            <div className="he-data">{stat.value}</div>
          </div>
        ))}
      </div>
      {realms.length > 0 && (
        <ol className="mt-3 columns-1 gap-x-6 text-sm sm:columns-2">
          {realms.map((civ) => (
            <li key={civ.name} className="mb-1 flex break-inside-avoid items-baseline justify-between gap-3">
              <span className={civ.endedYear === undefined ? '' : 'text-[var(--ink-faint)] line-through'}>
                {civ.name}
              </span>
              <span className="he-data shrink-0 text-[var(--ink-faint)]">
                {civ.population.toLocaleString()}
              </span>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}

function WorldThumb({
  preview,
  loading,
  error,
}: {
  preview?: WorldPreview;
  loading: boolean;
  error: boolean;
}) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  useEffect(() => {
    if (!preview || !canvasRef.current) return;
    paintPreview(canvasRef.current, preview);
  }, [preview]);

  return (
    <div className="relative h-32 w-32 shrink-0 overflow-hidden border border-[var(--rule)] bg-[var(--canvas)]">
      {loading && (
        <p className="absolute inset-0 flex items-center justify-center p-2 text-center text-[11px] text-[var(--ink-faint)]">
          Reading terrain…
        </p>
      )}
      {error && (
        <p className="absolute inset-0 flex items-center justify-center p-2 text-center text-[11px] text-[var(--ink-faint)]">
          No map preview
        </p>
      )}
      <canvas
        ref={canvasRef}
        width={128}
        height={128}
        className={`h-full w-full ${preview ? '' : 'opacity-0'}`}
      />
    </div>
  );
}

function paintPreview(
  canvas: HTMLCanvasElement,
  preview: WorldPreview,
) {
  const context = canvas.getContext('2d');
  if (!context) return;

  const { size, height, biome, flags, minHeight, maxHeight } = preview;
  canvas.width = size;
  canvas.height = size;
  const image = context.createImageData(size, size);
  const span = Math.max(1e-6, maxHeight - minHeight);
  const seaByte = ((0 - minHeight) / span) * 255;

  for (let i = 0; i < size * size; i++) {
    const biomeName = BIOME_ORDER[biome[i]] ?? 'Ocean';
    let [r, g, b] = PREVIEW_COLOURS[biomeName] ?? [120, 120, 120];
    const heightByte = height[i];
    const submerged = heightByte < seaByte;
    if (!submerged) {
      const relief = 0.82 + ((heightByte - seaByte) / Math.max(1, 255 - seaByte)) * 0.42;
      r = Math.min(255, r * relief);
      g = Math.min(255, g * relief);
      b = Math.min(255, b * relief);
    }
    if ((flags[i] & FLAG_COAST) !== 0) {
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

  context.imageSmoothingEnabled = false;
  context.putImageData(image, 0, 0);
}

function IconAction({
  href,
  disabled,
  destructive,
  label,
  onClick,
  children,
}: {
  href?: string;
  disabled?: boolean;
  destructive?: boolean;
  label: string;
  onClick?: () => void;
  children: React.ReactNode;
}) {
  const className = destructive
    ? 'inline-flex h-8 w-8 items-center justify-center rounded text-[var(--ink-faint)] transition-colors hover:text-[var(--error)] disabled:pointer-events-none disabled:opacity-35'
    : 'inline-flex h-8 w-8 items-center justify-center rounded text-[var(--ink-faint)] transition-colors hover:text-[var(--primary)] disabled:pointer-events-none disabled:opacity-35';

  if (href && !disabled) {
    return (
      <a href={href} title={label} aria-label={label} className={className}>
        {children}
      </a>
    );
  }

  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      disabled={disabled}
      onClick={onClick}
      className={className}
    >
      {children}
    </button>
  );
}

function matchesQuery(world: SavedWorld, needle: string): boolean {
  const params = worldParams(world);
  const haystack = [
    world.worldName,
    world.designation,
    world.name,
    world.engineVersion,
    params ? String(params.seed) : '',
  ]
    .filter(Boolean)
    .join(' ')
    .toLowerCase();
  return haystack.includes(needle);
}

function worldParams(world: SavedWorld): RunParams | null {
  if (world.params) return world.params;

  const named = paramsFromFilename(world.name);
  if (named?.seed === undefined || named.years === undefined || named.civs === undefined) {
    return null;
  }

  return {
    seed: named.seed,
    years: named.years,
    civs: named.civs,
    size: named.size ?? DEFAULT_PARAMS.size,
    eastWestPeriodic: named.eastWestPeriodic ?? false,
  };
}

function viewerUrl(world: string): string {
  return `${import.meta.env.BASE_URL}?world=${encodeURIComponent(world)}`;
}

function formatExportDate(value: string | undefined): string {
  if (!value) return '—';

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '—';

  return `${date.toISOString().replace('T', ' ').slice(0, 16)}Z`;
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
