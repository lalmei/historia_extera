import { useEffect, useRef, useState } from 'react';
import { CAN_GENERATE } from './generate';
import { hashParams, href, useRoute } from './router';
import { CosmologyPage } from './components/CosmologyPanel';
import { SiteFooter, SiteNav, generateHref, readingHref } from './components/SiteChrome';
import { WorldNav } from './components/WorldNav';
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
import { WorldMap } from './views/WorldMap';
import { WorldsLibrary } from './views/WorldsLibrary';

/** Where the CLI writes by default. */
const DEFAULT_WORLD = `${import.meta.env.BASE_URL}worlds/world.json`.replace('//', '/');

/**
 * A different export can be selected without rebuilding the static viewer.
 * Relative paths are resolved by fetch against the viewer page, so
 * `?world=worlds/custom.json` also works when the viewer has a base path.
 *
 * Accepted before the `#` (canonical — it survives navigation) or inside it,
 * which is where the parameter lands when appended to a copied deep link.
 *
 * Under the dev server, omitting `world` opens the Worlds Library instead of
 * assuming `worlds/world.json`. A built viewer still falls back to that default.
 */
function requestedWorldUrl(): string | null {
  const search = new URLSearchParams(window.location.search).get('world')?.trim();
  return search || hashParams().get('world')?.trim() || null;
}

function selectedWorldUrl(): string {
  return requestedWorldUrl() || DEFAULT_WORLD;
}

/** `?world=` value for the chronicle currently on screen, or the default export. */
function currentWorldQuery(): string {
  const requested = requestedWorldUrl();
  if (requested) return requested;
  return 'worlds/world.json';
}

export default function App() {
  const [world, setWorld] = useState<World | null>(null);
  const [error, setError] = useState<string | null>(null);
  const route = useRoute();
  const browseLibrary = CAN_GENERATE && requestedWorldUrl() === null;
  const mainRef = useRef<HTMLElement>(null);

  useEffect(() => {
    if (browseLibrary) return;

    // Loaded once for the lifetime of the app. Routing is client-side precisely so
    // that navigating between entities never re-fetches or re-parses a file that
    // can run to tens of megabytes.
    loadWorld(selectedWorldUrl())
      .then(setWorld)
      .catch((cause: unknown) => setError(cause instanceof Error ? cause.message : String(cause)));
  }, [browseLibrary]);

  useEffect(() => {
    if (!world) return;

    const designation = world.export.world.designation;
    const previous = document.title;
    document.title = designation
      ? `${designation} — Historia Extera`
      : 'Historia Extera — Legends';
    return () => {
      document.title = previous;
    };
  }, [world]);

  // Scroll the reading column, not the window — the world shell is a fixed viewport.
  useEffect(() => {
    mainRef.current?.scrollTo({ top: 0 });
  }, [route.path]);

  if (browseLibrary) return <WorldsLibrary />;
  if (error) return <LoadFailure message={error} />;
  if (!world) return <Loading />;

  const isMap = route.path === '/map';
  const isEntity = route.path.includes(':');
  const innerWidth = isMap || route.path === '/timeline'
    ? 'w-full'
    : isEntity
      ? 'mx-auto w-full max-w-[720px]'
      : 'mx-auto w-full max-w-6xl';

  return (
    <div className="flex h-screen flex-col overflow-hidden">
      <SiteNav fluid active="reading" readingHref={readingHref(currentWorldQuery())} />
      <div className="flex min-h-0 flex-1">
        <WorldNav world={world} activePath={route.path} />
        <main
          ref={mainRef}
          className={`min-h-0 min-w-0 flex-1 ${
            isMap ? 'flex min-h-0 overflow-hidden' : 'overflow-y-auto px-4 py-6 md:px-8'
          }`}
        >
          {isMap ? (
            renderRoute(world, route.path)
          ) : (
            <div className={innerWidth}>{renderRoute(world, route.path)}</div>
          )}
        </main>
      </div>
      <SiteFooter fluid />
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
    case 'cosmology':
      return <CosmologyPage world={world} />;
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

function Loading() {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteNav active="reading" readingHref={readingHref(currentWorldQuery())} />
      <div className="flex flex-1 items-center justify-center px-4">
        <p className="text-sm text-[var(--ink-faint)]">Reading the chronicle…</p>
      </div>
    </div>
  );
}

function LoadFailure({ message }: { message: string }) {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteNav active="reading" readingHref={readingHref(currentWorldQuery())} />
      <div className="mx-auto w-full max-w-2xl flex-1 px-4 py-16">
        <h1 className="he-headline-md">No world to show</h1>
        <pre className="he-data mt-4 overflow-x-auto rounded-lg border border-[var(--rule)] bg-[var(--panel)] p-4 whitespace-pre-wrap">
          {message}
        </pre>

        {CAN_GENERATE && (
          <p className="mt-4 text-sm">
            <a href={import.meta.env.BASE_URL} className="text-[var(--accent)] underline">
              Worlds library
            </a>
            {' · '}
            <a href={generateHref()} className="text-[var(--accent)] underline">
              Simulate one now
            </a>
          </p>
        )}

        <p className="mt-4 text-sm text-[var(--ink-soft)]">
          Or generate one from the repository root, then reload:
        </p>
        <pre className="mt-2 overflow-x-auto rounded border border-[var(--rule)] bg-[var(--panel)] p-3 text-sm">
          dotnet run --project src/HistoryEngine.Cli -- --seed 42
        </pre>
      </div>
      <SiteFooter />
    </div>
  );
}

function NotFound({ id }: { id: string }) {
  return (
    <div className="py-12 text-center">
      <h1 className="he-headline-md">Nothing here</h1>
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
