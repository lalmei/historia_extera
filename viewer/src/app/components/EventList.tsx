import { useMemo, useState } from 'react';
import { narrate } from '../narrate';
import type { World } from '../store';
import type { HistoryEvent } from '../types';
import { EntityLink } from './common';

/**
 * Renders narrated events with every entity slot as a cross-link.
 *
 * This is where the narration templates pay off: the component has no idea what
 * kinds of event exist. It asks `narrate` for parts, prints the text ones, and
 * links the entity ones. New event kinds from later milestones render here with no
 * change at all.
 */
export function NarratedEvent({ world, event }: { world: World; event: HistoryEvent }) {
  const parts = useMemo(
    () => narrate(event, world.export.narration, world.nameOf),
    [event, world],
  );

  return (
    <span>
      {parts.map((part, index) =>
        part.type === 'text' ? (
          <span key={index}>{part.text}</span>
        ) : (
          <EntityLink key={index} world={world} id={part.id} />
        ),
      )}
    </span>
  );
}

/**
 * A filterable chronicle.
 *
 * Kind filters are built from what is actually present rather than from a fixed
 * list, so they extend themselves as the engine gains event kinds.
 */
export function EventList({
  world,
  events,
  emptyMessage = 'No events recorded.',
  showFilters = true,
  pageSize = 150,
}: {
  world: World;
  events: HistoryEvent[];
  emptyMessage?: string;
  showFilters?: boolean;
  pageSize?: number;
}) {
  const [kind, setKind] = useState<string>('all');
  const [limit, setLimit] = useState(pageSize);

  const kinds = useMemo(() => {
    const counts = new Map<string, number>();
    for (const event of events) counts.set(event.kind, (counts.get(event.kind) ?? 0) + 1);
    return [...counts.entries()].sort((a, b) => b[1] - a[1]);
  }, [events]);

  const filtered = useMemo(
    () => (kind === 'all' ? events : events.filter((event) => event.kind === kind)),
    [events, kind],
  );

  const visible = filtered.slice(0, limit);

  if (events.length === 0) {
    return <p className="text-sm text-[var(--ink-faint)]">{emptyMessage}</p>;
  }

  return (
    <div>
      {showFilters && kinds.length > 1 && (
        <div className="mb-3 flex flex-wrap gap-1.5">
          <FilterChip
            label="All"
            count={events.length}
            active={kind === 'all'}
            onClick={() => {
              setKind('all');
              setLimit(pageSize);
            }}
          />
          {kinds.map(([name, count]) => (
            <FilterChip
              key={name}
              label={humanise(name)}
              count={count}
              active={kind === name}
              onClick={() => {
                setKind(name);
                setLimit(pageSize);
              }}
            />
          ))}
        </div>
      )}

      <ol className="space-y-0">
        {visible.map((event) => (
          <li
            key={event.id}
            className="flex gap-3 border-b border-[var(--rule)]/50 py-1.5 last:border-0"
          >
            <span
              className="w-14 shrink-0 text-right text-sm tabular-nums text-[var(--ink-faint)]"
              title={`Year ${event.year}`}
            >
              {event.year}
            </span>
            <span className="min-w-0 text-sm leading-relaxed">
              <NarratedEvent world={world} event={event} />
            </span>
          </li>
        ))}
      </ol>

      {filtered.length > limit && (
        <button
          type="button"
          onClick={() => setLimit(limit + pageSize * 4)}
          className="mt-3 w-full rounded border border-[var(--rule)] py-1.5 text-sm text-[var(--ink-soft)] hover:border-[var(--accent)] hover:text-[var(--accent)]"
        >
          Show more ({(filtered.length - limit).toLocaleString()} remaining)
        </button>
      )}
    </div>
  );
}

function FilterChip({
  label,
  count,
  active,
  onClick,
}: {
  label: string;
  count: number;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded border px-2 py-0.5 text-xs transition-colors ${
        active
          ? 'border-[var(--accent)] bg-[var(--accent-soft)] text-[var(--accent)]'
          : 'border-[var(--rule)] text-[var(--ink-soft)] hover:border-[var(--accent)]'
      }`}
    >
      {label}
      <span className="ml-1.5 tabular-nums opacity-60">{count.toLocaleString()}</span>
    </button>
  );
}

/** `SettlementFounded` → `Settlement founded`. */
export function humanise(kind: string): string {
  const spaced = kind.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}
