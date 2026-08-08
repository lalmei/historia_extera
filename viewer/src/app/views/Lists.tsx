import { useMemo, useState } from 'react';
import { EventList, humanise } from '../components/EventList';
import {
  Badge,
  type Column,
  DataTable,
  EntityLink,
  PageTitle,
  Panel,
  Stat,
  yearRange,
} from '../components/common';
import type { World } from '../store';
import {
  DEATH_LABELS,
  SUCCESSION_LABELS,
  type Civilization,
  type Culture,
  type Dynasty,
  type Figure,
  type Region,
} from '../types';
import { SettlementTable } from './EntityPages';

export function CivilizationList({ world }: { world: World }) {
  const columns: Column<Civilization>[] = [
    {
      key: 'name',
      header: 'Civilization',
      cell: (civ) => <EntityLink world={world} id={civ.id} />,
      sort: (civ) => civ.name,
    },
    {
      key: 'culture',
      header: 'Culture',
      cell: (civ) => <EntityLink world={world} id={civ.cultureId} />,
      sort: (civ) => world.nameOf(civ.cultureId),
    },
    {
      key: 'capital',
      header: 'Capital',
      cell: (civ) => <EntityLink world={world} id={civ.capitalId} />,
      sort: (civ) => (civ.capitalId ? world.nameOf(civ.capitalId) : ''),
    },
    {
      key: 'population',
      header: 'Population',
      cell: (civ) => civ.population.toLocaleString(),
      sort: (civ) => civ.population,
      align: 'right',
    },
    {
      key: 'settlements',
      header: 'Settlements',
      cell: (civ) => civ.settlementIds.length,
      sort: (civ) => civ.settlementIds.length,
      align: 'right',
    },
    {
      key: 'span',
      header: 'Span',
      cell: (civ) => (
        <span className={civ.endedYear !== undefined ? 'text-[var(--ink-faint)]' : ''}>
          {yearRange(civ.foundedYear, civ.endedYear)}
        </span>
      ),
      sort: (civ) => civ.foundedYear,
    },
  ];

  return (
    <div>
      <PageTitle eyebrow="Index" title="Civilizations" />
      <Panel>
        <DataTable
          rows={world.export.civilizations}
          columns={columns}
          searchText={(civ) => `${civ.name} ${world.nameOf(civ.cultureId)}`}
          placeholder="Search civilizations…"
          initialSort={{ key: 'population', descending: true }}
        />
      </Panel>
    </div>
  );
}

export function SettlementList({ world }: { world: World }) {
  return (
    <div>
      <PageTitle eyebrow="Index" title="Settlements" />
      <Panel>
        <SettlementTable world={world} settlements={world.export.settlements} />
      </Panel>
    </div>
  );
}

export function DynastyList({ world }: { world: World }) {
  const columns: Column<Dynasty>[] = [
    {
      key: 'name',
      header: 'House',
      cell: (house) => <EntityLink world={world} id={house.id} />,
      sort: (house) => house.name,
    },
    {
      key: 'origin',
      header: 'Rose in',
      cell: (house) => <EntityLink world={world} id={house.originCivilizationId} />,
      sort: (house) => (house.originCivilizationId ? world.nameOf(house.originCivilizationId) : ''),
    },
    {
      key: 'rulers',
      header: 'Rulers',
      cell: (house) => house.rulerIds.length,
      sort: (house) => house.rulerIds.length,
      align: 'right',
    },
    {
      key: 'members',
      header: 'Blood',
      cell: (house) => house.memberIds.length,
      sort: (house) => house.memberIds.length,
      align: 'right',
    },
    {
      key: 'span',
      header: 'Span',
      cell: (house) => (
        <span className={house.endedYear !== undefined ? 'text-[var(--ink-faint)]' : ''}>
          {yearRange(house.foundedYear, house.endedYear)}
        </span>
      ),
      sort: (house) => house.foundedYear,
    },
    {
      key: 'fate',
      header: 'Fate',
      cell: (house) =>
        house.endedYear === undefined ? (
          <Badge tone="accent">extant</Badge>
        ) : (
          <Badge tone="muted">died out {house.endedYear}</Badge>
        ),
      sort: (house) => house.endedYear ?? Number.MAX_SAFE_INTEGER,
    },
  ];

  return (
    <div>
      <PageTitle eyebrow="Index" title="Houses" />
      <Panel>
        <DataTable
          rows={world.export.dynasties}
          columns={columns}
          searchText={(house) => house.name}
          placeholder="Search houses…"
          initialSort={{ key: 'rulers', descending: true }}
        />
      </Panel>
    </div>
  );
}

