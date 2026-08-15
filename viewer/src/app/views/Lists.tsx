import { useMemo, useState } from 'react';
import { EventList, humanise } from '../components/EventList';
import {
  Badge,
  type Column,
  DataTable,
  EntityLink,
  type Facet,
  Field,
  PageTitle,
  Panel,
  Stat,
  yearRange,
} from '../components/common';
import { cultureOf, figures, type World } from '../store';
import {
  AUTHORITY_LABELS,
  DEATH_LABELS,
  DEITY_LABELS,
  SUCCESSION_LABELS,
  type Civilization,
  type Culture,
  type Dynasty,
  type EntityId,
  type Figure,
  type HolySite,
  type HistoryEvent,
  type Region,
  type Religion,
} from '../types';
import {
  ArtifactTable,
  BattleTable,
  HolySiteTable,
  SettlementTable,
  TradeRouteTable,
  WarTable,
  warsOf,
} from './EntityPages';

export function TradeRouteList({ world }: { world: World }) {
  const { tradeRoutes } = world.export;
  const active = tradeRoutes.filter((route) => route.endedYear === undefined);
  const prosperous = active.filter((route) => route.status === 'Prosperous');
  const overland = active.filter((route) => route.mode === 'Overland');

  return (
    <div className="space-y-5">
      <PageTitle eyebrow="Index" title="Trade routes" />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Active" value={active.length} />
        <Stat label="Recorded" value={tradeRoutes.length} />
        <Stat label="Prosperous" value={prosperous.length} />
        <Stat label="Overland" value={overland.length} hint="Candidates for future roads" />
      </div>

      <Panel title="Routes">
        <TradeRouteTable world={world} routes={tradeRoutes} />
      </Panel>
    </div>
  );
}

/**
 * Every war ever fought, and every engagement in them.
 *
 * Two tables rather than one page per war, because the interesting question at this level
 * is comparative — which wars actually moved a border, and which were the bloodiest — and
 * that is a thing you sort a column by rather than click through twenty pages for.
 */
export function WarList({ world }: { world: World }) {
  const { wars, battles } = world.export;
  const settled = wars.filter((war) => war.endYear !== undefined);
  const decided = settled.filter((war) => war.outcome !== 'Stalemate');
  const dead = battles.reduce((sum, b) => sum + b.attackerLosses + b.defenderLosses, 0);

  return (
    <div className="space-y-5">
      <PageTitle eyebrow="Index" title="Wars" />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Wars" value={wars.length} />
        <Stat
          label="Decided"
          value={`${decided.length} / ${settled.length}`}
          hint="The rest were fought to exhaustion"
        />
        <Stat label="Battles" value={battles.length} />
        <Stat label="Dead" value={dead.toLocaleString()} hint="In battle, across every war" />
      </div>

      <Panel title="Wars">
        <WarTable world={world} wars={wars} />
      </Panel>

      <Panel title="Battles">
        <BattleTable world={world} battles={battles} />
      </Panel>
    </div>
  );
}

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

  const governmentOf = (civ: Civilization) => cultureOf(world, civ.cultureId)?.government;

  const facets: Facet<Civilization>[] = [
    {
      key: 'status',
      label: 'Status',
      options: [
        { value: 'standing', label: 'Standing', match: (civ) => civ.endedYear === undefined },
        { value: 'fallen', label: 'Fallen', match: (civ) => civ.endedYear !== undefined },
        { value: 'war', label: 'Fought a war', match: (civ) => warsOf(world, civ.id).length > 0 },
      ],
    },
    {
      key: 'government',
      label: 'Government',
      options: distinct(world.export.cultures.map((culture) => culture.government)).map(
        (government) => ({
          value: government,
          label: government,
          match: (civ: Civilization) => governmentOf(civ) === government,
        }),
      ),
    },
  ];

  return (
    <div>
      <PageTitle eyebrow="Index" title="Civilizations" />
      <Panel>
        <DataTable
          rows={world.export.civilizations}
          columns={columns}
          facets={facets}
          searchText={(civ) => `${civ.name} ${world.nameOf(civ.cultureId)}`}
          placeholder="Search civilizations…"
          initialSort={{ key: 'population', descending: true }}
        />
      </Panel>
    </div>
  );
}

