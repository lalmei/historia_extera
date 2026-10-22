import { useMemo, useState } from 'react';
import { useLanguage } from '../i18n/LanguageProvider';
import { translateIn } from '../i18n/translations.ts';
import { discoverFigures, SIGNIFICANCE_BANDS } from '../discovery';
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
  OCCUPATION_LABELS,
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
  type SettlementTier,
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

function useListsTranslations() {
  const { language } = useLanguage();
  return (key: string, values?: Readonly<Record<string, string | number | undefined>>) =>
    translateIn(language, 'lists', key, values);
}

export function TradeRouteList({ world }: { world: World }) {
  const tr = useListsTranslations();
  const { tradeRoutes } = world.export;
  const active = tradeRoutes.filter((route) => route.endedYear === undefined);
  const prosperous = active.filter((route) => route.status === 'Prosperous');
  const overland = active.filter((route) => route.mode === 'Overland');

  return (
    <div className="space-y-5">
      <PageTitle eyebrow={tr('index')} title={tr('tradeRoutes')} />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label={tr('active')} value={active.length} />
        <Stat label={tr('recorded')} value={tradeRoutes.length} />
        <Stat label={tr('prosperous')} value={prosperous.length} />
        <Stat label={tr('overland')} value={overland.length} hint={tr('futureRoads')} />
      </div>

      <Panel title={tr('routes')}>
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
  const tr = useListsTranslations();
  const { wars, battles } = world.export;
  const settled = wars.filter((war) => war.endYear !== undefined);
  const decided = settled.filter((war) => war.outcome !== 'Stalemate');
  const dead = battles.reduce((sum, b) => sum + b.attackerLosses + b.defenderLosses, 0);

  return (
    <div className="space-y-5">
      <PageTitle eyebrow={tr('index')} title={tr('wars')} />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label={tr('wars')} value={wars.length} />
        <Stat
          label={tr('decided')}
          value={`${decided.length} / ${settled.length}`}
          hint={tr('foughtToExhaustion')}
        />
        <Stat label={tr('battles')} value={battles.length} />
        <Stat label={tr('dead')} value={dead.toLocaleString()} hint={tr('battleDeaths')} />
      </div>

      <Panel title={tr('wars')}>
        <WarTable world={world} wars={wars} />
      </Panel>

      <Panel title={tr('battles')}>
        <BattleTable world={world} battles={battles} />
      </Panel>
    </div>
  );
}

export function CivilizationList({ world }: { world: World }) {
  const tr = useListsTranslations();
  const { language } = useLanguage();
  const columns: Column<Civilization>[] = [
    {
      key: 'name',
      header: tr('civilization'),
      cell: (civ) => <EntityLink world={world} id={civ.id} />,
      sort: (civ) => civ.name,
    },
    {
      key: 'culture',
      header: tr('culture'),
      cell: (civ) => <EntityLink world={world} id={civ.cultureId} />,
      sort: (civ) => world.nameOf(civ.cultureId),
    },
    {
      key: 'capital',
      header: tr('capital'),
      cell: (civ) => <EntityLink world={world} id={civ.capitalId} />,
      sort: (civ) => (civ.capitalId ? world.nameOf(civ.capitalId) : ''),
    },
    {
      key: 'population',
      header: tr('population'),
      cell: (civ) => civ.population.toLocaleString(),
      sort: (civ) => civ.population,
      align: 'right',
    },
    {
      key: 'settlements',
      header: tr('settlements'),
      cell: (civ) => civ.settlementIds.length,
      sort: (civ) => civ.settlementIds.length,
      align: 'right',
    },
    {
      key: 'span',
      header: tr('span'),
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
      label: tr('status'),
      options: [
        { value: 'standing', label: tr('standing'), match: (civ) => civ.endedYear === undefined },
        { value: 'fallen', label: tr('fallen'), match: (civ) => civ.endedYear !== undefined },
        { value: 'war', label: tr('foughtWar'), match: (civ) => warsOf(world, civ.id).length > 0 },
      ],
    },
    {
      key: 'government',
      label: tr('government'),
      options: distinct(world.export.cultures.map((culture) => culture.government)).map(
        (government) => ({
          value: government,
          label: translateIn(language, 'lore', government),
          match: (civ: Civilization) => governmentOf(civ) === government,
        }),
      ),
    },
  ];

  return (
    <div>
      <PageTitle eyebrow={tr('index')} title={tr('civilizations')} />
      <Panel>
        <DataTable
          rows={world.export.civilizations}
          columns={columns}
          facets={facets}
          searchText={(civ) => `${civ.name} ${world.nameOf(civ.cultureId)}`}
          placeholder={tr('searchCivilizations')}
          initialSort={{ key: 'population', descending: true }}
        />
      </Panel>
    </div>
  );
}

/** The distinct values present, in first-seen order. */
/**
 * The distinct values in a column, minus the ones an older export never recorded.
 *
 * Dropping `undefined` is what keeps a facet built from a field a schema added later from
 * offering a blank option that matches nothing — see `compat.ts`.
 */
function distinct<T>(values: (T | undefined)[]): T[] {
  return [...new Set(values)].filter((value): value is T => value !== undefined);
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
  const tr = useListsTranslations();
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
      header: tr('plague'),
      cell: (plague) => plague.name,
      sort: (plague) => plague.name,
    },
    {
      key: 'origin',
      header: tr('beganIn'),
      cell: (plague) => <EntityLink world={world} id={plague.originId} />,
      sort: (plague) => (plague.originId ? world.nameOf(plague.originId) : ''),
    },
    {
      key: 'realm',
      header: tr('realm'),
      cell: (plague) => <EntityLink world={world} id={plague.civilizationId} />,
      sort: (plague) =>
        plague.civilizationId ? world.nameOf(plague.civilizationId) : '',
    },
    {
      key: 'span',
      header: tr('span'),
      cell: (plague) => yearRange(plague.beganYear, plague.endedYear),
      sort: (plague) => plague.beganYear,
    },
    {
      key: 'reached',
      header: tr('reached'),
      cell: (plague) => (plague.endedYear === undefined ? '—' : plague.reached),
      sort: (plague) => plague.reached,
      align: 'right',
    },
    {
      key: 'dead',
      header: tr('dead'),
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
      label: tr('status'),
      options: [
        { value: 'ended', label: tr('burnedOut'), match: (plague) => plague.endedYear !== undefined },
        { value: 'active', label: tr('stillBurning'), match: (plague) => plague.endedYear === undefined },
      ],
    },
    {
      key: 'realm',
      label: tr('originRealm'),
      options: realmIds.map((id) => ({
        value: id,
        label: world.nameOf(id),
        match: (plague) => plague.civilizationId === id,
      })),
    },
  ];

  return (
    <div className="space-y-5">
      <PageTitle eyebrow={tr('calamities')} title={tr('plagues')} />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label={tr('outbreaks')} value={histories.length} />
        <Stat label={tr('dead')} value={dead.toLocaleString()} hint={tr('reportedAtEnd')} />
        <Stat label={tr('placesAfflicted')} value={afflicted.size} hint={tr('distinctSettlements')} />
        <Stat label={tr('widestReach')} value={widest} hint={tr('settlementBouts')} />
      </div>

      <Panel title={tr('outbreaks')}>
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
          placeholder={tr('searchPlagues')}
          initialSort={{ key: 'span' }}
          emptyMessage={tr('noPlagues')}
        />
      </Panel>

      <Panel title={tr('plagueChronicle')}>
        <EventList world={world} events={events} emptyMessage={tr('noPlagueEvent')} />
      </Panel>
    </div>
  );
}