export function FigureList({ world }: { world: World }) {
  const columns: Column<Figure>[] = [
    {
      key: 'name',
      header: 'Figure',
      cell: (figure) => <EntityLink world={world} id={figure.id} />,
      sort: (figure) => figure.name,
    },
    {
      key: 'title',
      header: 'Title',
      cell: (figure) => figure.titles[0]?.title ?? '—',
      sort: (figure) => figure.titles[0]?.title ?? '',
    },
    {
      key: 'house',
      header: 'House',
      cell: (figure) =>
        figure.dynastyId ? (
          <EntityLink world={world} id={figure.dynastyId} />
        ) : (
          <span className="text-[var(--ink-faint)]">—</span>
        ),
      sort: (figure) => (figure.dynastyId ? world.nameOf(figure.dynastyId) : ''),
    },
    {
      key: 'civ',
      header: 'Civilization',
      cell: (figure) => <EntityLink world={world} id={figure.civilizationId} />,
      sort: (figure) => world.nameOf(figure.civilizationId),
    },
    {
      key: 'lived',
      header: 'Lived',
      cell: (figure) => yearRange(figure.birthYear, figure.deathYear),
      sort: (figure) => figure.birthYear,
    },
    {
      key: 'age',
      header: 'Age',
      cell: (figure) =>
        (figure.deathYear ?? world.export.meta.endYear) - figure.birthYear,
      sort: (figure) => (figure.deathYear ?? world.export.meta.endYear) - figure.birthYear,
      align: 'right',
    },
    {
      key: 'fate',
      header: 'Fate',
      cell: (figure) =>
        figure.deathYear === undefined ? (
          <Badge tone="accent">living</Badge>
        ) : (
          <span className="text-[var(--ink-faint)]">
            {DEATH_LABELS[figure.deathCause] ?? figure.deathCause}
          </span>
        ),
      sort: (figure) => figure.deathCause,
    },
  ];

  return (
    <div>
      <PageTitle eyebrow="Index" title="Figures" />
      <Panel>
        <DataTable
          rows={world.export.figures}
          columns={columns}
          searchText={(figure) =>
            `${figure.name} ${figure.titles[0]?.title ?? ''} ` +
            `${figure.dynastyId ? world.nameOf(figure.dynastyId) : ''} ` +
            `${world.nameOf(figure.civilizationId)}`
          }
          placeholder="Search figures…"
          initialSort={{ key: 'lived' }}
        />
      </Panel>
    </div>
  );
}

export function RegionList({ world }: { world: World }) {
  const [landOnly, setLandOnly] = useState(true);

  const rows = useMemo(
    () => (landOnly ? world.export.regions.filter((r) => r.isLand) : world.export.regions),
    [world.export.regions, landOnly],
  );

  const columns: Column<Region>[] = [
    {
      key: 'name',
      header: 'Region',
      cell: (region) => <EntityLink world={world} id={region.id} />,
      sort: (region) => region.name,
    },
    { key: 'biome', header: 'Biome', cell: (region) => region.biome, sort: (region) => region.biome },
    {
      key: 'habitability',
      header: 'Habitability',
      cell: (region) => region.habitability.toFixed(2),
      sort: (region) => region.habitability,
      align: 'right',
    },
    {
      key: 'height',
      header: 'Height',
      cell: (region) => `${Math.round(region.meanHeight)} m`,
      sort: (region) => region.meanHeight,
      align: 'right',
    },
    {
      key: 'features',
      header: 'Features',
      cell: (region) => (
        <span className="flex gap-1">
          {region.hasRiver && <Badge>river</Badge>}
          {region.isCoastal && <Badge>coast</Badge>}
        </span>
      ),
    },
    {
      key: 'owner',
      header: 'Claimed by',
      cell: (region) => <EntityLink world={world} id={region.owner} />,
      sort: (region) => (region.owner ? world.nameOf(region.owner) : '~'),
    },
  ];

  return (
    <div>
      <PageTitle eyebrow="Index" title="Regions" />
      <Panel
        actions={
          <label className="inline-flex cursor-pointer items-center gap-1.5 text-xs select-none">
            <input
              type="checkbox"
              checked={landOnly}
              onChange={(event) => setLandOnly(event.target.checked)}
              className="accent-[var(--accent)]"
            />
            Land only
          </label>
        }
      >
        <DataTable
          rows={rows}
          columns={columns}
          searchText={(region) => `${region.name} ${region.biome}`}
          placeholder="Search regions…"
          initialSort={{ key: 'habitability', descending: true }}
        />
      </Panel>
    </div>
  );
}

