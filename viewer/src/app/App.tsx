import { useEffect, useState } from 'react';
import { hashParams, href, useRoute } from './router';
import { loadWorld, type World } from './store';
import {
  kindOf,
  type Artifact,
  type Battle,
  type Civilization,
  type Culture,
  type Dynasty,
  type Figure,
  type Region,
  type Religion,
  type Settlement,
  type War,
} from './types';
import {
  ArtifactPage,
  BattlePage,
  CivilizationPage,
  CulturePage,
  DynastyPage,
  FigurePage,
  RegionPage,
  ReligionPage,
  SettlementPage,
  WarPage,
} from './views/EntityPages';
import {
  ArtifactList,
  CivilizationList,
  CultureList,
  DynastyList,
  FigureList,
  Overview,
  RegionList,
  ReligionList,
  SettlementList,
  Timeline,
  WarList,
} from './views/Lists';
import { WorldMap } from './views/WorldMap';

/** Where the CLI writes by default. */
const DEFAULT_WORLD = `${import.meta.env.BASE_URL}worlds/world.json`.replace('//', '/');

/**
 * A different export can be selected without rebuilding the static viewer.
 * Relative paths are resolved by fetch against the viewer page, so
 * `?world=worlds/custom.json` also works when the viewer has a base path.
 *
 * Accepted before the `#` (canonical — it survives navigation) or inside it,
 * which is where the parameter lands when appended to a copied deep link.
 */
function selectedWorldUrl(): string {
  const search = new URLSearchParams(window.location.search).get('world')?.trim();
  const requested = search || hashParams().get('world')?.trim();
  return requested || DEFAULT_WORLD;
}

const NAV = [
  { path: '/', label: 'Overview' },
  { path: '/map', label: 'Map' },
  { path: '/timeline', label: 'Timeline' },
  { path: '/civ', label: 'Civilizations' },
  { path: '/war', label: 'Wars' },
  { path: '/set', label: 'Settlements' },
  { path: '/dyn', label: 'Houses' },
  { path: '/fig', label: 'Figures' },
  { path: '/rel', label: 'Faiths' },
  { path: '/art', label: 'Artifacts' },
  { path: '/cul', label: 'Cultures' },
  { path: '/reg', label: 'Regions' },
];

export default function App() {
  const [world, setWorld] = useState<World | null>(null);
  const [error, setError] = useState<string | null>(null);
  const route = useRoute();

  useEffect(() => {
    // Loaded once for the lifetime of the app. Routing is client-side precisely so
    // that navigating between entities never re-fetches or re-parses a file that
    // can run to tens of megabytes.
    loadWorld(selectedWorldUrl())
      .then(setWorld)
      .catch((cause: unknown) => setError(cause instanceof Error ? cause.message : String(cause)));
  }, []);

  // Scroll to the top on navigation — a deep chronicle otherwise leaves the next
  // page opening halfway down.
  useEffect(() => {
    window.scrollTo({ top: 0 });
  }, [route.path]);

  if (error) return <LoadFailure message={error} />;
  if (!world) return <Loading />;

  return (
    <div className="mx-auto max-w-5xl px-4 py-6 sm:px-6">
      <Header world={world} activePath={route.path} />
      <main className="mt-6">{renderRoute(world, route.path)}</main>
      <Footer world={world} />
    </div>
  );
}