export function DisasterList({ world }: { world: World }) {
  const tr = useListsTranslations();
  const { language } = useLanguage();
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
      header: tr('year'),
      cell: (event) => event.year,
      sort: (event) => event.year,
      align: 'right',
    },
    {
      key: 'kind',
      header: tr('disaster'),
      cell: (event) => translateIn(language, 'lore', event.data?.kind ?? 'calamity'),
      sort: (event) => event.data?.kind ?? '',
    },
    {
      key: 'settlement',
      header: tr('settlement'),
      cell: (event) => <EntityLink world={world} id={event.subject} />,
      sort: (event) => (event.subject ? world.nameOf(event.subject) : ''),
    },
    {
      key: 'realm',
      header: tr('realm'),
      cell: (event) => <EntityLink world={world} id={event.object} />,
      sort: (event) => (event.object ? world.nameOf(event.object) : ''),
    },
    {
      key: 'region',
      header: tr('region'),
      cell: (event) => <EntityLink world={world} id={event.location} />,
      sort: (event) => (event.location ? world.nameOf(event.location) : ''),
    },
    {
      key: 'dead',
      header: tr('dead'),
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
      label: tr('disaster'),
      options: kinds.map((kind) => ({
        value: kind,
        label: translateIn(language, 'lore', kind),
        match: (event) => (event.data?.kind ?? 'calamity') === kind,
      })),
    },
    {
      key: 'realm',
      label: tr('realm'),
      options: realmIds.map((id) => ({
        value: id,
        label: world.nameOf(id),
        match: (event) => event.object === id,
      })),
    },
  ];

  return (
    <div className="space-y-5">
      <PageTitle eyebrow={tr('calamities')} title={tr('disasters')} />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label={tr('disasters')} value={events.length} />
        <Stat label={tr('dead')} value={dead.toLocaleString()} />
        <Stat label={tr('placesStruck')} value={struck.size} hint={tr('distinctSettlements')} />
        <Stat
          label={tr('worstToll')}
          value={worst ? eventNumber(worst, 'lost').toLocaleString() : '—'}
          hint={worst?.subject ? world.nameOf(worst.subject) : undefined}
        />
      </div>

      <Panel title={tr('recordedDisasters')}>
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
          placeholder={tr('searchDisasters')}
          initialSort={{ key: 'year', descending: true }}
          emptyMessage={tr('noDisasters')}
        />
      </Panel>
    </div>
  );
}