export function CultureList({ world }: { world: World }) {
  const columns: Column<Culture>[] = [
    {
      key: 'name',
      header: 'Culture',
      cell: (culture) => <EntityLink world={world} id={culture.id} />,
      sort: (culture) => culture.name,
    },
    {
      key: 'government',
      header: 'Government',
      cell: (culture) => culture.government,
      sort: (culture) => culture.government,
    },
    { key: 'title', header: 'Ruler styled', cell: (culture) => culture.rulerTitle },
    {
      key: 'succession',
      header: 'Succession',
      cell: (culture) => (
        <span>
          {SUCCESSION_LABELS[culture.successionLaw] ?? culture.successionLaw}
          {culture.termYears > 0 && (
            <span className="text-[var(--ink-faint)]"> · {culture.termYears}y terms</span>
          )}
        </span>
      ),
      sort: (culture) => culture.successionLaw,
    },
    {
      key: 'aggression',
      header: 'Aggression',
      cell: (culture) => culture.aggression.toFixed(2),
      sort: (culture) => culture.aggression,
      align: 'right',
    },
    {
      key: 'expansionism',
      header: 'Expansionism',
      cell: (culture) => culture.expansionism.toFixed(2),
      sort: (culture) => culture.expansionism,
      align: 'right',
    },
  ];

  return (
    <div>
      <PageTitle eyebrow="Index" title="Cultures" />
      <Panel>
        <DataTable
          rows={world.export.cultures}
          columns={columns}
          searchText={(culture) => `${culture.name} ${culture.government}`}
          placeholder="Search cultures…"
        />
      </Panel>
    </div>
  );
}

/**
 * The whole chronicle, filterable by era and civilization.
 *
 * Era filtering is on the year index rather than a scan, and the civilization
 * filter reuses the same per-entity index the entity pages use.
 */
