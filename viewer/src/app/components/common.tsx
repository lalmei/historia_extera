import type React from 'react';
import { useMemo, useState } from 'react';
import { href } from '../router';
import type { World } from '../store';
import { KIND_LABELS, kindOf, type EntityId } from '../types';

/**
 * A cross-link to any entity.
 *
 * The whole product is navigation, so this is the most important component here:
 * every entity reference anywhere — in a table, in narrated prose, in a list of a
 * civilization's rulers — routes through it and is clickable.
 */
export function EntityLink({
  world,
  id,
  className = '',
}: {
  world: World;
  id: EntityId | undefined;
  className?: string;
}) {
  if (!id) return <span className="text-[var(--ink-faint)]">—</span>;

  const known = world.byId.has(id);
  const label = world.nameOf(id);

  if (!known) {
    return (
      <span className="text-[var(--ink-faint)]" title={`${id} is not in this world file`}>
        {label}
      </span>
    );
  }

  return (
    <a
      href={href(`/${id}`)}
      title={`${KIND_LABELS[kindOf(id)] ?? 'Entity'} ${id}`}
      className={`underline decoration-[var(--rule)] decoration-1 underline-offset-2 transition-colors hover:text-[var(--accent)] hover:decoration-[var(--accent)] ${className}`}
    >
      {label}
    </a>
  );
}

export function Panel({
  title,
  children,
  actions,
}: {
  title?: string;
  children: React.ReactNode;
  actions?: React.ReactNode;
}) {
  // The header appears for either a title or controls. Requiring a title to show `actions` is
  // how the map's whole control strip — layer, overlays, what the dots mean — stayed invisible
  // from the milestone that added it: the props were passed, the component silently dropped them.
  return (
    <section className="rounded-lg border border-[var(--rule)] bg-[var(--panel)]">
      {(title || actions) && (
        <header className="flex flex-wrap items-baseline justify-between gap-4 border-b border-[var(--rule)] px-4 py-2.5">
          {title ? <h2 className="he-label">{title}</h2> : <span />}
          {actions}
        </header>
      )}
      <div className="p-4">{children}</div>
    </section>
  );
}

export function Stat({
  label,
  value,
  hint,
}: {
  label: string;
  value: React.ReactNode;
  hint?: string;
}) {
  return (
    <div title={hint}>
      <div className="he-label">{label}</div>
      <div className="he-data mt-1 text-lg text-[var(--ink)]">{value}</div>
    </div>
  );
}

export function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex gap-3 border-b border-[var(--rule)] py-1.5 last:border-0">
      <dt className="w-40 shrink-0 text-sm text-[var(--ink-faint)]">{label}</dt>
      <dd className="min-w-0 text-sm">{children}</dd>
    </div>
  );
}

export function Badge({
  children,
  tone = 'neutral',
}: {
  children: React.ReactNode;
  tone?: 'neutral' | 'accent' | 'muted';
}) {
  const tones = {
    neutral: 'border-[var(--rule)] text-[var(--ink-soft)]',
    accent: 'border-[var(--primary)] text-[var(--primary)]',
    muted: 'border-[var(--rule)] text-[var(--ink-faint)]',
  };

  return (
    <span
      className={`inline-flex items-center rounded border px-1.5 py-0.5 text-[0.7rem] font-medium ${tones[tone]}`}
    >
      {children}
    </span>
  );
}

/** One [0, 1] reading, optionally against a second reading of the same dial. */
export interface Dial {
  label: string;
  value: number;
  /** A second reading of the same dial, drawn as a tick on the bar. */
  against?: number;
  hint?: string;
}

/**
 * A set of [0, 1] dials, drawn to be compared with each other and against a baseline.
 *
 * `against` is why this is a component rather than a list of numbers. A realm's effective
 * values say little on their own and a great deal beside its culture's; a person's
 * disposition says little until you can see which way it pulls against the people they were
 * born to. The baseline is a tick on the same bar rather than a second bar, so the gap
 * between the two is the thing the eye lands on.
 */