export function ReligionList({ world }: { world: World }) {
  const tr = useListsTranslations();
  const { language } = useLanguage();
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
      header: tr('faith'),
      cell: (faith) => (
        <span className="flex items-center gap-1.5">
          <EntityLink world={world} id={faith.id} />
          {faith.parentId && <Badge tone="muted">{tr('offshoot')}</Badge>}
        </span>
      ),
      sort: (faith) => faith.name,
    },
    {
      key: 'origin',
      header: tr('firstPreached'),
      cell: (faith) => <EntityLink world={world} id={faith.originSettlementId} />,
      sort: (faith) => world.nameOf(faith.originSettlementId),
    },
    {
      key: 'following',
      header: tr('settlements'),
      cell: (faith) => following(faith),
      sort: following,
      align: 'right',
    },
    {
      key: 'peak',
      header: tr('atItsHeight'),
      cell: (faith) => faith.peakSettlements,
      sort: (faith) => faith.peakSettlements,
      align: 'right',
    },
    {
      key: 'deity',
      header: tr('gods'),
      cell: (faith) => translateIn(language, 'lore', DEITY_LABELS[faith.character.deity]),
      sort: (faith) => faith.character.deity,
    },
    {
      key: 'authority',
      header: tr('church'),
      cell: (faith) => translateIn(language, 'lore', AUTHORITY_LABELS[faith.character.authority]),
      sort: (faith) => faith.character.authority,
    },
    {
      key: 'fervour',
      header: tr('fervour'),
      cell: (faith) => faith.fervour.toFixed(2),
      sort: (faith) => faith.fervour,
      align: 'right',
    },
    {
      key: 'span',
      header: tr('span'),
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
      label: tr('status'),
      options: [
        { value: 'living', label: tr('stillFollowed'), match: (f) => f.endedYear === undefined },
        { value: 'gone', label: tr('forgotten'), match: (f) => f.endedYear !== undefined },
        { value: 'state', label: tr('realmFaith'), match: (f) =>
          world.export.civilizations.some((civ) => civ.stateReligionId === f.id) },
      ],
    },
    {
      key: 'origin',
      label: tr('origin'),
      options: [
        { value: 'root', label: tr('preachedNew'), match: (f) => f.parentId === undefined },
        { value: 'schism', label: tr('brokeFromAnother'), match: (f) => f.parentId !== undefined },
      ],
    },
    {
      key: 'deity',
      label: tr('gods'),
      options: [
        { value: 'mono', label: tr('monotheistic'), match: (f) => f.character.deity === 'Monotheistic' },
        { value: 'poly', label: tr('polytheistic'), match: (f) => f.character.deity === 'Polytheistic' },
        { value: 'pan', label: tr('pantheistic'), match: (f) => f.character.deity === 'Pantheistic' },
        { value: 'anim', label: tr('animistic'), match: (f) => f.character.deity === 'Animistic' },
      ],
    },
    {
      key: 'church',
      label: tr('church'),
      options: [
        { value: 'hier', label: tr('hierarchical'), match: (f) => f.character.authority === 'Hierarchical' },
        { value: 'dec', label: tr('decentralized'), match: (f) => f.character.authority === 'Decentralized' },
        { value: 'mon', label: tr('monastic'), match: (f) => f.character.authority === 'Monastic' },
      ],
    },
  ];

  return (
    <div className="space-y-5">
      <PageTitle eyebrow={tr('index')} title={tr('faiths')} />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label={tr('faiths')} value={religions.length} hint={tr('everPreached')} />
        <Stat label={tr('stillFollowed')} value={followed.length} />
        <Stat label={tr('schisms')} value={offshoots.length} hint={tr('brokeFromAnotherFaith')} />
        <Stat
          label={tr('converted')}
          value={
            world.export.settlements.filter(
              (s) => s.religionId !== undefined && s.abandonedYear === undefined,
            ).length
          }
          hint={tr('settlementsFollowingFaith')}
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
          placeholder={tr('searchFaiths')}
          initialSort={{ key: 'peak', descending: true }}
          emptyMessage={tr('noFaiths')}
        />
      </Panel>
    </div>
  );
}

export function HolySiteList({ world }: { world: World }) {
  const tr = useListsTranslations();
  const { holySites } = world.export;
  const independent = holySites.filter((site) => site.settlementId === undefined);

  return (
    <div className="space-y-5">
      <PageTitle eyebrow={tr('index')} title={tr('holySites')} />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label={tr('holySites')} value={holySites.length} hint={tr('everEstablished')} />
        <Stat label={tr('independent')} value={independent.length} hint={tr('outsideSettlements')} />
        <Stat label={tr('inSettlements')} value={holySites.length - independent.length} />
        <Stat
          label={tr('traditions')}
          value={
            new Set(
              holySites
                .map((site: HolySite) => site.description?.tradition)
                .filter((tradition) => tradition !== undefined),
            ).size
          }
          hint={tr('architecturalFamilies')}
        />
      </div>

      <Panel>
        <HolySiteTable world={world} sites={holySites} />
      </Panel>
    </div>
  );
}

export function ArtifactList({ world }: { world: World }) {
  const tr = useListsTranslations();
  const { artifacts } = world.export;
  const held = artifacts.filter((a) => a.lostYear === undefined);
  const travelled = artifacts.filter((a) => a.provenance.length > 1);

  return (
    <div className="space-y-5">
      <PageTitle eyebrow={tr('index')} title={tr('artifacts')} />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label={tr('made')} value={artifacts.length} />
        <Stat label={tr('stillHeld')} value={held.length} />
        <Stat label={tr('changedHands')} value={travelled.length} hint={tr('lootedOrLost')} />
        <Stat
          label={tr('sacred')}
          value={artifacts.filter((a) => a.religionId !== undefined).length}
          hint={tr('faithRelics')}
        />
      </div>

      <Panel>
        <ArtifactTable world={world} artifacts={artifacts} />
      </Panel>
    </div>
  );
}

export function SettlementList({ world }: { world: World }) {
  const tr = useListsTranslations();
  return (
    <div className="space-y-5">
      <PageTitle eyebrow={tr('index')} title={tr('settlements')} />
      <SettlementHierarchy world={world} />
      <Panel>
        <SettlementTable world={world} settlements={world.export.settlements} />
      </Panel>
    </div>
  );
}

/** Population at which each tier begins. Mirrors the engine's SettlementTiers ladder. */
const TIER_FLOORS: { tier: SettlementTier; from: number; to?: number }[] = [
  { tier: 'Hamlet', from: 0, to: 180 },
  { tier: 'Village', from: 180, to: 900 },
  { tier: 'Town', from: 900, to: 4000 },
  { tier: 'City', from: 4000 },
];