export function Timeline({ world }: { world: World }) {
  const { meta, events } = world.export;
  const [civ, setCiv] = useState<string>('all');
  const [from, setFrom] = useState(meta.startYear);
  const [to, setTo] = useState(meta.endYear);

  const filtered = useMemo(() => {
    const inRange = (year: number) => year >= from && year <= to;

    if (civ === 'all') return events.filter((event) => inRange(event.year));

    // Reuse the engine's index instead of scanning: the civilization's own events
    // plus those of everything it owns would otherwise be a full pass per filter.
    const relevant = new Set<number>();
    const collect = (id: string) => {
      for (const index of world.export.indices.eventsByEntity[id] ?? []) relevant.add(index);
    };

    collect(civ);
    const civilization = world.export.civilizations.find((c) => c.id === civ);
    civilization?.settlementIds.forEach(collect);
    civilization?.rulerIds.forEach(collect);

    return [...relevant]
      .sort((a, b) => a - b)
      .map((index) => events[index])
      .filter((event) => inRange(event.year));
  }, [civ, from, to, events, world.export]);

  return (
    <div>
      <PageTitle
        eyebrow="Chronicle"
        title="Timeline"
        meta={
          <Badge>
            {filtered.length.toLocaleString()} of {events.length.toLocaleString()} events
          </Badge>
        }
      />

      <Panel
        actions={
          <div className="flex flex-wrap items-center gap-3 text-xs">
            <select
              value={civ}
              onChange={(event) => setCiv(event.target.value)}
              className="rounded border border-[var(--rule)] bg-[var(--page)] px-1.5 py-1 text-xs"
            >
              <option value="all">All civilizations</option>
              {world.export.civilizations.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
            <label className="inline-flex items-center gap-1.5">
              Years
              <input
                type="number"
                value={from}
                min={meta.startYear}
                max={meta.endYear}
                onChange={(event) => setFrom(Number(event.target.value))}
                className="w-20 rounded border border-[var(--rule)] bg-[var(--page)] px-1.5 py-1 tabular-nums"
              />
              <span className="text-[var(--ink-faint)]">to</span>
              <input
                type="number"
                value={to}
                min={meta.startYear}
                max={meta.endYear}
                onChange={(event) => setTo(Number(event.target.value))}
                className="w-20 rounded border border-[var(--rule)] bg-[var(--page)] px-1.5 py-1 tabular-nums"
              />
            </label>
          </div>
        }
      >
        <EventList world={world} events={filtered} pageSize={200} />
      </Panel>
    </div>
  );
}

export function Overview({ world }: { world: World }) {
  const { meta, civilizations, dynasties, settlements, figures, events, indices } = world.export;

  const standing = civilizations.filter((civ) => civ.endedYear === undefined);
  const inhabited = settlements.filter((s) => s.abandonedYear === undefined);
  const cities = inhabited.filter((s) => s.tier === 'City');
  const living = figures.filter((f) => f.deathYear === undefined);
  const extant = dynasties.filter((house) => house.endedYear === undefined);

  const largest = [...inhabited].sort((a, b) => b.population - a.population).slice(0, 5);
  const kindCounts = Object.entries(indices.eventCountsByKind).sort((a, b) => b[1] - a[1]);

  // Longest-lived houses by reigns held — the closest thing a world has to a protagonist.
  const greatest = [...dynasties].sort((a, b) => b.rulerIds.length - a.rulerIds.length).slice(0, 5);

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={`Seed ${meta.seed} · config ${meta.configHash}`}
        title="A world, in brief"
        meta={
          <>
            <Badge>
              years {meta.startYear}–{meta.endYear}
            </Badge>
            <Badge>engine {meta.engineVersion}</Badge>
          </>
        }
      />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
        <Stat label="Events" value={events.length.toLocaleString()} />
        <Stat label="Civilizations" value={`${standing.length} / ${civilizations.length}`} hint="Standing of all founded" />
        <Stat label="Settlements" value={inhabited.length} />
        <Stat label="Cities" value={cities.length} />
        <Stat label="Figures" value={figures.length} hint={`${living.length} still living`} />
        <Stat
          label="Houses"
          value={`${extant.length} / ${dynasties.length}`}
          hint="Extant of all that ever rose"
        />
      </div>

      <div className="grid gap-5 lg:grid-cols-2">
        <Panel title="Great houses">
          <ol className="space-y-1.5 text-sm">
            {greatest.map((house) => (
              <li key={house.id} className="flex items-baseline justify-between gap-3">
                <span>
                  <EntityLink world={world} id={house.id} />
                  <span className="ml-2 text-[var(--ink-faint)]">
                    {yearRange(house.foundedYear, house.endedYear)}
                  </span>
                </span>
                <span className="shrink-0 tabular-nums text-[var(--ink-faint)]">
                  {house.rulerIds.length} {house.rulerIds.length === 1 ? 'reign' : 'reigns'}
                </span>
              </li>
            ))}
          </ol>
        </Panel>

        <Panel title="Largest settlements">
          <ol className="space-y-1.5 text-sm">
            {largest.map((settlement) => (
              <li key={settlement.id} className="flex items-baseline justify-between gap-3">
                <span>
                  <EntityLink world={world} id={settlement.id} />
                  <span className="ml-2 text-[var(--ink-faint)]">
                    {settlement.tier} · <EntityLink world={world} id={settlement.civilizationId} />
                  </span>
                </span>
                <span className="shrink-0 tabular-nums">
                  {settlement.population.toLocaleString()}
                </span>
              </li>
            ))}
          </ol>
        </Panel>

      </div>

      <Panel title="What happened">
        {/* Two columns: dynasties roughly tripled the number of distinct event kinds. */}
        <ul className="grid gap-x-8 gap-y-1 text-sm sm:grid-cols-2">
          {kindCounts.map(([kind, count]) => (
            <li key={kind} className="flex items-baseline justify-between gap-3">
              <span>{humanise(kind)}</span>
              <span className="tabular-nums text-[var(--ink-faint)]">
                {count.toLocaleString()}
              </span>
            </li>
          ))}
        </ul>
      </Panel>

      <Panel title="Terrain sampling">
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
          <Stat
            label="Simulation"
            value={meta.terrainSampling.simulationSamples.toLocaleString()}
            hint="Samples spent on worldgen and simulation"
          />
          <Stat
            label="In-game cost"
            value={`≈${meta.terrainSampling.estimatedGameSecondsSimulation.toFixed(1)}s`}
            hint="What that many samples would cost against Vintage Story's terrain sampler"
          />
          <Stat
            label="Map raster"
            value={meta.terrainSampling.rasterSamples.toLocaleString()}
            hint="Presentation only — budgeted separately"
          />
          <Stat label="Capabilities" value={<span className="text-xs">{world.export.world.capabilities}</span>} />
        </div>
        <p className="mt-3 text-xs leading-relaxed text-[var(--ink-faint)]">
          Rivers are derived from the height lattice rather than sampled, so they cost nothing and
          exist in every phase — Vintage Story&rsquo;s sampler cannot report them without the
          Watersheds sampler installed.
        </p>
      </Panel>

      <Panel title="Recent history">
        <EventList
          world={world}
          events={events.slice(-40).reverse()}
          showFilters={false}
          pageSize={40}
        />
      </Panel>
    </div>
  );
}
