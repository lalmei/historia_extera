import { useEffect, useState } from 'react';

/**
 * Hash-based routing.
 *
 * Hash rather than History API on purpose: the viewer is a static bundle that
 * should work when opened straight off disk, with no server rewriting unknown
 * paths back to index.html. Cross-links stay instant because navigation never
 * reloads the document — and so never re-fetches and re-parses a world file that
 * can run to tens of megabytes.
 *
 * Routes:
 *   #/                  overview
 *   #/map               map
 *   #/timeline          timeline
 *   #/cosmology         host galaxy, star and system physics
 *   #/plague  #/disaster                   event-derived histories
 *   #/civ  #/set  #/rte  #/fig  #/reg     entity lists
 *   #/civ:3  #/rte:12  #/fig:1204         entity pages (the export's own ids)
 */
export interface Route {
  path: string;
  segments: string[];
}

export function useRoute(): Route {
  const [path, setPath] = useState(() => currentPath());

  useEffect(() => {
    const onChange = () => setPath(currentPath());
    window.addEventListener('hashchange', onChange);
    return () => window.removeEventListener('hashchange', onChange);
  }, []);

  return { path, segments: path.split('/').filter(Boolean) };
}

function currentPath(): string {
  const raw = hashRaw();
  const query = raw.indexOf('?');
  const path = query === -1 ? raw : raw.slice(0, query);
  return path.length === 0 ? '/' : path;
}

/**
 * Parameters carried inside the hash.
 *
 * The canonical place for a parameter is before the `#`, where it survives
 * navigation. But a deep link is copied as `…/#/civ:3`, and appending `?world=…`
 * to *that* puts the query in the hash instead — a natural enough mistake that
 * reading it there costs less than the confusion of ignoring it.
 */
export function hashParams(): URLSearchParams {
  const raw = hashRaw();
  const query = raw.indexOf('?');
  return new URLSearchParams(query === -1 ? '' : raw.slice(query + 1));
}

function hashRaw(): string {
  return window.location.hash.replace(/^#/, '');
}

export function href(path: string): string {
  return `#${path.startsWith('/') ? path : `/${path}`}`;
}

export function navigate(path: string): void {
  window.location.hash = href(path);
}