export function Dials({ dials }: { dials: Dial[] }) {
  return (
    <dl className="space-y-2">
      {dials.map((dial) => (
        <div key={dial.label} className="flex items-center gap-3" title={dial.hint}>
          <dt className="w-28 shrink-0 text-sm text-[var(--ink-faint)]">{dial.label}</dt>
          <dd className="flex min-w-0 flex-1 items-center gap-2">
            <div className="relative h-1.5 flex-1 overflow-hidden rounded-full bg-[var(--rule)]">
              <div
                className="h-full rounded-full bg-[var(--accent)]"
                style={{ width: `${clamp01(dial.value) * 100}%` }}
              />
              {dial.against !== undefined && (
                <span
                  className="absolute top-0 h-full w-0.5 -translate-x-1/2 bg-[var(--ink)]"
                  style={{ left: `${clamp01(dial.against) * 100}%` }}
                />
              )}
            </div>
            <span className="he-data w-9 shrink-0 text-right text-[var(--ink-faint)]">
              {dial.value.toFixed(2)}
            </span>
            {dial.against !== undefined && (
              <span className="he-data w-11 shrink-0 text-right text-[var(--ink-faint)]">
                {signed(dial.value - dial.against)}
              </span>
            )}
          </dd>
        </div>
      ))}
    </dl>
  );
}

function clamp01(value: number): number {
  return Math.min(1, Math.max(0, value));
}

/** `+0.14`, `−0.03`, `·` for a gap too small to mean anything. */
function signed(delta: number): string {
  if (Math.abs(delta) < 0.005) return '·';
  return `${delta > 0 ? '+' : '−'}${Math.abs(delta).toFixed(2)}`;
}

export function PageTitle({
  eyebrow,
  title,
  meta,
}: {
  eyebrow: string;
  title: string;
  meta?: React.ReactNode;
}) {
  return (
    <header className="mb-8">
      <div className="he-label">{eyebrow}</div>
      <h1 className="he-headline mt-2">{title}</h1>
      {meta && <div className="mt-3 flex flex-wrap items-center gap-2 text-sm">{meta}</div>}
    </header>
  );
}

export interface Column<T> {
  key: string;
  header: string;
  cell: (row: T) => React.ReactNode;
  /** Sort key. Omit to make the column unsortable. */
  sort?: (row: T) => number | string;
  align?: 'left' | 'right';
  className?: string;
}

/**
 * One question you can ask of a list: "which tier?", "living or dead?", "whose?".
 *
 * A predicate per option rather than a field name, because most of the interesting questions
 * are not fields — "held a throne" is the length of a list, "still standing" is the absence of
 * an end year, and a realm filter has to look through a settlement's region. Predicates keep
 * all of that at the call site, where the answer is obvious, instead of building a query
 * language nobody asked for.
 */
export interface Facet<T> {
  key: string;
  label: string;
  options: { value: string; label: string; match: (row: T) => boolean }[];
}

/**
 * A searchable, sortable, filterable table.
 *
 * Rows are windowed with a "show more" step rather than paginated, so a world with
 * thousands of settlements stays responsive without the user losing their place.
 * Full virtualisation is the next step if these lists grow another order of
 * magnitude.
 *
 * <b>Facet counts are computed against everything except the facet they belong to.</b> That
 * is the behaviour that makes a set of filters explorable rather than a dead end: narrowing to
 * cities should tell you how many of them are known for mining, not keep insisting there are
 * ninety settlements in the world.
 */