/** The distinct values present, in first-seen order. */
function distinct<T>(values: T[]): T[] {
  return [...new Set(values)];
}

/**
 * One completed or still-burning outbreak, reconstructed from the event contract.
 *
 * Plagues deliberately stop being engine entities when they burn out. Their name and origin are
 * repeated on the begin/end pair instead, so this view keeps that boundary and does not invent
 * viewer-only ids that could be mistaken for export entities.
 */
interface PlagueHistory {
  name: string;
  originId?: EntityId;
  civilizationId?: EntityId;
  regionId?: EntityId;
  beganYear: number;
  endedYear?: number;
  reached: number;
  dead: number;
}

const PLAGUE_KINDS = new Set(['PlagueBegan', 'PlagueSpread', 'PlagueEnded']);

function eventNumber(event: HistoryEvent, key: string): number {
  const value = Number(event.data?.[key]);
  return Number.isFinite(value) ? value : 0;
}

function plagueHistories(events: HistoryEvent[]): PlagueHistory[] {
  const histories: PlagueHistory[] = [];
  const active = new Map<string, PlagueHistory[]>();

  for (const event of events) {
    const name = event.data?.name;
    if (!name) continue;

    const key = `${name}\u0000${event.subject ?? ''}`;

    if (event.kind === 'PlagueBegan') {
      const history: PlagueHistory = {
        name,
        originId: event.subject,
        civilizationId: event.object,
        regionId: event.location,
        beganYear: event.year,
        reached: 0,
        dead: 0,
      };

      histories.push(history);
      const queue = active.get(key) ?? [];
      queue.push(history);
      active.set(key, queue);
    } else if (event.kind === 'PlagueEnded') {
      const queue = active.get(key);
      const history = queue?.shift();
      if (!history) continue;

      history.endedYear = event.year;
      history.reached = eventNumber(event, 'reached');
      history.dead = eventNumber(event, 'dead');
      if (queue?.length === 0) active.delete(key);
    }
  }

  return histories;
}

