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
 *   #/civ  #/set  #/fig  #/reg  #/cul     entity lists
 *   #/civ:3  #/fig:1204                   entity pages (the export's own ids)
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
  const raw = window.location.hash.replace(/^#/, '');
  return raw.length === 0 ? '/' : raw;
}

export function href(path: string): string {
  return `#${path.startsWith('/') ? path : `/${path}`}`;
}

export function navigate(path: string): void {
  window.location.hash = href(path);
}
