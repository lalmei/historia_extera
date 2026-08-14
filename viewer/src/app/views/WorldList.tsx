import { useEffect, useState } from 'react';
import { Badge, Panel } from '../components/common';
import { SCHEMA_VERSION } from '../types';
import { deleteWorld, listWorlds, type SavedWorld } from '../worlds';

type DeleteMode = 'trash' | 'permanent';

export function WorldList() {
  const [worlds, setWorlds] = useState<SavedWorld[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<{ name: string; mode: DeleteMode } | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

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

  async function remove(world: SavedWorld, mode: DeleteMode) {
    const permanent = mode === 'permanent';
    const confirmed = window.confirm(
      permanent
        ? `Permanently delete ${world.name}?\n\nThis cannot be undone. The file will not be moved to trash.`
        : `Move ${world.name} to trash?\n\n` +
            'The file will be moved to viewer/public/worlds/.trash/ so it can be recovered.',
    );
    if (!confirmed) return;

    setDeleting({ name: world.name, mode });
    setActionError(null);
    setNotice(null);

    try {
      const deleted = await deleteWorld(world.name, permanent);
      setWorlds((current) => current?.filter((item) => item.name !== world.name) ?? null);
      setNotice(
        deleted.permanent
          ? `${deleted.name} permanently deleted`
          : `${deleted.name} moved to ${deleted.recoveryPath}`,
      );
    } catch (cause) {
      setActionError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setDeleting(null);
    }
  }

  return (
    <Panel
      title="Generated worlds"
      actions={<span className="text-xs text-[var(--ink-faint)]">Viewer schema v{SCHEMA_VERSION}</span>}
    >
      {notice && (
        <p aria-live="polite" className="mb-3 text-sm text-[var(--ink-soft)]">
          {notice}
        </p>
      )}
      {actionError && (
        <p aria-live="assertive" className="mb-3 text-sm text-[var(--accent)]">
          Could not delete world: {actionError}
        </p>
      )}

      {error ? (
        <p className="text-sm text-[var(--accent)]">Could not list worlds: {error}</p>
      ) : worlds === null ? (
        <p className="text-sm text-[var(--ink-faint)]">Looking for earlier exports…</p>
      ) : worlds.length === 0 ? (
        <p className="text-sm text-[var(--ink-faint)]">
          No generated worlds yet. The first completed run will appear here.
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-2xl border-collapse text-left text-sm">
            <thead className="text-[0.7rem] font-medium tracking-wide uppercase text-[var(--ink-faint)]">
              <tr>
                <th className="border-b border-[var(--rule)] pb-2 font-medium">World</th>
                <th className="border-b border-[var(--rule)] pb-2 font-medium">Modified</th>
                <th className="border-b border-[var(--rule)] pb-2 text-right font-medium">Size</th>
                <th className="border-b border-[var(--rule)] pb-2 pl-4 font-medium">Compatibility</th>
                <th className="border-b border-[var(--rule)] pb-2 pl-4 text-right font-medium">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody>
              {worlds.map((world) => (
                <WorldRow
                  key={world.name}
                  world={world}
                  deleting={deleting?.name === world.name ? deleting.mode : null}
                  deleteDisabled={deleting !== null}
                  onDelete={(mode) => remove(world, mode)}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}

function WorldRow({
  world,
  deleting,
  deleteDisabled,
  onDelete,
}: {
  world: SavedWorld;
  deleting: DeleteMode | null;
  deleteDisabled: boolean;
  onDelete: (mode: DeleteMode) => void;
}) {
  const compatible = world.schemaVersion === SCHEMA_VERSION;

  return (
    <tr className="border-b border-[var(--rule)] last:border-0">
      <td className="max-w-sm py-3 pr-4 font-mono text-xs break-all">{world.name}</td>
      <td className="py-3 pr-4 whitespace-nowrap text-[var(--ink-soft)]">
        {formatDate(world.modifiedAt)}
      </td>
      <td className="py-3 text-right tabular-nums whitespace-nowrap text-[var(--ink-soft)]">
        {formatBytes(world.bytes)}
      </td>
      <td className="py-3 pl-4 whitespace-nowrap">
        {compatible ? (
          <Badge tone="accent">Schema v{world.schemaVersion} · ready</Badge>
        ) : world.schemaVersion === null ? (
          <span title={world.error} className="text-xs text-[var(--accent)]">
            Could not inspect
          </span>
        ) : (
          <Badge tone="muted">
            Schema v{world.schemaVersion} · needs v{SCHEMA_VERSION}
          </Badge>
        )}
      </td>
      <td className="py-3 pl-4 text-right whitespace-nowrap">
        <div className="flex items-center justify-end gap-2">
          {compatible ? (
            <a
              href={viewerUrl(world.world)}
              className="rounded border border-[var(--accent)] bg-[var(--accent-soft)] px-2.5 py-1 text-xs font-medium text-[var(--accent)]"
            >
              Open
            </a>
          ) : (
            <span className="text-xs text-[var(--ink-faint)]">Unavailable</span>
          )}
          <button
            type="button"
            disabled={deleteDisabled}
            onClick={() => onDelete('trash')}
            className="rounded border border-[var(--rule)] px-2.5 py-1 text-xs text-[var(--ink-soft)] transition-colors hover:border-[var(--accent)] hover:text-[var(--accent)] disabled:opacity-40"
          >
            {deleting === 'trash' ? 'Moving…' : 'Move to trash'}
          </button>
          <button
            type="button"
            disabled={deleteDisabled}
            onClick={() => onDelete('permanent')}
            className="rounded border border-[var(--accent)] px-2.5 py-1 text-xs text-[var(--accent)] transition-colors hover:bg-[var(--accent-soft)] disabled:opacity-40"
          >
            {deleting === 'permanent' ? 'Deleting…' : 'Delete permanently'}
          </button>
        </div>
      </td>
    </tr>
  );
}

function viewerUrl(world: string): string {
  return `${import.meta.env.BASE_URL}?world=${encodeURIComponent(world)}`;
}

function formatDate(value: string | undefined): string {
  if (!value) return '—';

  return new Date(value).toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  });
}

function formatBytes(bytes: number): string {
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}