export function PlagueList({ world }: { world: World }) {
  const events = world.export.events.filter((event) => PLAGUE_KINDS.has(event.kind));
  const histories = plagueHistories(events);
  const dead = histories.reduce((sum, plague) => sum + plague.dead, 0);
  const afflicted = new Set(
    events
      .filter((event) => event.kind !== 'PlagueEnded' && event.subject)
      .map((event) => event.subject as EntityId),
  );
  const widest = histories.reduce((largest, plague) => Math.max(largest, plague.reached), 0);

  const columns: Column<PlagueHistory>[] = [
    {
      key: 'name',
      header: 'Plague',
      cell: (plague) => plague.name,
      sort: (plague) => plague.name,
    },
    {
      key: 'origin',
      header: 'Began in',
      cell: (plague) => <EntityLink world={world} id={plague.originId} />,
      sort: (plague) => (plague.originId ? world.nameOf(plague.originId) : ''),
    },
    {
      key: 'realm',
      header: 'Realm',
      cell: (plague) => <EntityLink world={world} id={plague.civilizationId} />,
      sort: (plague) =>
        plague.civilizationId ? world.nameOf(plague.civilizationId) : '',
    },
    {
      key: 'span',
      header: 'Span',
      cell: (plague) => yearRange(plague.beganYear, plague.endedYear),
      sort: (plague) => plague.beganYear,
    },
    {
      key: 'reached',
      header: 'Reached',
      cell: (plague) => (plague.endedYear === undefined ? '—' : plague.reached),
      sort: (plague) => plague.reached,
      align: 'right',
    },
    {
      key: 'dead',
      header: 'Dead',
      cell: (plague) =>
        plague.endedYear === undefined ? '—' : plague.dead.toLocaleString(),
      sort: (plague) => plague.dead,
      align: 'right',
    },
  ];

  const realmIds = distinct(
    histories
      .map((plague) => plague.civilizationId)
      .filter((id): id is EntityId => id !== undefined),
  );
  const facets: Facet<PlagueHistory>[] = [
    {
      key: 'status',
      label: 'Status',
      options: [
        { value: 'ended', label: 'Burned out', match: (plague) => plague.endedYear !== undefined },
        { value: 'active', label: 'Still burning', match: (plague) => plague.endedYear === undefined },
      ],
    },
    {
      key: 'realm',
      label: 'Origin realm',
      options: realmIds.map((id) => ({
        value: id,
        label: world.nameOf(id),
        match: (plague) => plague.civilizationId === id,
      })),
    },
  ];

  return (
    <div className="space-y-5">
      <PageTitle eyebrow="Calamities" title="Plagues" />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Outbreaks" value={histories.length} />
        <Stat label="Dead" value={dead.toLocaleString()} hint="Reported when each plague ended" />
        <Stat label="Places afflicted" value={afflicted.size} hint="Distinct settlements" />
        <Stat label="Widest reach" value={widest} hint="Recorded settlement bouts in one outbreak" />
      </div>

      <Panel title="Outbreaks">
        <DataTable
          rows={histories}
          columns={columns}
          facets={facets}
          searchText={(plague) =>
            [
              plague.name,
              plague.originId ? world.nameOf(plague.originId) : '',
              plague.civilizationId ? world.nameOf(plague.civilizationId) : '',
              plague.regionId ? world.nameOf(plague.regionId) : '',
            ].join(' ')
          }
          placeholder="Search plagues and origins…"
          initialSort={{ key: 'span' }}
          emptyMessage="No plague was recorded in this world."
        />
      </Panel>

      <Panel title="Plague chronicle">
        <EventList world={world} events={events} emptyMessage="No plague was recorded." />
      </Panel>
    </div>
  );
}

export function DisasterList({ world }: { world: World }) {
  const events = world.export.events.filter((event) => event.kind === 'DisasterStruck');
  const dead = events.reduce((sum, event) => sum + eventNumber(event, 'lost'), 0);
  const struck = new Set(events.flatMap((event) => (event.subject ? [event.subject] : [])));
  const worst = events.reduce<HistoryEvent | undefined>(
    (largest, event) =>
      !largest || eventNumber(event, 'lost') > eventNumber(largest, 'lost') ? event : largest,
    undefined,
  );

  const columns: Column<HistoryEvent>[] = [
    {
      key: 'year',
      header: 'Year',
      cell: (event) => event.year,
      sort: (event) => event.year,
      align: 'right',
    },
    {
      key: 'kind',
      header: 'Disaster',
      cell: (event) => event.data?.kind ?? 'calamity',
      sort: (event) => event.data?.kind ?? '',
    },
    {
      key: 'settlement',
      header: 'Settlement',
      cell: (event) => <EntityLink world={world} id={event.subject} />,
      sort: (event) => (event.subject ? world.nameOf(event.subject) : ''),
    },
    {
      key: 'realm',
      header: 'Realm',
      cell: (event) => <EntityLink world={world} id={event.object} />,
      sort: (event) => (event.object ? world.nameOf(event.object) : ''),
    },
    {
      key: 'region',
      header: 'Region',
      cell: (event) => <EntityLink world={world} id={event.location} />,
      sort: (event) => (event.location ? world.nameOf(event.location) : ''),
    },
    {
      key: 'dead',
      header: 'Dead',
      cell: (event) => eventNumber(event, 'lost').toLocaleString(),
      sort: (event) => eventNumber(event, 'lost'),
      align: 'right',
    },
  ];

  const kinds = distinct(events.map((event) => event.data?.kind ?? 'calamity'));
  const realmIds = distinct(
    events.map((event) => event.object).filter((id): id is EntityId => id !== undefined),
  );
  const facets: Facet<HistoryEvent>[] = [
    {
      key: 'kind',
      label: 'Disaster',
      options: kinds.map((kind) => ({
        value: kind,
        label: kind,
        match: (event) => (event.data?.kind ?? 'calamity') === kind,
      })),
    },
    {
      key: 'realm',
      label: 'Realm',
      options: realmIds.map((id) => ({
        value: id,
        label: world.nameOf(id),
        match: (event) => event.object === id,
      })),
    },
  ];

  return (
    <div className="space-y-5">
      <PageTitle eyebrow="Calamities" title="Disasters" />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Disasters" value={events.length} />
        <Stat label="Dead" value={dead.toLocaleString()} />
        <Stat label="Places struck" value={struck.size} hint="Distinct settlements" />
        <Stat
          label="Worst toll"
          value={worst ? eventNumber(worst, 'lost').toLocaleString() : '—'}
          hint={worst?.subject ? world.nameOf(worst.subject) : undefined}
        />
      </div>

      <Panel title="Recorded disasters">
        <DataTable
          rows={events}
          columns={columns}
          facets={facets}
          searchText={(event) =>
            [
              event.data?.kind ?? '',
              event.subject ? world.nameOf(event.subject) : '',
              event.object ? world.nameOf(event.object) : '',
              event.location ? world.nameOf(event.location) : '',
            ].join(' ')
          }
          placeholder="Search disasters and places…"
          initialSort={{ key: 'year', descending: true }}
          emptyMessage="No disaster was recorded in this world."
        />
      </Panel>
    </div>
  );
}