function renderRoute(world: World, path: string) {
  const target = path.replace(/^\//, '');

  switch (target) {
    case '':
      return <Overview world={world} />;
    case 'map':
      return <WorldMap world={world} />;
    case 'timeline':
      return <Timeline world={world} />;
    case 'civ':
      return <CivilizationList world={world} />;
    case 'war':
      return <WarList world={world} />;
    case 'set':
      return <SettlementList world={world} />;
    case 'dyn':
      return <DynastyList world={world} />;
    case 'fig':
      return <FigureList world={world} />;
    case 'rel':
      return <ReligionList world={world} />;
    case 'art':
      return <ArtifactList world={world} />;
    case 'cul':
      return <CultureList world={world} />;
    case 'reg':
      return <RegionList world={world} />;
    default:
      break;
  }

  // Anything else is an entity id, which is also its route — the readable id format
  // means no separate slug scheme is needed.
  const entity = world.byId.get(target);
  if (!entity) return <NotFound id={target} />;

  switch (kindOf(target)) {
    case 'civ':
      return <CivilizationPage world={world} civ={entity as Civilization} />;
    case 'set':
      return <SettlementPage world={world} settlement={entity as Settlement} />;
    case 'dyn':
      return <DynastyPage world={world} house={entity as Dynasty} />;
    case 'fig':
      return <FigurePage world={world} figure={entity as Figure} />;
    case 'reg':
      return <RegionPage world={world} region={entity as Region} />;
    case 'cul':
      return <CulturePage world={world} culture={entity as Culture} />;
    case 'war':
      return <WarPage world={world} war={entity as War} />;
    case 'bat':
      return <BattlePage world={world} battle={entity as Battle} />;
    case 'rel':
      return <ReligionPage world={world} religion={entity as Religion} />;
    case 'art':
      return <ArtifactPage world={world} artifact={entity as Artifact} />;
    default:
      return <NotFound id={target} />;
  }
}

function Header({ world, activePath }: { world: World; activePath: string }) {
  const { meta } = world.export;

  return (
    <header>
      <div className="flex flex-wrap items-baseline justify-between gap-3 border-b border-[var(--rule)] pb-3">
        <a href={href('/')} className="font-serif text-xl tracking-tight">
          Historia Extera
          <span className="ml-2 text-sm font-normal text-[var(--ink-faint)]">Legends</span>
        </a>
        <div className="text-xs tabular-nums text-[var(--ink-faint)]">
          seed {meta.seed} · {meta.eventCount.toLocaleString()} events · years {meta.startYear}–
          {meta.endYear}
        </div>
      </div>

      <nav className="-mx-4 mt-3 overflow-x-auto px-4">
        <ul className="flex gap-1 text-sm whitespace-nowrap">
          {NAV.map((item) => {
            const active =
              item.path === '/' ? activePath === '/' : activePath.startsWith(item.path);

            return (
              <li key={item.path}>
                <a
                  href={href(item.path)}
                  className={`inline-block rounded px-2.5 py-1 transition-colors ${
                    active
                      ? 'bg-[var(--accent-soft)] font-medium text-[var(--accent)]'
                      : 'text-[var(--ink-soft)] hover:text-[var(--accent)]'
                  }`}
                >
                  {item.label}
                </a>
              </li>
            );
          })}
        </ul>
      </nav>
    </header>
  );
}

function Footer({ world }: { world: World }) {
  const { meta } = world.export;

  return (
    <footer className="mt-10 border-t border-[var(--rule)] pt-4 text-xs text-[var(--ink-faint)]">
      <p>
        Schema v{world.export.schemaVersion} · engine {meta.engineVersion} · config{' '}
        {meta.configHash} · systems {meta.systemOrder.join(' → ')}
      </p>
      <p className="mt-1">
        Names come from per-culture Markov chains over public-domain corpora, blended and
        sound-shifted per culture. No generated name appears in its training data.
      </p>
    </footer>
  );
}

function Loading() {
  return (
    <div className="mx-auto flex min-h-[60vh] max-w-5xl items-center justify-center px-4">
      <p className="text-sm text-[var(--ink-faint)]">Reading the chronicle…</p>
    </div>
  );
}

function LoadFailure({ message }: { message: string }) {
  return (
    <div className="mx-auto max-w-2xl px-4 py-16">
      <h1 className="font-serif text-2xl">No world to show</h1>
      <pre className="mt-4 overflow-x-auto rounded border border-[var(--rule)] bg-[var(--panel)] p-4 text-sm whitespace-pre-wrap">
        {message}
      </pre>
      <p className="mt-4 text-sm text-[var(--ink-soft)]">
        Generate one from the repository root, then reload:
      </p>
      <pre className="mt-2 overflow-x-auto rounded border border-[var(--rule)] bg-[var(--panel)] p-3 text-sm">
        dotnet run --project src/HistoryEngine.Cli -- --seed 42
      </pre>
    </div>
  );
}

function NotFound({ id }: { id: string }) {
  return (
    <div className="py-12 text-center">
      <h1 className="font-serif text-2xl">Nothing here</h1>
      <p className="mt-2 text-sm text-[var(--ink-soft)]">
        <code className="rounded bg-[var(--panel)] px-1.5 py-0.5">{id}</code> is not in this world
        file.
      </p>
      <a href={href('/')} className="mt-4 inline-block text-sm text-[var(--accent)] underline">
        Back to the overview
      </a>
    </div>
  );
}
