import { useEffect, useState } from 'react';
import { href } from '../router';
import type { World } from '../store';
import { kindOf, WORLD_KIND_LABELS, type EntityKind } from '../types';
import {
  IconBolt,
  IconChevronLeft,
  IconChevronRight,
  IconCity,
  IconCrown,
  IconDrop,
  IconFaith,
  IconFlag,
  IconGem,
  IconGlobe,
  IconHex,
  IconLandmark,
  IconMap,
  IconPeople,
  IconPerson,
  IconRoute,
  IconStar,
  IconSwords,
  IconTimeline,
} from './icons';

const NAV_KEY = 'he.worldNav.collapsed';

const KIND_NAV: Partial<Record<EntityKind, string>> = {
  civ: '/civ',
  set: '/set',
  rte: '/rte',
  dyn: '/dyn',
  fig: '/fig',
  rel: '/rel',
  hol: '/hol',
  art: '/art',
  war: '/war',
  bat: '/war',
  cul: '/cul',
  reg: '/reg',
};

const NAV_GROUPS = [
  {
    label: 'System',
    items: [{ path: '/cosmology', label: 'Cosmology', Icon: IconStar }],
  },
  {
    label: 'Chronicle',
    items: [
      { path: '/', label: 'Overview', Icon: IconGlobe },
      { path: '/map', label: 'Map', Icon: IconMap },
      { path: '/timeline', label: 'Timeline', Icon: IconTimeline },
    ],
  },
  {
    label: 'Peoples',
    items: [
      { path: '/civ', label: 'Civilizations', Icon: IconPeople },
      { path: '/dyn', label: 'Houses', Icon: IconCrown },
      { path: '/fig', label: 'Figures', Icon: IconPerson },
      { path: '/cul', label: 'Cultures', Icon: IconFlag },
    ],
  },
  {
    label: 'Places',
    items: [
      { path: '/set', label: 'Settlements', Icon: IconCity },
      { path: '/reg', label: 'Regions', Icon: IconHex },
      { path: '/rte', label: 'Trade', Icon: IconRoute },
      { path: '/hol', label: 'Holy sites', Icon: IconLandmark },
    ],
  },
  {
    label: 'Conflict',
    items: [
      { path: '/war', label: 'Wars', Icon: IconSwords },
      { path: '/plague', label: 'Plagues', Icon: IconDrop },
      { path: '/disaster', label: 'Disasters', Icon: IconBolt },
    ],
  },
  {
    label: 'Faith',
    items: [
      { path: '/rel', label: 'Faiths', Icon: IconFaith },
      { path: '/art', label: 'Artifacts', Icon: IconGem },
    ],
  },
];

function readCollapsed(): boolean {
  // A remembered desktop choice must not leave only a sliver for the reading column on a phone.
  if (window.matchMedia('(max-width: 639px)').matches) return true;

  try {
    const stored = localStorage.getItem(NAV_KEY);
    if (stored === '1') return true;
    if (stored === '0') return false;
  } catch {
    /* ignore */
  }
  return window.matchMedia('(max-width: 1023px)').matches;
}

function navIsActive(itemPath: string, activePath: string): boolean {
  if (itemPath === '/') return activePath === '/';
  if (activePath === itemPath || activePath.startsWith(`${itemPath}/`)) return true;

  const id = activePath.replace(/^\//, '');
  if (!id.includes(':')) return false;
  return KIND_NAV[kindOf(id)] === itemPath;
}

/**
 * Index of a loaded world: Overview, Map, Timeline, and the entity lists.
 *
 * Replaces the old horizontal tab strip so the map can use the full remaining
 * viewport. Collapsed it is an icon rail; expanded it names the world and the
 * indexes. The choice is remembered.
 */
export function WorldNav({ world, activePath }: { world: World; activePath: string }) {
  const [collapsed, setCollapsed] = useState(readCollapsed);
  const { meta } = world.export;
  const { designation, kind, name } = world.export.world;

  useEffect(() => {
    const narrow = window.matchMedia('(max-width: 639px)');
    const collapseForNarrowView = (event: MediaQueryListEvent) => {
      if (event.matches) setCollapsed(true);
    };
    narrow.addEventListener('change', collapseForNarrowView);
    return () => narrow.removeEventListener('change', collapseForNarrowView);
  }, []);

  const toggle = () => {
    setCollapsed((current) => {
      const next = !current;
      try {
        localStorage.setItem(NAV_KEY, next ? '1' : '0');
      } catch {
        /* ignore */
      }
      return next;
    });
  };

  return (
    <aside
      className="he-world-nav flex shrink-0 flex-col border-r border-[var(--rule)] bg-[var(--surface-container-low)]"
      data-collapsed={collapsed ? 'true' : 'false'}
    >
      <div
        className={`flex items-start gap-2 border-b border-[var(--rule)] ${
          collapsed ? 'justify-center px-1 py-3' : 'px-3 py-3'
        }`}
      >
        {!collapsed && (
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium">
              {name || designation || 'Untitled world'}
            </p>
            {kind && (
              <p className="he-label mt-0.5">{WORLD_KIND_LABELS[kind]}</p>
            )}
          </div>
        )}
        <button
          type="button"
          onClick={toggle}
          title={collapsed ? 'Expand index' : 'Collapse index'}
          aria-label={collapsed ? 'Expand index' : 'Collapse index'}
          aria-expanded={!collapsed}
          className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded text-[var(--ink-faint)] transition-colors hover:text-[var(--primary)]"
        >
          {collapsed ? <IconChevronRight /> : <IconChevronLeft />}
        </button>
      </div>

      <nav className="min-h-0 flex-1 overflow-y-auto py-2" aria-label="World index">
        {NAV_GROUPS.map((group) => (
          <div key={group.label} className={collapsed ? 'px-1' : 'px-2'}>
            {!collapsed && <div className="he-label px-2 pt-3 pb-1">{group.label}</div>}
            <ul className="space-y-0.5">
              {group.items.map((item) => {
                const active = navIsActive(item.path, activePath);
                return (
                  <li key={item.path}>
                    <a
                      href={href(item.path)}
                      title={item.label}
                      aria-current={active ? 'page' : undefined}
                      className={`flex items-center rounded-sm text-sm transition-colors ${
                        collapsed ? 'justify-center px-0 py-2' : 'gap-2.5 px-2 py-1.5'
                      } ${
                        active
                          ? 'bg-[var(--accent-soft)] font-medium text-[var(--primary)]'
                          : 'text-[var(--ink-soft)] hover:bg-[var(--hover)] hover:text-[var(--primary)]'
                      }`}
                    >
                      <item.Icon className="h-4 w-4 shrink-0" />
                      {!collapsed && <span className="truncate">{item.label}</span>}
                    </a>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </nav>

      {!collapsed && (
        <div className="he-data border-t border-[var(--rule)] px-3 py-2.5 text-[11px] text-[var(--ink-faint)]">
          seed {meta.seed}
          <br />
          years {meta.startYear}–{meta.endYear} · {meta.eventCount.toLocaleString()} events
          <br />
          schema v{world.export.schemaVersion} · {meta.engineVersion}
        </div>
      )}
    </aside>
  );
}