export function DataTable<T>({
  rows,
  columns,
  searchText,
  facets,
  placeholder = 'Search…',
  initialSort,
  emptyMessage = 'Nothing here.',
  pageSize = 100,
}: {
  rows: T[];
  columns: Column<T>[];
  searchText: (row: T) => string;
  facets?: Facet<T>[];
  placeholder?: string;
  initialSort?: { key: string; descending?: boolean };
  emptyMessage?: string;
  pageSize?: number;
}) {
  const [query, setQuery] = useState('');
  const [chosen, setChosen] = useState<Record<string, string>>({});
  const [sortKey, setSortKey] = useState(initialSort?.key ?? columns[0]?.key);
  const [descending, setDescending] = useState(initialSort?.descending ?? false);
  const [limit, setLimit] = useState(pageSize);

  const searched = useMemo(() => {
    const needle = query.trim().toLowerCase();
    return needle ? rows.filter((row) => searchText(row).toLowerCase().includes(needle)) : rows;
  }, [rows, query, searchText]);

  /** The predicate for one facet's current choice, or null when it is not narrowing anything. */
  const predicateFor = (facet: Facet<T>) =>
    facet.options.find((option) => option.value === chosen[facet.key])?.match ?? null;

  const filtered = useMemo(() => {
    let matched = searched;

    for (const facet of facets ?? []) {
      const predicate = predicateFor(facet);
      if (predicate) matched = matched.filter(predicate);
    }

    matched = matched.slice();

    const column = columns.find((c) => c.key === sortKey);
    if (column?.sort) {
      const key = column.sort;
      matched.sort((a, b) => {
        const av = key(a);
        const bv = key(b);
        const order =
          typeof av === 'number' && typeof bv === 'number'
            ? av - bv
            : String(av).localeCompare(String(bv), undefined, { numeric: true });
        return descending ? -order : order;
      });
    }

    return matched;
  }, [searched, facets, chosen, sortKey, descending, columns]);

  const visible = filtered.slice(0, limit);
  const narrowed = query.trim() !== '' || Object.values(chosen).some((value) => value !== '');

  return (
    <div>
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <input
          type="search"
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            setLimit(pageSize);
          }}
          placeholder={placeholder}
          className="w-full max-w-xs rounded border border-[var(--rule)] bg-[var(--input)] px-2.5 py-1.5 text-sm outline-none focus:border-[var(--primary)]"
        />

        {(facets ?? []).map((facet) => {
          // Counted against the rows the *other* facets leave standing, so the numbers
          // describe what choosing this option would actually give you.
          const others = (facets ?? [])
            .filter((other) => other.key !== facet.key)
            .map(predicateFor)
            .filter((predicate): predicate is (row: T) => boolean => predicate !== null);

          const pool = searched.filter((row) => others.every((predicate) => predicate(row)));

          return (
            <select
              key={facet.key}
              value={chosen[facet.key] ?? ''}
              onChange={(event) => {
                setChosen({ ...chosen, [facet.key]: event.target.value });
                setLimit(pageSize);
              }}
              title={facet.label}
              className={`rounded border bg-[var(--input)] px-1.5 py-1.5 text-xs ${
                chosen[facet.key]
                  ? 'border-[var(--primary)] text-[var(--primary)]'
                  : 'border-[var(--rule)]'
              }`}
            >
              <option value="">{facet.label}: any</option>
              {facet.options.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label} ({pool.filter(option.match).length.toLocaleString()})
                </option>
              ))}
            </select>
          );
        })}

        {narrowed && (
          <button
            type="button"
            onClick={() => {
              setQuery('');
              setChosen({});
              setLimit(pageSize);
            }}
            className="text-xs text-[var(--ink-faint)] hover:text-[var(--accent)]"
          >
            clear
          </button>
        )}

        <span className="ml-auto text-xs tabular-nums text-[var(--ink-faint)]">
          {filtered.length.toLocaleString()}
          {filtered.length !== rows.length && ` of ${rows.length.toLocaleString()}`}
        </span>
      </div>

      {/* Wide tables scroll inside this container; the page itself never does. */}
      <div className="-mx-4 overflow-x-auto px-4">
        <table className="w-full min-w-full border-collapse text-sm">
          <thead>
            <tr className="border-b border-[var(--rule)]">
              {columns.map((column) => (
                <th
                  key={column.key}
                  className={`he-label py-2 pr-4 text-left ${
                    column.align === 'right' ? 'text-right' : ''
                  }`}
                >
                  {column.sort ? (
                    <button
                      type="button"
                      onClick={() => {
                        if (sortKey === column.key) setDescending(!descending);
                        else {
                          setSortKey(column.key);
                          setDescending(false);
                        }
                      }}
                      className="inline-flex items-center gap-1 hover:text-[var(--accent)]"
                    >
                      {column.header}
                      {sortKey === column.key && <span>{descending ? '▾' : '▴'}</span>}
                    </button>
                  ) : (
                    column.header
                  )}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {visible.map((row, index) => (
              <tr
                key={index}
                className="border-b border-[var(--rule)] last:border-0 hover:bg-[var(--hover)]"
              >
                {columns.map((column) => (
                  <td
                    key={column.key}
                    className={`py-1.5 pr-4 ${column.align === 'right' ? 'he-data text-right' : ''} ${column.className ?? ''}`}
                  >
                    {column.cell(row)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {filtered.length === 0 && (
        <p className="py-6 text-center text-sm text-[var(--ink-faint)]">{emptyMessage}</p>
      )}

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

export function yearRange(from: number, to?: number): string {
  return to === undefined ? `${from} –` : `${from} – ${to}`;
}
