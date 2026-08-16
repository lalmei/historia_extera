import { useMemo, useState } from 'react';
import { narrate, unnarrated } from '../narrate';
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
 *
 * `separateRegister` is for the world chronicle and deliberately not for entity pages.
 * Reading three centuries of a world, the ordinary births and deaths outnumber the
 * history four to one and there is nothing to be gained from scrolling past them; reading
 * one person, those same events are their life and hiding them would show someone who
 * arrives in the world fully grown. Same log, two questions.
 */
export function EventList({
  world,
  events,
  emptyMessage = 'No events recorded.',
  showFilters = true,
  separateRegister = false,
  pageSize = 150,
}: {
  world: World;
  events: HistoryEvent[];
  emptyMessage?: string;
  showFilters?: boolean;
  separateRegister?: boolean;
  pageSize?: number;
}) {
  const [kind, setKind] = useState<string>('all');
  const [limit, setLimit] = useState(pageSize);
  const [showRecord, setShowRecord] = useState(false);
  const [showRegister, setShowRegister] = useState(false);

  const spine = useMemo(
    () =>
      separateRegister && !showRegister
        ? events.filter((event) => event.significance !== 'Routine')
        : events,
    [events, separateRegister, showRegister],
  );

  const registerCount = useMemo(
    () =>
      separateRegister
        ? events.reduce((count, event) => count + (event.significance === 'Routine' ? 1 : 0), 0)
        : 0,
    [events, separateRegister],
  );

  // Counted over the spine rather than the whole log, so a chip's number matches what
  // clicking it will actually show.
  const kinds = useMemo(() => {
    const counts = new Map<string, number>();
    for (const event of spine) counts.set(event.kind, (counts.get(event.kind) ?? 0) + 1);
    return [...counts.entries()].sort((a, b) => b[1] - a[1]);
  }, [spine]);

  const filtered = useMemo(
    () => (kind === 'all' ? spine : spine.filter((event) => event.kind === kind)),
    [spine, kind],
  );

  const visible = filtered.slice(0, limit);

  if (events.length === 0) {
    return <p className="text-sm text-[var(--ink-faint)]">{emptyMessage}</p>;
  }

  return (
    <div>
      {showFilters && (
        <div className="mb-3 flex flex-wrap items-center gap-1.5">
          {kinds.length > 1 && (
            <>
              <FilterChip
                label="All"
                count={spine.length}
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
            </>
          )}

          {/* Off by default, which is the whole point of the significance field: the register is
              most of the log by volume and almost none of it by meaning. On, the chronicle is
              every event the engine wrote, in order, exactly as before. */}
          {separateRegister && registerCount > 0 && (
            <button
              type="button"
              onClick={() => {
                setShowRegister(!showRegister);
                setKind('all');
                setLimit(pageSize);
              }}
              title="Ordinary births, deaths, marriages and consort appointments — true, and not the history"
              className={`rounded border px-2 py-0.5 text-xs transition-colors ${
                showRegister
                  ? 'border-[var(--primary)] text-[var(--primary)]'
                  : 'border-[var(--rule)] text-[var(--ink-soft)] hover:border-[var(--primary)]'
              }`}
            >
              {showRegister
                ? 'Hide the register'
                : `The register (${registerCount.toLocaleString()})`}
            </button>
          )}

          {/* Off by default: the prose is the point, and a chronicle with a payload under every
              line is a log file. On, nothing the engine attached is hidden. */}
          <button
            type="button"
            onClick={() => setShowRecord(!showRecord)}
            title="Show what each event carries that its narration does not print"
            className={`ml-auto rounded border px-2 py-0.5 text-xs transition-colors ${
              showRecord
                ? 'border-[var(--accent)] bg-[var(--accent-soft)] text-[var(--accent)]'
                : 'border-[var(--rule)] text-[var(--ink-soft)] hover:border-[var(--accent)]'
            }`}
          >
            {showRecord ? 'Hide the record' : 'The record'}
          </button>
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
              {showRecord && <EventRecord world={world} event={event} />}
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

/**
 * What an event carries that its narration does not print.
 *
 * Templates are prose and leave things out — a coronation reads better without the new king's
 * age in it, and a battle without the id of the war it belongs to — but all of it is in the
 * export, and none of it was reachable from any page until this. Kind-agnostic like everything
 * else here: it prints whatever the template did not consume, so an event kind added later
 * needs no change.
 */
function EventRecord({ world, event }: { world: World; event: HistoryEvent }) {
  const { data, extra } = useMemo(
    () => unnarrated(event, world.export.narration),
    [event, world],
  );

  if (data.length === 0 && extra.length === 0) return null;

  return (
    <span className="mt-0.5 flex flex-wrap items-baseline gap-x-3 gap-y-0.5 text-xs text-[var(--ink-faint)]">
      {data.map(([key, value]) => (
        <span key={key}>
          <span className="tracking-wide uppercase">{key}</span> {value}
        </span>
      ))}
      {extra.map((id) => (
        <EntityLink key={id} world={world} id={id} />
      ))}
    </span>
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
