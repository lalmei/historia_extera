import { useEffect, useState } from 'react';
import { PageTitle } from './components/common';
import { CAN_GENERATE } from './generate';
import { hashParams, href, navigate, useRoute } from './router';
import { loadWorld, type World } from './store';
import {
  kindOf,
  type Artifact,
  type Battle,
  type Civilization,
  type Culture,
  type Dynasty,
  type Figure,
  type HolySite,
  type Region,
  type Religion,
  type Settlement,
  type TradeRoute,
  type War,
} from './types';
import {
  ArtifactPage,
  BattlePage,
  CivilizationPage,
  CulturePage,
  DynastyPage,
  FigurePage,
  HolySitePage,
  RegionPage,
  ReligionPage,
  SettlementPage,
  TradeRoutePage,
  WarPage,
} from './views/EntityPages';
import {
  ArtifactList,
  CivilizationList,
  CultureList,
  DisasterList,
  DynastyList,
  FigureList,
  HolySiteList,
  Overview,
  PlagueList,
  RegionList,
  ReligionList,
  SettlementList,
  TradeRouteList,
  Timeline,
  WarList,
} from './views/Lists';
import { NewWorld } from './views/NewWorld';
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
  { path: '/rte', label: 'Trade' },
  { path: '/dyn', label: 'Houses' },
  { path: '/fig', label: 'Figures' },
  { path: '/rel', label: 'Faiths' },
  { path: '/hol', label: 'Holy sites' },
  { path: '/art', label: 'Artifacts' },
  { path: '/plague', label: 'Plagues' },
  { path: '/disaster', label: 'Disasters' },
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

  // A freshly generated world replaces the one on screen without a reload: the app is
  // already holding a parsed chronicle, and reloading would drop it only to fetch the
  // same file again. The address bar is updated so a later reload comes back here.
  const adopt = (next: World, url: string) => {
    remember(url);
    setError(null);
    setWorld(next);
    navigate('/');
  };

  if (error) return <LoadFailure message={error} onLoaded={adopt} />;
  if (!world) return <Loading />;

  return (
    <div className="mx-auto max-w-5xl px-4 py-6 sm:px-6">
      <Header world={world} activePath={route.path} />
      <main className="mt-6">{renderRoute(world, route.path, adopt)}</main>
      <Footer world={world} />
    </div>
  );
}

/**
 * Points `?world=` at what is being shown.
 *
 * Before the `#`, which is the form that survives navigation and the one
 * `selectedWorldUrl` prefers — so a deep link copied out of a generated world reopens
 * that world rather than whatever `world.json` happens to hold.
 */
function remember(url: string) {
  const here = new URL(window.location.href);
  const params = new URLSearchParams(here.search);
  params.set('world', url);

  // Slashes are legal in a query string, and `?world=worlds/world-s7.json` is the form
  // the docs give and the one somebody can read back off the address bar.
  here.search = params.toString().replace(/%2F/g, '/');

  window.history.replaceState(null, '', here);
}

function renderRoute(world: World, path: string, onLoaded: (next: World, url: string) => void) {
  const target = path.replace(/^\//, '');

  switch (target) {
    case '':
      return <Overview world={world} />;
    case 'new':
      if (!CAN_GENERATE) break;
      return (
        <div>
          <PageTitle
            eyebrow="Generator"
            title="New world"
            meta={<span className="text-[var(--ink-faint)]">Development only</span>}
          />
          <NewWorld onLoaded={onLoaded} />
        </div>
      );
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
    case 'rte':
      return <TradeRouteList world={world} />;
    case 'dyn':
      return <DynastyList world={world} />;
    case 'fig':
      return <FigureList world={world} />;
    case 'rel':
      return <ReligionList world={world} />;
    case 'hol':
      return <HolySiteList world={world} />;
    case 'art':
      return <ArtifactList world={world} />;
    case 'plague':
      return <PlagueList world={world} />;
    case 'disaster':
      return <DisasterList world={world} />;
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
    case 'rte':
      return <TradeRoutePage world={world} route={entity as TradeRoute} />;
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
    case 'hol':
      return <HolySitePage world={world} site={entity as HolySite} />;
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
        <div className="flex items-center gap-3 text-xs tabular-nums text-[var(--ink-faint)]">
          <span>
            seed {meta.seed} · {meta.eventCount.toLocaleString()} events · years {meta.startYear}–
            {meta.endYear}
          </span>

          {/* Next to the seed rather than in the nav: the nav lists views of this world, and
              this makes a different one. Compiled out of a built viewer with the endpoint. */}
          {CAN_GENERATE && (
            <a
              href={href('/new')}
              title="Run another seed"
              className="rounded border border-[var(--rule)] px-2 py-0.5 transition-colors hover:border-[var(--accent)] hover:text-[var(--accent)]"
            >
              New world
            </a>
          )}
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

function LoadFailure({
  message,
  onLoaded,
}: {
  message: string;
  onLoaded: (next: World, url: string) => void;
}) {
  return (
    <div className="mx-auto max-w-2xl px-4 py-16">
      <h1 className="font-serif text-2xl">No world to show</h1>
      <pre className="mt-4 overflow-x-auto rounded border border-[var(--rule)] bg-[var(--panel)] p-4 text-sm whitespace-pre-wrap">
        {message}
      </pre>

      {/* The empty-viewer case is the one the generator most obviously answers: worlds are
          gitignored, so a fresh checkout lands here, and under the dev server it can now
          simulate its way out without a terminal. */}
      {CAN_GENERATE ? (
        <>
          <p className="mt-4 text-sm text-[var(--ink-soft)]">Simulate one now:</p>
          <div className="mt-2">
            <NewWorld onLoaded={onLoaded} />
          </div>
        </>
      ) : (
        <>
          <p className="mt-4 text-sm text-[var(--ink-soft)]">
            Generate one from the repository root, then reload:
          </p>
          <pre className="mt-2 overflow-x-auto rounded border border-[var(--rule)] bg-[var(--panel)] p-3 text-sm">
            dotnet run --project src/HistoryEngine.Cli -- --seed 42
          </pre>
        </>
      )}
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