export function ReligionList({ world }: { world: World }) {
  const { religions } = world.export;
  const followed = religions.filter((faith) => faith.endedYear === undefined);
  const offshoots = religions.filter((faith) => faith.parentId !== undefined);

  const following = (faith: Religion) =>
    world.export.settlements.filter(
      (s) => s.religionId === faith.id && s.abandonedYear === undefined,
    ).length;

  const columns: Column<Religion>[] = [
    {
      key: 'name',
      header: 'Faith',
      cell: (faith) => (
        <span className="flex items-center gap-1.5">
          <EntityLink world={world} id={faith.id} />
          {faith.parentId && <Badge tone="muted">offshoot</Badge>}
        </span>
      ),
      sort: (faith) => faith.name,
    },
    {
      key: 'origin',
      header: 'First preached',
      cell: (faith) => <EntityLink world={world} id={faith.originSettlementId} />,
      sort: (faith) => world.nameOf(faith.originSettlementId),
    },
    {
      key: 'following',
      header: 'Settlements',
      cell: (faith) => following(faith),
      sort: following,
      align: 'right',
    },
    {
      key: 'peak',
      header: 'At its height',
      cell: (faith) => faith.peakSettlements,
      sort: (faith) => faith.peakSettlements,
      align: 'right',
    },
    {
      key: 'deity',
      header: 'Gods',
      cell: (faith) => DEITY_LABELS[faith.character.deity],
      sort: (faith) => faith.character.deity,
    },
    {
      key: 'authority',
      header: 'Church',
      cell: (faith) => AUTHORITY_LABELS[faith.character.authority],
      sort: (faith) => faith.character.authority,
    },
    {
      key: 'fervour',
      header: 'Fervour',
      cell: (faith) => faith.fervour.toFixed(2),
      sort: (faith) => faith.fervour,
      align: 'right',
    },
    {
      key: 'span',
      header: 'Span',
      cell: (faith) => (
        <span className={faith.endedYear !== undefined ? 'text-[var(--ink-faint)]' : ''}>
          {yearRange(faith.foundedYear, faith.endedYear)}
        </span>
      ),
      sort: (faith) => faith.foundedYear,
    },
  ];

  const facets: Facet<Religion>[] = [
    {
      key: 'status',
      label: 'Status',
      options: [
        { value: 'living', label: 'Still followed', match: (f) => f.endedYear === undefined },
        { value: 'gone', label: 'Forgotten', match: (f) => f.endedYear !== undefined },
        { value: 'state', label: 'A realm’s faith', match: (f) =>
          world.export.civilizations.some((civ) => civ.stateReligionId === f.id) },
      ],
    },
    {
      key: 'origin',
      label: 'Origin',
      options: [
        { value: 'root', label: 'Preached new', match: (f) => f.parentId === undefined },
        { value: 'schism', label: 'Broke from another', match: (f) => f.parentId !== undefined },
      ],
    },
    {
      key: 'deity',
      label: 'Gods',
      options: [
        { value: 'mono', label: 'Monotheistic', match: (f) => f.character.deity === 'Monotheistic' },
        { value: 'poly', label: 'Polytheistic', match: (f) => f.character.deity === 'Polytheistic' },
        { value: 'pan', label: 'Pantheistic', match: (f) => f.character.deity === 'Pantheistic' },
        { value: 'anim', label: 'Animistic', match: (f) => f.character.deity === 'Animistic' },
      ],
    },
    {
      key: 'church',
      label: 'Church',
      options: [
        { value: 'hier', label: 'Hierarchical', match: (f) => f.character.authority === 'Hierarchical' },
        { value: 'dec', label: 'Decentralized', match: (f) => f.character.authority === 'Decentralized' },
        { value: 'mon', label: 'Monastic', match: (f) => f.character.authority === 'Monastic' },
      ],
    },
  ];

  return (
    <div className="space-y-5">
      <PageTitle eyebrow="Index" title="Faiths" />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Faiths" value={religions.length} hint="Ever preached" />
        <Stat label="Still followed" value={followed.length} />
        <Stat label="Schisms" value={offshoots.length} hint="Broke from another faith" />
        <Stat
          label="Converted"
          value={
            world.export.settlements.filter(
              (s) => s.religionId !== undefined && s.abandonedYear === undefined,
            ).length
          }
          hint="Settlements following anything at all"
        />
      </div>

      <Panel>
        <DataTable
          rows={religions}
          columns={columns}
          facets={facets}
          searchText={(faith) =>
            `${faith.name} ${faith.character.deity} ${faith.character.authority} ${faith.character.dogma}`
          }
          placeholder="Search faiths…"
          initialSort={{ key: 'peak', descending: true }}
          emptyMessage="No faith was ever preached in this world."
        />
      </Panel>
    </div>
  );
}