/**
 * The shape of the settlement hierarchy, as one row.
 *
 * <b>A property of the whole world that no per-settlement page can show.</b> A world where most
 * places are cities has lost the tier ladder's meaning, and the table below — which is sorted,
 * paged and filtered — is the last place anyone would notice. This was a real failure that
 * survived for the model's whole life precisely because nothing in the viewer or the test suite
 * ever asked what the distribution looked like; it had to be reconstructed from the export with
 * a script before it could be seen at all.
 *
 * The counts are of settlements still standing, because abandoned ones are a different question
 * and mixing them makes both harder to read.
 */
function SettlementHierarchy({ world }: { world: World }) {
  const tr = useListsTranslations();
  const { language } = useLanguage();
  const standing = world.export.settlements.filter((one) => one.abandonedYear === undefined);
  if (standing.length === 0) return null;

  const counts = new Map<SettlementTier, number>();
  for (const settlement of standing) {
    counts.set(settlement.tier, (counts.get(settlement.tier) ?? 0) + 1);
  }

  const largest = Math.max(1, ...TIER_FLOORS.map((band) => counts.get(band.tier) ?? 0));
  const abandoned = world.export.settlements.length - standing.length;

  return (
    <Panel title={tr('hierarchy')}>
      <div className="space-y-2">
        {TIER_FLOORS.map((band) => {
          const count = counts.get(band.tier) ?? 0;
          const share = Math.round((count / standing.length) * 100);

          return (
            <div key={band.tier} className="flex items-center gap-3">
              <div className="w-16 shrink-0 text-sm text-[var(--ink-faint)]">
                {translateIn(language, 'lore', band.tier)}
              </div>
              <div className="w-24 shrink-0 text-xs tabular-nums text-[var(--ink-faint)]">
                {band.to === undefined
                  ? `${band.from.toLocaleString()}+`
                  : `${band.from.toLocaleString()}–${(band.to - 1).toLocaleString()}`}
              </div>
              <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-[var(--rule)]">
                <div
                  className="h-full rounded-full bg-[var(--accent)]"
                  style={{ width: `${(count / largest) * 100}%` }}
                />
              </div>
              <div className="w-20 shrink-0 text-right text-xs tabular-nums text-[var(--ink-faint)]">
                {count} · {share}%
              </div>
            </div>
          );
        })}
      </div>
      <p className="mt-3 text-xs text-[var(--ink-faint)]">
        {tr('standingSettlements', { count: standing.length.toLocaleString() })}
        {abandoned > 0 && <> · {tr('abandonedSettlements', { count: abandoned.toLocaleString() })}</>} · {tr('hierarchyExplanation')}
      </p>
    </Panel>
  );
}

