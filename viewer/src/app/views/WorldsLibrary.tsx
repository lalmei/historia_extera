import { useEffect, useState } from 'react';
import { MIN_SCHEMA_VERSION } from '../compat';
import { generateHref, SiteShell } from '../components/SiteChrome';
import { SCHEMA_VERSION } from '../types';
import { WorldList } from './WorldList';

/**
 * Catalog of generated exports: pick one to read, rerun, or throw away.
 *
 * Shown as the viewer home under `astro dev` when no `?world=` is selected.
 * A built viewer has no catalog endpoint, so this page does not ship.
 */
export function WorldsLibrary() {
  const [query, setQuery] = useState('');

  useEffect(() => {
    const previous = document.title;
    document.title = 'Worlds Library — Historia Extera';
    return () => {
      document.title = previous;
    };
  }, []);

  return (
    <SiteShell active="worlds" search={{ value: query, onChange: setQuery }}>
      <div className="mb-8 flex flex-wrap items-start justify-between gap-4">
        <div className="max-w-2xl">
          <h1 className="he-headline">Worlds Library</h1>
          <p className="mt-2 text-[var(--ink-soft)]">
            Manage and explore generated historical simulations.
          </p>
          <p className="mt-3 text-sm text-[var(--ink-faint)]">
            This viewer reads exports from schema v{MIN_SCHEMA_VERSION} to v{SCHEMA_VERSION}. A
            world written by an earlier engine still opens, and is labelled below with what it
            was recorded before — those panels are empty rather than wrong. Anything older than
            v{MIN_SCHEMA_VERSION}, or written by a newer engine than this viewer, is marked
            unreadable and has to be run again.
          </p>
        </div>
        <a
          href={generateHref()}
          className="he-btn-primary px-4 py-2.5 text-sm font-semibold tracking-wide uppercase"
        >
          + Generate new world
        </a>
      </div>

      <WorldList query={query} />
    </SiteShell>
  );
}