export function HolySiteList({ world }: { world: World }) {
  const { holySites } = world.export;
  const independent = holySites.filter((site) => site.settlementId === undefined);

  return (
    <div className="space-y-5">
      <PageTitle eyebrow="Index" title="Holy sites" />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Holy sites" value={holySites.length} hint="Ever established" />
        <Stat label="Independent" value={independent.length} hint="Outside settlements" />
        <Stat label="In settlements" value={holySites.length - independent.length} />
        <Stat
          label="Traditions"
          value={new Set(holySites.map((site: HolySite) => site.description.tradition)).size}
          hint="Architectural families"
        />
      </div>

      <Panel>
        <HolySiteTable world={world} sites={holySites} />
      </Panel>
    </div>
  );
}

export function ArtifactList({ world }: { world: World }) {
  const { artifacts } = world.export;
  const held = artifacts.filter((a) => a.lostYear === undefined);
  const travelled = artifacts.filter((a) => a.provenance.length > 1);

  return (
    <div className="space-y-5">
      <PageTitle eyebrow="Index" title="Artifacts" />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Made" value={artifacts.length} />
        <Stat label="Still held" value={held.length} />
        <Stat label="Changed hands" value={travelled.length} hint="Looted, or lost with a town" />
        <Stat
          label="Sacred"
          value={artifacts.filter((a) => a.religionId !== undefined).length}
          hint="Relics and idols of a faith"
        />
      </div>

      <Panel>
        <ArtifactTable world={world} artifacts={artifacts} />
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

  const facets: Facet<Dynasty>[] = [
    {
      key: 'status',
      label: 'Status',
      options: [
        { value: 'extant', label: 'Extant', match: (house) => house.endedYear === undefined },
        { value: 'ended', label: 'Died out', match: (house) => house.endedYear !== undefined },
      ],
    },
    {
      key: 'throne',
      label: 'Throne',
      options: [
        { value: 'ruled', label: 'Held one', match: (house) => house.rulerIds.length > 0 },
        { value: 'never', label: 'Never held one', match: (house) => house.rulerIds.length === 0 },
        {
          value: 'many',
          label: 'Ruled more than one realm',
          match: (house) =>
            new Set(
              figures(world, house.rulerIds).map((ruler) => ruler.civilizationId),
            ).size > 1,
        },
      ],
    },
  ];

  return (
    <div>
      <PageTitle eyebrow="Index" title="Houses" />
      <Panel>
        <DataTable
          rows={world.export.dynasties}
          columns={columns}
          facets={facets}
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
            {figure.deathDetail ?? DEATH_LABELS[figure.deathCause] ?? figure.deathCause}
          </span>
        ),
      sort: (figure) => figure.deathCause,
    },
  ];

  const facets: Facet<Figure>[] = [
    {
      key: 'fate',
      label: 'Fate',
      options: [
        { value: 'living', label: 'Still living', match: (f) => f.deathYear === undefined },
        ...distinct(
          world.export.figures.filter((f) => f.deathYear !== undefined).map((f) => f.deathCause),
        ).map((cause) => ({
          value: cause,
          label: `Died of ${DEATH_LABELS[cause] ?? cause}`,
          match: (f: Figure) => f.deathYear !== undefined && f.deathCause === cause,
        })),
      ],
    },
    {
      key: 'role',
      label: 'Role',
      options: [
        // "Ruled" means held a crown, not held any office. Left as titles.length these two
        // would have silently become "held anything" and "held nothing" the moment marshals
        // and governors appeared, and nobody would have seen the filter change meaning.
        {
          value: 'ruled',
          label: 'Ruled',
          match: (f) => f.titles.some((t) => t.kind === 'Ruler'),
        },
        {
          value: 'never',
          label: 'Never ruled',
          match: (f) => !f.titles.some((t) => t.kind === 'Ruler'),
        },
        {
          value: 'served',
          label: 'Held office',
          match: (f) => f.titles.some((t) => t.kind !== 'Ruler'),
        },
        // Consorts keep the house they were born into, so no house means married in from
        // outside — which is the only way to find them in a list of a thousand people.
        { value: 'married-in', label: 'Married in', match: (f) => f.dynastyId === undefined },
      ],
    },
    {
      key: 'realm',
      label: 'Realm',
      options: world.export.civilizations.map((civ) => ({
        value: civ.id,
        label: civ.name,
        match: (f: Figure) => f.civilizationId === civ.id,
      })),
    },
  ];

  return (
    <div>
      <PageTitle eyebrow="Index" title="Figures" />
      <Panel>
        <DataTable
          rows={world.export.figures}
          columns={columns}
          facets={facets}
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

  const facets: Facet<Region>[] = [
    {
      key: 'owner',
      label: 'Held by',
      options: [
        { value: 'none', label: 'Nobody', match: (region) => region.owner === undefined },
        ...world.export.civilizations.map((civ) => ({
          value: civ.id,
          label: civ.name,
          match: (region: Region) => region.owner === civ.id,
        })),
      ],
    },
    {
      key: 'biome',
      label: 'Biome',
      options: distinct(rows.map((region) => region.biome)).map((biome) => ({
        value: biome,
        label: biome,
        match: (region: Region) => region.biome === biome,
      })),
    },
    {
      key: 'features',
      label: 'Features',
      options: [
        { value: 'river', label: 'On a river', match: (region) => region.hasRiver },
        { value: 'coast', label: 'On the coast', match: (region) => region.isCoastal },
        {
          value: 'both',
          label: 'River and coast',
          match: (region) => region.hasRiver && region.isCoastal,
        },
      ],
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
          facets={facets}
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

  const facets: Facet<Culture>[] = [
    {
      key: 'government',
      label: 'Government',
      options: distinct(world.export.cultures.map((culture) => culture.government)).map(
        (government) => ({
          value: government,
          label: government,
          match: (culture: Culture) => culture.government === government,
        }),
      ),
    },
    {
      key: 'succession',
      label: 'Succession',
      options: [
        ...distinct(world.export.cultures.map((culture) => culture.successionLaw)).map((law) => ({
          value: law,
          label: SUCCESSION_LABELS[law] ?? law,
          match: (culture: Culture) => culture.successionLaw === law,
        })),
        { value: 'term', label: 'Serves a fixed term', match: (culture) => culture.termYears > 0 },
      ],
    },
  ];

  return (
    <div>
      <PageTitle eyebrow="Index" title="Cultures" />
      <Panel>
        <DataTable
          rows={world.export.cultures}
          columns={columns}
          facets={facets}
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
  const {
    meta,
    civilizations,
    dynasties,
    settlements,
    tradeRoutes,
    figures,
    events,
    indices,
    wars,
  } = world.export;

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

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4 lg:grid-cols-7">
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
        <Stat
          label="Wars"
          value={wars.length}
          hint={`${world.export.battles.length} battles fought`}
        />
        <Stat
          label="Trade routes"
          value={tradeRoutes.filter((route) => route.endedYear === undefined).length}
          hint={`${tradeRoutes.length} recorded across history`}
        />
        <Stat
          label="Faiths"
          value={`${world.export.religions.filter((f) => f.endedYear === undefined).length} / ${world.export.religions.length}`}
          hint="Still followed of all ever preached"
        />
        <Stat
          label="Artifacts"
          value={world.export.artifacts.length}
          hint={`${world.export.artifacts.filter((a) => a.lostYear === undefined).length} still held`}
        />
        <Stat
          label="Plagues"
          value={indices.eventCountsByKind.PlagueBegan ?? 0}
          hint="Outbreaks recorded"
        />
        <Stat
          label="Disasters"
          value={indices.eventCountsByKind.DisasterStruck ?? 0}
          hint="Calamities recorded"
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

      {/* Everything needed to reproduce this file byte for byte. The export carries no
          timestamp on purpose — seed and hashes are the provenance, and a clock would make the
          determinism test impossible to write. */}
      <Panel title="Provenance">
        <dl>
          <Field label="Seed">
            <span className="tabular-nums">{meta.seed}</span>
          </Field>
          <Field label="Config hash">
            <code className="font-mono text-xs">{meta.configHash}</code>
          </Field>
          <Field label="System order">
            <code className="font-mono text-xs">{meta.systemOrderHash}</code>
            <div className="mt-1 text-xs text-[var(--ink-faint)]">
              {meta.systemOrder.join(' → ')}
            </div>
          </Field>
          <Field label="Years simulated">
            <span className="tabular-nums">
              {meta.yearsSimulated.toLocaleString()}
            </span>
            <span className="ml-2 text-[var(--ink-faint)]">
              (years {meta.startYear} – {meta.endYear})
            </span>
          </Field>
          <Field label="Engine">
            {meta.engineVersion}
            <span className="ml-2 text-[var(--ink-faint)]">
              · schema v{world.export.schemaVersion} · narration syntax v
              {meta.narrationSyntaxVersion}
            </span>
          </Field>
          <Field label="Map lattice">
            <span className="tabular-nums">{world.export.world.regionSize}</span> per region,
            terrain sampled every{' '}
            <span className="tabular-nums">{world.export.world.terrainStride}</span> units
          </Field>
          <Field label="Measures tracked">
            <span className="tabular-nums">{world.export.series.length.toLocaleString()}</span>{' '}
            yearly series across{' '}
            <span className="tabular-nums">
              {new Set(world.export.series.map((s) => s.entity)).size.toLocaleString()}
            </span>{' '}
            entities
          </Field>
        </dl>
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