export function DynastyList({ world }: { world: World }) {
  const tr = useListsTranslations();
  const columns: Column<Dynasty>[] = [
    {
      key: 'name',
      header: tr('house'),
      cell: (house) => <EntityLink world={world} id={house.id} />,
      sort: (house) => house.name,
    },
    {
      key: 'origin',
      header: tr('roseIn'),
      cell: (house) => <EntityLink world={world} id={house.originCivilizationId} />,
      sort: (house) => (house.originCivilizationId ? world.nameOf(house.originCivilizationId) : ''),
    },
    {
      key: 'rulers',
      header: tr('rulers'),
      cell: (house) => house.rulerIds.length,
      sort: (house) => house.rulerIds.length,
      align: 'right',
    },
    {
      key: 'members',
      header: tr('bloodline'),
      cell: (house) => house.memberIds.length,
      sort: (house) => house.memberIds.length,
      align: 'right',
    },
    {
      key: 'span',
      header: tr('span'),
      cell: (house) => (
        <span className={house.endedYear !== undefined ? 'text-[var(--ink-faint)]' : ''}>
          {yearRange(house.foundedYear, house.endedYear)}
        </span>
      ),
      sort: (house) => house.foundedYear,
    },
    {
      key: 'fate',
      header: tr('fate'),
      cell: (house) =>
        house.endedYear === undefined ? (
          <Badge tone="accent">{tr('extant')}</Badge>
        ) : (
          <Badge tone="muted">{tr('diedOutYear', { year: house.endedYear })}</Badge>
        ),
      sort: (house) => house.endedYear ?? Number.MAX_SAFE_INTEGER,
    },
  ];

  const facets: Facet<Dynasty>[] = [
    {
      key: 'status',
      label: tr('status'),
      options: [
        { value: 'extant', label: tr('extant'), match: (house) => house.endedYear === undefined },
        { value: 'ended', label: tr('diedOut'), match: (house) => house.endedYear !== undefined },
      ],
    },
    {
      key: 'throne',
      label: tr('throne'),
      options: [
        { value: 'ruled', label: tr('heldThrone'), match: (house) => house.rulerIds.length > 0 },
        { value: 'never', label: tr('neverHeldThrone'), match: (house) => house.rulerIds.length === 0 },
        {
          value: 'many',
          label: tr('ruledMultipleRealms'),
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
      <PageTitle eyebrow={tr('index')} title={tr('houses')} />
      <Panel>
        <DataTable
          rows={world.export.dynasties}
          columns={columns}
          facets={facets}
          searchText={(house) => house.name}
          placeholder={tr('searchHouses')}
          initialSort={{ key: 'rulers', descending: true }}
        />
      </Panel>
    </div>
  );
}

export function FigureList({ world }: { world: World }) {
  const tr = useListsTranslations();
  const { language } = useLanguage();
  const discovery = discoverFigures(world);
  const columns: Column<Figure>[] = [
    {
      key: 'name',
      header: tr('figure'),
      cell: (figure) => <EntityLink world={world} id={figure.id} />,
      sort: (figure) => figure.name,
    },
    {
      key: 'significance',
      header: tr('significance'),
      cell: (figure) => {
        const reading = discovery.byId.get(figure.id)!;
        return (
          <span title={reading.reasons.map((reason) => translateIn(language, 'lore', reason)).join(' · ')}>
            {translateIn(language, 'lore', reading.band)}
          </span>
        );
      },
      sort: (figure) => discovery.byId.get(figure.id)!.score,
    },
    {
      key: 'title',
      header: tr('title'),
      cell: (figure) => figure.titles[0]?.title ?? '—',
      sort: (figure) => figure.titles[0]?.title ?? '',
    },
    {
      key: 'house',
      header: tr('house'),
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
      header: tr('civilization'),
      cell: (figure) => <EntityLink world={world} id={figure.civilizationId} />,
      sort: (figure) => world.nameOf(figure.civilizationId),
    },
    {
      key: 'faith',
      header: tr('faith'),
      cell: (figure) =>
        figure.religionId ? (
          <EntityLink world={world} id={figure.religionId} />
        ) : (
          <span className="text-[var(--ink-faint)]">—</span>
        ),
      sort: (figure) => (figure.religionId ? world.nameOf(figure.religionId) : ''),
    },
    {
      key: 'occupation',
      header: tr('occupation'),
      cell: (figure) =>
        figure.occupation === undefined
          ? '—'
          : translateIn(language, 'lore', OCCUPATION_LABELS[figure.occupation] ?? figure.occupation),
      sort: (figure) => figure.occupation ?? '',
    },
    {
      key: 'lived',
      header: tr('lived'),
      cell: (figure) => yearRange(figure.birthYear, figure.deathYear),
      sort: (figure) => figure.birthYear,
    },
    {
      key: 'age',
      header: tr('age'),
      cell: (figure) =>
        (figure.deathYear ?? world.export.meta.endYear) - figure.birthYear,
      sort: (figure) => (figure.deathYear ?? world.export.meta.endYear) - figure.birthYear,
      align: 'right',
    },
    {
      key: 'fate',
      header: tr('fate'),
      cell: (figure) =>
        figure.deathYear === undefined ? (
          <Badge tone="accent">{tr('living')}</Badge>
        ) : (
          <span className="text-[var(--ink-faint)]">
            {figure.deathDetail ?? translateIn(language, 'lore', DEATH_LABELS[figure.deathCause] ?? figure.deathCause)}
          </span>
        ),
      sort: (figure) => figure.deathCause,
    },
  ];

  const facets: Facet<Figure>[] = [
    {
      key: 'significance',
      label: tr('significance'),
      options: SIGNIFICANCE_BANDS.map((band) => ({
        value: band,
        label: translateIn(language, 'lore', band),
        match: (figure: Figure) => discovery.byId.get(figure.id)?.band === band,
      })),
    },
    {
      key: 'fate',
      label: tr('fate'),
      options: [
        { value: 'living', label: tr('stillLiving'), match: (f) => f.deathYear === undefined },
        ...distinct(
          world.export.figures.filter((f) => f.deathYear !== undefined).map((f) => f.deathCause),
        ).map((cause) => ({
          value: cause,
          label: tr('diedOf', { cause: translateIn(language, 'lore', DEATH_LABELS[cause] ?? cause) }),
          match: (f: Figure) => f.deathYear !== undefined && f.deathCause === cause,
        })),
      ],
    },
    {
      key: 'role',
      label: tr('role'),
      options: [
        // "Ruled" means held a crown, not held any office. Left as titles.length these two
        // would have silently become "held anything" and "held nothing" the moment marshals
        // and governors appeared, and nobody would have seen the filter change meaning.
        {
          value: 'ruled',
          label: tr('ruled'),
          match: (f) => f.titles.some((t) => t.kind === 'Ruler'),
        },
        {
          value: 'never',
          label: tr('neverRuled'),
          match: (f) => !f.titles.some((t) => t.kind === 'Ruler'),
        },
        {
          value: 'served',
          label: tr('heldOffice'),
          match: (f) => f.titles.some((t) => t.kind !== 'Ruler'),
        },
        // Consorts keep the house they were born into, so no house means married in from
        // outside — which is the only way to find them in a list of a thousand people.
        { value: 'married-in', label: tr('marriedIn'), match: (f) => f.dynastyId === undefined },
      ],
    },
    {
      key: 'realm',
      label: tr('realm'),
      options: world.export.civilizations.map((civ) => ({
        value: civ.id,
        label: civ.name,
        match: (f: Figure) => f.civilizationId === civ.id,
      })),
    },
    {
      key: 'faith',
      label: tr('faith'),
      options: [
        { value: 'none', label: tr('noneRecorded'), match: (f: Figure) => f.religionId === undefined },
        ...world.export.religions.map((faith) => ({
          value: faith.id,
          label: faith.name,
          match: (f: Figure) => f.religionId === faith.id,
        })),
      ],
    },
    {
      key: 'occupation',
      label: tr('occupation'),
      options: distinct(world.export.figures.map((f) => f.occupation)).map((occupation) => ({
        value: occupation,
        label: OCCUPATION_LABELS[occupation] ?? occupation,
        match: (f: Figure) => f.occupation === occupation,
      })),
    },
  ];

  return (
    <div>
      <PageTitle eyebrow={tr('index')} title={tr('figures')} />
      <p className="mb-4 text-sm text-[var(--ink-faint)]">
        Significance measures how much of the recorded history runs through a life, through its
        last living year. Hover a band to see what contributed to it.
      </p>
      <Panel>
        <DataTable
          rows={world.export.figures}
          columns={columns}
          facets={facets}
          searchText={(figure) =>
            `${figure.name} ${figure.titles[0]?.title ?? ''} ` +
            `${(figure.occupation && OCCUPATION_LABELS[figure.occupation]) ?? figure.occupation ?? ''} ` +
            `${figure.dynastyId ? world.nameOf(figure.dynastyId) : ''} ` +
            `${world.nameOf(figure.civilizationId)} ` +
            `${figure.religionId ? world.nameOf(figure.religionId) : ''}`
          }
          placeholder={tr('searchFigures')}
          initialSort={{ key: 'significance', descending: true }}
        />
      </Panel>
    </div>
  );
}

export function RegionList({ world }: { world: World }) {
  const tr = useListsTranslations();
  const { language } = useLanguage();
  const [landOnly, setLandOnly] = useState(true);

  const rows = useMemo(
    () => (landOnly ? world.export.regions.filter((r) => r.isLand) : world.export.regions),
    [world.export.regions, landOnly],
  );

  const columns: Column<Region>[] = [
    {
      key: 'name',
      header: tr('region'),
      cell: (region) => <EntityLink world={world} id={region.id} />,
      sort: (region) => region.name,
    },
    { key: 'biome', header: tr('biome'), cell: (region) => translateIn(language, 'lore', region.biome), sort: (region) => region.biome },
    {
      key: 'habitability',
      header: tr('habitability'),
      cell: (region) => region.habitability.toFixed(2),
      sort: (region) => region.habitability,
      align: 'right',
    },
    {
      key: 'height',
      header: tr('height'),
      cell: (region) => `${Math.round(region.meanHeight)} m`,
      sort: (region) => region.meanHeight,
      align: 'right',
    },
    {
      key: 'features',
      header: tr('features'),
      cell: (region) => (
        <span className="flex gap-1">
          {region.hasRiver && <Badge>{tr('river')}</Badge>}
          {region.isCoastal && <Badge>{tr('coast')}</Badge>}
        </span>
      ),
    },
    {
      key: 'owner',
      header: tr('claimedBy'),
      cell: (region) => <EntityLink world={world} id={region.owner} />,
      sort: (region) => (region.owner ? world.nameOf(region.owner) : '~'),
    },
  ];

  const facets: Facet<Region>[] = [
    {
      key: 'owner',
      label: tr('heldBy'),
      options: [
        { value: 'none', label: tr('nobody'), match: (region) => region.owner === undefined },
        ...world.export.civilizations.map((civ) => ({
          value: civ.id,
          label: civ.name,
          match: (region: Region) => region.owner === civ.id,
        })),
      ],
    },
    {
      key: 'biome',
      label: tr('biome'),
      options: distinct(rows.map((region) => region.biome)).map((biome) => ({
        value: biome,
        label: translateIn(language, 'lore', biome),
        match: (region: Region) => region.biome === biome,
      })),
    },
    {
      key: 'features',
      label: tr('features'),
      options: [
        { value: 'river', label: tr('onRiver'), match: (region) => region.hasRiver },
        { value: 'coast', label: tr('onCoast'), match: (region) => region.isCoastal },
        {
          value: 'both',
          label: tr('riverAndCoast'),
          match: (region) => region.hasRiver && region.isCoastal,
        },
      ],
    },
  ];

  return (
    <div>
      <PageTitle eyebrow={tr('index')} title={tr('regions')} />
      <Panel
        actions={
          <label className="inline-flex cursor-pointer items-center gap-1.5 text-xs select-none">
            <input
              type="checkbox"
              checked={landOnly}
              onChange={(event) => setLandOnly(event.target.checked)}
              className="accent-[var(--accent)]"
            />
            {tr('landOnly')}
          </label>
        }
      >
        <DataTable
          rows={rows}
          columns={columns}
          facets={facets}
          searchText={(region) => `${region.name} ${region.biome}`}
          placeholder={tr('searchRegions')}
          initialSort={{ key: 'habitability', descending: true }}
        />
      </Panel>
    </div>
  );
}

export function CultureList({ world }: { world: World }) {
  const tr = useListsTranslations();
  const { language } = useLanguage();
  const columns: Column<Culture>[] = [
    {
      key: 'name',
      header: tr('culture'),
      cell: (culture) => <EntityLink world={world} id={culture.id} />,
      sort: (culture) => culture.name,
    },
    {
      key: 'government',
      header: tr('government'),
      cell: (culture) => translateIn(language, 'lore', culture.government),
      sort: (culture) => culture.government,
    },
    { key: 'title', header: tr('rulerTitle'), cell: (culture) => translateIn(language, 'lore', culture.rulerTitle) },
    {
      key: 'succession',
      header: tr('succession'),
      cell: (culture) => (
        <span>
          {translateIn(language, 'lore', SUCCESSION_LABELS[culture.successionLaw] ?? culture.successionLaw)}
          {culture.termYears > 0 && (
            <span className="text-[var(--ink-faint)]"> · {tr('termYears', { years: culture.termYears })}</span>
          )}
        </span>
      ),
      sort: (culture) => culture.successionLaw,
    },
    {
      key: 'aggression',
      header: tr('aggression'),
      cell: (culture) => culture.aggression.toFixed(2),
      sort: (culture) => culture.aggression,
      align: 'right',
    },
    {
      key: 'expansionism',
      header: tr('expansionism'),
      cell: (culture) => culture.expansionism.toFixed(2),
      sort: (culture) => culture.expansionism,
      align: 'right',
    },
  ];

  const facets: Facet<Culture>[] = [
    {
      key: 'government',
      label: tr('government'),
      options: distinct(world.export.cultures.map((culture) => culture.government)).map(
        (government) => ({
          value: government,
          label: translateIn(language, 'lore', government),
          match: (culture: Culture) => culture.government === government,
        }),
      ),
    },
    {
      key: 'succession',
      label: tr('succession'),
      options: [
        ...distinct(world.export.cultures.map((culture) => culture.successionLaw)).map((law) => ({
          value: law,
          label: translateIn(language, 'lore', SUCCESSION_LABELS[law] ?? law),
          match: (culture: Culture) => culture.successionLaw === law,
        })),
        { value: 'term', label: tr('fixedTerm'), match: (culture) => culture.termYears > 0 },
      ],
    },
  ];

  return (
    <div>
      <PageTitle eyebrow={tr('index')} title={tr('cultures')} />
      <Panel>
        <DataTable
          rows={world.export.cultures}
          columns={columns}
          facets={facets}
          searchText={(culture) => `${culture.name} ${culture.government}`}
          placeholder={tr('searchCultures')}
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
  const tr = useListsTranslations();
  const { meta, events } = world.export;
  const [civ, setCiv] = useState<string>('all');
  const [from, setFrom] = useState(meta.startYear);
  const [to, setTo] = useState(meta.endYear);

  const filtered = useMemo(() => {
    const inRange = (year: number) => year >= from && year <= to;

    if (civ === 'all') return events.filter((event) => inRange(event.year));

    // Reuse the loaded world's index instead of scanning: the civilization's own events
    // plus those of everything it owns would otherwise be a full pass per filter.
    const relevant = new Set<number>();
    const collect = (id: string) => {
      for (const index of world.indices.eventsByEntity[id] ?? []) relevant.add(index);
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
        eyebrow={tr('chronicle')}
        title={tr('timeline')}
        meta={
          <Badge>
            {tr('eventCount', { visible: filtered.length.toLocaleString(), total: events.length.toLocaleString() })}
          </Badge>
        }
      />

      <Panel
        actions={
          <div className="flex flex-wrap items-center gap-3 text-xs">
            <select
              value={civ}
              onChange={(event) => setCiv(event.target.value)}
              className="rounded border border-[var(--rule)] bg-[var(--input)] px-1.5 py-1 text-xs"
            >
              <option value="all">{tr('allCivilizations')}</option>
              {world.export.civilizations.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
            <label className="inline-flex items-center gap-1.5">
              {tr('years')}
              <input
                type="number"
                value={from}
                min={meta.startYear}
                max={meta.endYear}
                onChange={(event) => setFrom(Number(event.target.value))}
                className="w-20 rounded border border-[var(--rule)] bg-[var(--input)] px-1.5 py-1 tabular-nums"
              />
              <span className="text-[var(--ink-faint)]">{tr('to')}</span>
              <input
                type="number"
                value={to}
                min={meta.startYear}
                max={meta.endYear}
                onChange={(event) => setTo(Number(event.target.value))}
                className="w-20 rounded border border-[var(--rule)] bg-[var(--input)] px-1.5 py-1 tabular-nums"
              />
            </label>
          </div>
        }
      >
        <EventList world={world} events={filtered} pageSize={200} separateRegister />
      </Panel>
    </div>
  );
}

export function Overview({ world }: { world: World }) {
  const tr = useListsTranslations();
  const { language } = useLanguage();
  const discovery = discoverFigures(world);
  const centralFigures = discovery.ranked.filter((figure) =>
    ['Consequential', 'Influential'].includes(discovery.byId.get(figure.id)!.band),
  ).slice(0, 5);
  const {
    meta,
    civilizations,
    dynasties,
    settlements,
    tradeRoutes,
    figures,
    events,
    wars,
  } = world.export;
  const { indices } = world;

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
        eyebrow={world.export.world.designation || `Seed ${meta.seed} · config ${meta.configHash}`}
        title={world.export.world.name || tr('worldInBrief')}
        meta={
          <>
            <Badge>{tr(world.export.world.kind === 'Moon' ? 'moon' : 'planet')}</Badge>
            {world.export.world.cosmology && (
              <Badge tone="accent">{tr('starClass', { starClass: world.export.world.cosmology.starClass })}</Badge>
            )}
            <Badge>{tr('seedValue', { seed: meta.seed })}</Badge>
            <Badge>
              {tr('yearRange', { from: meta.startYear, to: meta.endYear })}
            </Badge>
            <Badge>{tr('engineVersion', { version: meta.engineVersion })}</Badge>
          </>
        }
      />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4 lg:grid-cols-7">
        <Stat label={tr('events')} value={events.length.toLocaleString()} />
        <Stat label={tr('civilizations')} value={`${standing.length} / ${civilizations.length}`} hint={tr('standingOfFounded')} />
        <Stat label={tr('settlements')} value={inhabited.length} />
        <Stat label={tr('cities')} value={cities.length} />
        <Stat label={tr('figures')} value={figures.length} hint={tr('stillLivingCount', { count: living.length })} />
        <Stat
          label={tr('houses')}
          value={`${extant.length} / ${dynasties.length}`}
          hint={tr('extantOfAll')}
        />
        <Stat
          label={tr('wars')}
          value={wars.length}
          hint={tr('battlesFoughtCount', { count: world.export.battles.length })}
        />
        <Stat
          label={tr('tradeRoutes')}
          value={tradeRoutes.filter((route) => route.endedYear === undefined).length}
          hint={tr('recordedAcrossHistory', { count: tradeRoutes.length })}
        />
        <Stat
          label={tr('faiths')}
          value={`${world.export.religions.filter((f) => f.endedYear === undefined).length} / ${world.export.religions.length}`}
          hint={tr('stillFollowedOfAll')}
        />
        <Stat
          label={tr('artifacts')}
          value={world.export.artifacts.length}
          hint={tr('stillHeldCount', { count: world.export.artifacts.filter((a) => a.lostYear === undefined).length })}
        />
        <Stat
          label={tr('plagues')}
          value={indices.eventCountsByKind.PlagueBegan ?? 0}
          hint={tr('outbreaks')}
        />
        <Stat
          label={tr('disasters')}
          value={indices.eventCountsByKind.DisasterStruck ?? 0}
          hint={tr('calamitiesRecorded')}
        />
      </div>

      <div className="grid gap-5 lg:grid-cols-2">
        <Panel title={tr('greatHouses')}>
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
                  {tr(house.rulerIds.length === 1 ? 'reign.one' : 'reign.other', { count: house.rulerIds.length })}
                </span>
              </li>
            ))}
          </ol>
        </Panel>

        <Panel title={tr('largestSettlements')}>
          <ol className="space-y-1.5 text-sm">
            {largest.map((settlement) => (
              <li key={settlement.id} className="flex items-baseline justify-between gap-3">
                <span>
                  <EntityLink world={world} id={settlement.id} />
                  <span className="ml-2 text-[var(--ink-faint)]">
                    {translateIn(language, 'lore', settlement.tier)} · <EntityLink world={world} id={settlement.civilizationId} />
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

      {centralFigures.length > 0 && (
        <Panel title={tr('centralFigures')}>
          <ol className="space-y-3 text-sm">
            {centralFigures.map((figure) => {
              const reading = discovery.byId.get(figure.id)!;
              return (
                <li key={figure.id}>
                  <div className="flex flex-wrap items-baseline justify-between gap-2">
                    <span><EntityLink world={world} id={figure.id} /> · {yearRange(figure.birthYear, figure.deathYear)}</span>
                    <Badge>{translateIn(language, 'lore', reading.band)}</Badge>
                  </div>
                  <p className="mt-1 text-xs text-[var(--ink-faint)]">{reading.reasons.map((reason) => translateIn(language, 'lore', reason)).join(' · ')}</p>
                </li>
              );
            })}
          </ol>
          <a href="#/fig" className="mt-3 inline-block text-sm text-[var(--accent)]">{tr('exploreFigures')} →</a>
        </Panel>
      )}

      <Panel title={tr('whatHappened')}>
        {/* Two columns: dynasties roughly tripled the number of distinct event kinds. */}
        <ul className="grid gap-x-8 gap-y-1 text-sm sm:grid-cols-2">
          {kindCounts.map(([kind, count]) => (
            <li key={kind} className="flex items-baseline justify-between gap-3">
              <span>{translateIn(language, 'lore', humanise(kind))}</span>
              <span className="tabular-nums text-[var(--ink-faint)]">
                {count.toLocaleString()}
              </span>
            </li>
          ))}
        </ul>
      </Panel>

      <Panel title={tr('terrainSampling')}>
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
          <Stat
            label={tr('simulation')}
            value={meta.terrainSampling.simulationSamples.toLocaleString()}
            hint={tr('simulationSamplesHint')}
          />
          <Stat
            label={tr('inGameCost')}
            value={`≈${meta.terrainSampling.estimatedGameSecondsSimulation.toFixed(1)}s`}
            hint={tr('inGameCostHint')}
          />
          <Stat
            label={tr('mapRaster')}
            value={meta.terrainSampling.rasterSamples.toLocaleString()}
            hint={tr('presentationOnly')}
          />
          <Stat label={tr('capabilities')} value={<span className="text-xs">{translateIn(language, 'lore', world.export.world.capabilities)}</span>} />
        </div>
        <p className="mt-3 text-xs leading-relaxed text-[var(--ink-faint)]">
          {tr('riverSamplingExplanation')}
        </p>
      </Panel>

      {/* Everything needed to reproduce this file byte for byte. The export carries no
          timestamp on purpose — seed and hashes are the provenance, and a clock would make the
          determinism test impossible to write. */}
      <Panel title={tr('provenance')}>
        <dl>
          <Field label={tr('seed')}>
            <span className="tabular-nums">{meta.seed}</span>
          </Field>
          <Field label={tr('configHash')}>
            <code className="font-mono text-xs">{meta.configHash}</code>
          </Field>
          <Field label={tr('systemOrder')}>
            <code className="font-mono text-xs">{meta.systemOrderHash}</code>
            <div className="mt-1 text-xs text-[var(--ink-faint)]">
              {meta.systemOrder.join(' → ')}
            </div>
          </Field>
          <Field label={tr('yearsSimulated')}>
            <span className="tabular-nums">
              {meta.yearsSimulated.toLocaleString()}
            </span>
            <span className="ml-2 text-[var(--ink-faint)]">
              ({tr('yearRange', { from: meta.startYear, to: meta.endYear })})
            </span>
          </Field>
          <Field label={tr('engine')}>
            {meta.engineVersion}
            <span className="ml-2 text-[var(--ink-faint)]">
              · {tr('schemaVersion', { version: world.export.schemaVersion })} ·{' '}
              {tr('narrationSyntaxVersion', { version: meta.narrationSyntaxVersion })}
            </span>
          </Field>
          <Field label={tr('mapLattice')}>
            {tr('regionLattice', { size: world.export.world.regionSize })},{' '}
            {tr('terrainSampledEvery')}{' '}
            <span className="tabular-nums">{world.export.world.terrainStride}</span> units
          </Field>
          <Field label={tr('measuresTracked')}>
            <span className="tabular-nums">{world.export.series.length.toLocaleString()}</span>{' '}
            {tr('yearlySeriesAcross')}{' '}
            <span className="tabular-nums">
              {new Set(world.export.series.map((s) => s.entity)).size.toLocaleString()}
            </span>{' '}
            {tr('entities')}
          </Field>
        </dl>
      </Panel>

      <Panel title={tr('recentHistory')}>
        <EventList
          world={world}
          events={events.slice(-40).reverse()}
          showFilters={false}
          pageSize={40}
          separateRegister
        />
      </Panel>
    </div>
  );
}
