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
  return (
    <section className="rounded-lg border border-[var(--rule)] bg-[var(--panel)]">
      {title && (
        <header className="flex items-baseline justify-between gap-4 border-b border-[var(--rule)] px-4 py-2.5">
          <h2 className="font-serif text-sm font-semibold tracking-wide uppercase text-[var(--ink-soft)]">
            {title}
          </h2>
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
      <div className="text-[0.7rem] font-medium tracking-wide uppercase text-[var(--ink-faint)]">
        {label}
      </div>
      <div className="mt-0.5 font-serif text-lg tabular-nums">{value}</div>
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
    accent: 'border-[var(--accent)] bg-[var(--accent-soft)] text-[var(--accent)]',
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
    <header className="mb-6">
      <div className="text-xs font-medium tracking-widest uppercase text-[var(--ink-faint)]">
        {eyebrow}
      </div>
      <h1 className="mt-1 font-serif text-3xl">{title}</h1>
      {meta && <div className="mt-2 flex flex-wrap items-center gap-2 text-sm">{meta}</div>}
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
 * A searchable, sortable table.
 *
 * Rows are windowed with a "show more" step rather than paginated, so a world with
 * thousands of settlements stays responsive without the user losing their place.
 * Full virtualisation is the next step if these lists grow another order of
 * magnitude.
 */
export function DataTable<T>({
  rows,
  columns,
  searchText,
  placeholder = 'Search…',
  initialSort,
  emptyMessage = 'Nothing here.',
  pageSize = 100,
}: {
  rows: T[];
  columns: Column<T>[];
  searchText: (row: T) => string;
  placeholder?: string;
  initialSort?: { key: string; descending?: boolean };
  emptyMessage?: string;
  pageSize?: number;
}) {
  const [query, setQuery] = useState('');
  const [sortKey, setSortKey] = useState(initialSort?.key ?? columns[0]?.key);
  const [descending, setDescending] = useState(initialSort?.descending ?? false);
  const [limit, setLimit] = useState(pageSize);

  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase();
    const matched = needle
      ? rows.filter((row) => searchText(row).toLowerCase().includes(needle))
      : rows.slice();

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
  }, [rows, query, sortKey, descending, columns, searchText]);

  const visible = filtered.slice(0, limit);

  return (
    <div>
      <div className="mb-3 flex items-center gap-3">
        <input
          type="search"
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            setLimit(pageSize);
          }}
          placeholder={placeholder}
          className="w-full max-w-xs rounded border border-[var(--rule)] bg-[var(--page)] px-2.5 py-1.5 text-sm outline-none focus:border-[var(--accent)]"
        />
        <span className="text-xs tabular-nums text-[var(--ink-faint)]">
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
                  className={`py-2 pr-4 text-left text-[0.7rem] font-semibold tracking-wide uppercase text-[var(--ink-faint)] ${
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
                className="border-b border-[var(--rule)]/50 last:border-0 hover:bg-[var(--accent-soft)]/40"
              >
                {columns.map((column) => (
                  <td
                    key={column.key}
                    className={`py-1.5 pr-4 ${column.align === 'right' ? 'text-right tabular-nums' : ''} ${column.className ?? ''}`}
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
