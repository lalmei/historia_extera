import { useEffect, useState, type ReactNode } from 'react';
import { EventList, NarratedEvent } from '../components/EventList';
import { HistoryPanels } from '../components/History';
import {
  buildBiographyEpisodes,
  disputeAt,
  groupJourneys,
  plotAt,
  undertakingAt,
  visibleBondAt,
  visibleMemoryAt,
  type BiographyEpisode,
} from '../biography';
import {
  Badge,
  type Column,
  DataTable,
  type Dial,
  Dials,
  EntityLink,
  type Facet,
  Field,
  NotInThisExport,
  PageTitle,
  Panel,
  Stat,
  yearRange,
} from '../components/common';
import { IconCity, IconClock, IconPeople, IconSwords } from '../components/icons';
import {
  battlesOf,
  cultureOf,
  dynastyOf,
  figureOf,
  figures,
  regionOf,
  settlementOf,
  tradeRoutesOf,
  treasuresOf,
  treasuresOwnedBy,
  warOf,
  type World,
} from '../store';
import {
  APPARITION_LABELS,
  ARTIFACT_LABELS,
  BOND_LABELS,
  CAMPAIGN_ROLE_LABELS,
  CAREER_FAMILY_LABELS,
  JOURNEY_KIND_LABELS,
  JOURNEY_OUTCOME_LABELS,
  CAUSE_LABELS,
  CLAIM_VERDICT_LABELS,
  DEATH_LABELS,
  DISPUTE_CAUSE_LABELS,
  DISPUTE_OUTCOME_LABELS,
  DISPUTE_STAGE_LABELS,
  AFTERLIFE_LABELS,
  AUTHORITY_LABELS,
  CLERGY_LABELS,
  DEITY_LABELS,
  DIET_LABELS,
  DOGMA_LABELS,
  DRESS_LABELS,
  FESTIVAL_LABELS,
  HOLY_SITE_DEDICATION_LABELS,
  HOLY_SITE_LABELS,
  KIND_LABELS,
  MEMORY_LABELS,
  OCCUPATION_LABELS,
  ORIGIN_LABELS,
  OUTCOME_LABELS,
  PRAYER_LABELS,
  SACRED_TRADITION_LABELS,
  SITE_LABELS,
  SOUL_LABELS,
  SPECIALIZATION_LABELS,
  PLOT_CAUSE_LABELS,
  PLOT_OBJECTIVE_LABELS,
  PLOT_OUTCOME_LABELS,
  PLOT_PHASE_LABELS,
  PLOT_TIE_LABELS,
  SUCCESSION_LABELS,
  TIER_ORDER,
  TOME_CONTENT_LABELS,
  WEALTH_LABELS,
  kindOf,
  type Artifact,
  type Battle,
  type Campaign,
  type Civilization,
  type Culture,
  type Disposition,
  type Dynasty,
  type EntityId,
  type Figure,
  type Dispute,
  type FigureBond,
  type Fortunes,
  type FaithCharacter,
  type HolySite,
  type HolySiteDedicationKind,
  type HistoryEvent,
  type Journey,
  type Plot,
  type Region,
  type Relation,
  type Religion,
  type Settlement,
  type SalientMemory,
  type TradeRoute,
  type Undertaking,
  type Values,
  type War,
} from '../types';

/**
 * The entity pages.
 *
 * Every page follows the same shape: identity at the top, structured facts in the
 * middle, and this entity's slice of the chronicle at the bottom — pulled straight
 * from the export's per-entity index, so it costs an array lookup rather than a
 * scan. Every reference is an EntityLink, which is what makes
 * king → dynasty → war → battle → city browsable through the same linked archive.
 */

export function CivilizationPage({ world, civ }: { world: World; civ: Civilization }) {
  const culture = cultureOf(world, civ.cultureId);
  const capital = settlementOf(world, civ.capitalId);
  const settlements = civ.settlementIds
    .map((id) => settlementOf(world, id))
    .filter((s): s is Settlement => s !== undefined);
  const living = settlements.filter((s) => s.abandonedYear === undefined);

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={culture ? `${culture.government} · ${culture.name}` : 'Civilization'}
        title={civ.name}
        meta={
          <>
            <Badge tone={civ.endedYear === undefined ? 'accent' : 'muted'}>
              {civ.endedYear === undefined ? 'Standing' : 'Fallen'}
            </Badge>
            <span className="text-[var(--ink-faint)]">{yearRange(civ.foundedYear, civ.endedYear)}</span>
          </>
        }
      />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Population" value={civ.population.toLocaleString()} />
        <Stat label="Peak" value={civ.peakPopulation.toLocaleString()} />
        <Stat label="Settlements" value={`${living.length} / ${settlements.length}`} hint="Active of all ever founded" />
        <Stat label="Territory" value={`${civ.territoryRegionIds.length} regions`} />
      </div>

      <Panel title="Details">
        <dl>
          <Field label="Culture">
            <EntityLink world={world} id={civ.cultureId} />
          </Field>
          <Field label="Capital">
            <EntityLink world={world} id={civ.capitalId} />
            {capital && (
              <span className="ml-2 text-[var(--ink-faint)]">
                {capital.tier}, {capital.population.toLocaleString()} people
              </span>
            )}
          </Field>
          <Field label="Current ruler">
            <EntityLink world={world} id={civ.currentRulerId} />
            {civ.currentRulerId && (
              <span className="ml-2 text-[var(--ink-faint)]">since {civ.rulerSinceYear}</span>
            )}
          </Field>
          {civ.regentId && (
            <Field label="Regent">
              <EntityLink world={world} id={civ.regentId} />
              <span className="ml-2 text-[var(--ink-faint)]">governing for a minor</span>
            </Field>
          )}
          <Field label="Ruling house">
            <EntityLink world={world} id={civ.rulingDynastyId} />
          </Field>
          <Field label="Faith">
            {civ.stateReligionId ? (
              <EntityLink world={world} id={civ.stateReligionId} />
            ) : (
              <span className="text-[var(--ink-faint)]">No faith took hold at its seat</span>
            )}
          </Field>
          {culture && (
            <Field label="Succession">
              {SUCCESSION_LABELS[culture.successionLaw] ?? culture.successionLaw}
              {culture.termYears > 0 && (
                <span className="ml-2 text-[var(--ink-faint)]">
                  terms of {culture.termYears} years
                </span>
              )}
            </Field>
          )}
        </dl>
      </Panel>

      <Panel title="Extent">
        <ExtentChart world={world} civ={civ} />
      </Panel>

      <Panel title={`Rulers (${civ.rulerIds.length})`}>
        <Succession world={world} rulerIds={civ.rulerIds} />
      </Panel>

      <Panel title="Standing with its neighbours">
        <DiplomacyPanel world={world} civ={civ} />
      </Panel>

      <Panel title="Wars">
        <WarTable world={world} wars={warsOf(world, civ.id)} />
      </Panel>

      <div className="grid gap-5 lg:grid-cols-2">
        <Panel title="The values it is governed by">
          <Dials dials={valueDials(civ.effectiveValues, culture)} />
          <p className="mt-3 text-xs leading-relaxed text-[var(--ink-faint)]">
            {culture ? (
              <>
                Its culture&rsquo;s own values, moved toward whoever was governing in{' '}
                {world.export.meta.endYear} and then shifted by what the realm had lately been
                through. Ticks mark <EntityLink world={world} id={culture.id} />
                &rsquo;s reading of the same dial; the gap is the reign. Tradition and learning
                never move with a realm&rsquo;s fortunes — a plague leaves nobody less attached to
                their ancestral sites.
              </>
            ) : (
              <>The dials the realm was actually governed by in {world.export.meta.endYear}.</>
            )}
          </p>
        </Panel>

        <Panel title="What it has lately been through">
          <Dials dials={fortuneDials(civ.fortunes)} />
          <p className="mt-3 text-xs leading-relaxed text-[var(--ink-faint)]">
            Where the last years left it, not a running total: all four decay, and grievance
            decays slowest. Being beaten exhausts a realm and being humiliated angers it, which is
            why weariness and grievance are counted apart.
          </p>
        </Panel>
      </div>

      {/* Both panels above are the last year alone. These are every year of it. */}
      <HistoryPanels world={world} id={civ.id} />

      {culture && (
        <div className="grid gap-5 lg:grid-cols-2">
          <Panel title="Cultural values">
            <Dials dials={valueDials(culture)} />
          </Panel>
          <Panel title="Naming language">
            <LexiconPanel culture={culture} />
          </Panel>
        </div>
      )}

      <Panel title={`Settlements (${settlements.length})`}>
        <SettlementTable world={world} settlements={settlements} />
      </Panel>

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(civ.id)} separateRegister />
      </Panel>
    </div>
  );
}

/**
 * Why a settlement is the size it is, rather than only how large it is.
 *
 * A population figure cannot tell a town that stands on exceptional ground from one that
 * stands on six trade routes from one held together by a capital's administration, and those
 * are three different histories. The engine already itemises carrying capacity into exactly
 * those sources, so this is a matter of showing what it computed.
 *
 * <b>The share of the fields is the interesting number, and it is the one a reader would never
 * guess.</b> A village on excellent ground that never grew is not a failure of the soil — it
 * is a place whose neighbour took the fields, and nothing else on the page says so.
 */
function SupportBreakdown({
  settlement,
  support,
}: {
  settlement: Settlement;
  support: NonNullable<Settlement['support']>;
}) {
  const parts = [
    { label: 'Its fields', value: support.fromLand, source: 'Land' as const },
    { label: 'The roads', value: support.fromTrade, source: 'Trade' as const },
    { label: 'The site', value: support.fromSite, source: 'Site' as const },
  ].filter((part) => part.value > 0);

  // Against the capacity rather than the largest part, so the bars read as shares of one
  // whole and a settlement fed by a single source looks like one.
  const total = Math.max(1, support.capacity);
  const filled = settlement.population / total;

  return (
    <div className="space-y-4">
      <p className="text-sm text-[var(--ink-faint)]">
        {SUPPORT_SUMMARY[support.principal]} It could support{' '}
        <span className="text-[var(--ink)]">{support.capacity.toLocaleString()}</span> people
        {filled < 0.85 && (
          <>
            {' '}
            and holds {Math.round(filled * 100)}% of that — it is still growing into the room it
            has, or lately lost people
          </>
        )}
        {filled > 1.05 && <> and holds more, which it will shed over the coming years</>}.
      </p>

      <dl className="space-y-2">
        {parts.map((part) => (
          <div key={part.label} className="flex items-center gap-3">
            <dt className="w-24 shrink-0 text-sm text-[var(--ink-faint)]">{part.label}</dt>
            <dd className="flex min-w-0 flex-1 items-center gap-2">
              <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-[var(--rule)]">
                <div
                  className={`h-full rounded-full ${
                    part.source === support.principal
                      ? 'bg-[var(--accent)]'
                      : 'bg-[var(--ink-faint)]'
                  }`}
                  style={{ width: `${Math.min(100, (part.value / total) * 100)}%` }}
                />
              </div>
              <span className="w-16 shrink-0 text-right text-xs tabular-nums text-[var(--ink-faint)]">
                {part.value.toLocaleString()}
              </span>
            </dd>
          </div>
        ))}
      </dl>

      <dl>
        <Field label="Share of the fields">
          {Math.round(support.landShare * 100)}%
          <span className="ml-2 text-[var(--ink-faint)]">
            {support.landShare > 0.85
              ? 'nothing else is near enough to want them'
              : support.landShare > 0.45
                ? 'shared with its neighbours'
                : 'most of the country around it is worked from somewhere larger'}
          </span>
        </Field>
        <Field label="Traffic reaching it">
          {support.routeTraffic > 0 ? (
            <>
              {support.routeTraffic.toFixed(2)}
              <span className="ml-2 text-[var(--ink-faint)]">
                across every live route that reaches it
              </span>
            </>
          ) : (
            <span className="text-[var(--ink-faint)]">
              No live trade route reaches it — it lives on its own land
            </span>
          )}
        </Field>
      </dl>
    </div>
  );
}

const SUPPORT_SUMMARY: Record<NonNullable<Settlement['support']>['principal'], string> = {
  Land: 'Fed chiefly by the country around it.',
  Trade: 'Fed chiefly by what the roads bring in — it eats more than it grows.',
  Site: 'Fed chiefly by the ground it stands on rather than by the fields around it.',
};

export function SettlementPage({ world, settlement }: { world: World; settlement: Settlement }) {
  const region = regionOf(world, settlement.regionId);
  const treasures = treasuresOf(world, settlement.id);
  const routes = tradeRoutesOf(world, settlement.id);
  const holySites = world.export.holySites.filter((site) => site.settlementId === settlement.id);

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={`${settlement.tier} · ${world.nameOf(settlement.civilizationId)}`}
        title={settlement.name}
        meta={
          <>
            {settlement.isCapital && <Badge tone="accent">Capital</Badge>}
            {settlement.isFortified && <Badge>Fortified</Badge>}
            {settlement.specialization !== 'None' && (
              <Badge>{SPECIALIZATION_LABELS[settlement.specialization]}</Badge>
            )}
            {settlement.yearsDepressed > 10 && settlement.abandonedYear === undefined && (
              <Badge tone="muted">declining {settlement.yearsDepressed}y</Badge>
            )}
            <Badge tone={settlement.abandonedYear === undefined ? 'accent' : 'muted'}>
              {settlement.abandonedYear === undefined ? 'Inhabited' : 'Abandoned'}
            </Badge>
            <span className="text-[var(--ink-faint)]">
              {yearRange(settlement.foundedYear, settlement.abandonedYear)}
            </span>
          </>
        }
      />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Population" value={settlement.population.toLocaleString()} />
        <Stat label="Peak" value={settlement.peakPopulation.toLocaleString()} />
        <Stat label="Founded" value={settlement.foundedYear} />
        <Stat
          label="Position"
          value={`${settlement.x}, ${settlement.z}`}
          hint="World coordinates — real map location in Phase 2"
        />
      </div>

      <Panel title="Details">
        <dl>
          <Field label="Known for">
            {settlement.specialization === 'None' ? (
              <span className="text-[var(--ink-faint)]">
                Too small to have a character of its own
              </span>
            ) : (
              <>
                {SPECIALIZATION_LABELS[settlement.specialization]}
                {settlement.specializedYear !== undefined && (
                  <span className="ml-2 text-[var(--ink-faint)]">
                    since {settlement.specializedYear}
                  </span>
                )}
              </>
            )}
          </Field>
          <Field label="Built here for">
            {settlement.site === 'Plain' ? (
              <span className="text-[var(--ink-faint)]">
                Its soil — nothing else marks the spot
              </span>
            ) : (
              SITE_LABELS[settlement.site]
            )}
          </Field>
          <Field label="Civilization">
            <EntityLink world={world} id={settlement.civilizationId} />
          </Field>
          {settlement.foundedBy && settlement.foundedBy !== settlement.civilizationId && (
            <Field label="Founded by">
              <EntityLink world={world} id={settlement.foundedBy} />
            </Field>
          )}
          <Field label="Region">
            <EntityLink world={world} id={settlement.regionId} />
            {region && (
              <span className="ml-2 text-[var(--ink-faint)]">
                {region.biome}
                {region.hasRiver && ' · on a river'}
                {region.isCoastal && ' · coastal'}
              </span>
            )}
          </Field>
          <Field label="Faith">
            {settlement.religionId ? (
              <>
                <EntityLink world={world} id={settlement.religionId} />
                {settlement.convertedYear !== undefined && (
                  <span className="ml-2 text-[var(--ink-faint)]">
                    since {settlement.convertedYear}
                  </span>
                )}
              </>
            ) : (
              <span className="text-[var(--ink-faint)]">Keeps its own counsel</span>
            )}
          </Field>
        </dl>
      </Panel>

      {settlement.support && (
        <Panel title="What supports it">
          <SupportBreakdown settlement={settlement} support={settlement.support} />
        </Panel>
      )}

      <Panel title="What it has lately been through">
        <Dials dials={fortuneDials(settlement.fortunes)} />
        <p className="mt-3 text-xs leading-relaxed text-[var(--ink-faint)]">
          Where the last years left this place, not a running total: all four decay, and
          grievance decays slowest. A sack or a lost siege exhausts a town; occupation and
          cession are the humiliation that outlives the exhaustion. Plague, fire and famine
          are the hurt it cannot fight.
        </p>
      </Panel>

      {/* The panel above is the last year alone. These are every year of it. */}
      <HistoryPanels world={world} id={settlement.id} />

      {treasures.length > 0 && (
        <Panel title="Treasury">
          <ArtifactTable world={world} artifacts={treasures} />
        </Panel>
      )}

      {routes.length > 0 && (
        <Panel title={`Trade routes (${routes.length})`}>
          <TradeRouteTable world={world} routes={routes} />
        </Panel>
      )}

      {holySites.length > 0 && (
        <Panel title={`Holy sites (${holySites.length})`}>
          <HolySiteTable world={world} sites={holySites} />
        </Panel>
      )}

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(settlement.id)} />
      </Panel>
    </div>
  );
}

export function TradeRoutePage({ world, route }: { world: World; route: TradeRoute }) {
  const a = settlementOf(world, route.settlementAId);
  const b = settlementOf(world, route.settlementBId);

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={`${route.mode} trade route`}
        title={`${a?.name ?? route.settlementAId}–${b?.name ?? route.settlementBId}`}
        meta={
          <>
            <Badge tone={route.status === 'Prosperous' ? 'accent' : 'muted'}>
              {route.status}
            </Badge>
            <span className="text-[var(--ink-faint)]">
              {yearRange(route.foundedYear, route.endedYear)}
            </span>
          </>
        }
      />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Current traffic" value={`${Math.round(route.traffic * 100)}%`} />
        <Stat label="Peak traffic" value={`${Math.round(route.peakTraffic * 100)}%`} />
        <Stat label="Opened" value={route.foundedYear} />
        <Stat
          label="Duration"
          value={`${(route.endedYear ?? world.export.meta.endYear) - route.foundedYear + 1} years`}
        />
      </div>

      <Panel title="Connection">
        <dl>
          <Field label="Endpoints">
            <EntityLink world={world} id={route.settlementAId} />
            <span className="mx-2 text-[var(--ink-faint)]">↔</span>
            <EntityLink world={world} id={route.settlementBId} />
          </Field>
          <Field label="Transport">{route.mode}</Field>
          <Field label="Physical path">
            <span className="text-[var(--ink-faint)]">
              Not yet modelled — this route is the demand a future road network can serve
            </span>
          </Field>
        </dl>
      </Panel>

      <HistoryPanels world={world} id={route.id} />

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(route.id)} />
      </Panel>
    </div>
  );
}

export function TradeRouteTable({ world, routes }: { world: World; routes: TradeRoute[] }) {
  const columns: Column<TradeRoute>[] = [
    {
      key: 'route',
      header: 'Route',
      cell: (route) => <EntityLink world={world} id={route.id} />,
      sort: (route) => world.nameOf(route.id),
    },
    {
      key: 'endpoints',
      header: 'Endpoints',
      cell: (route) => (
        <span>
          <EntityLink world={world} id={route.settlementAId} />
          <span className="mx-1 text-[var(--ink-faint)]">↔</span>
          <EntityLink world={world} id={route.settlementBId} />
        </span>
      ),
      sort: (route) => world.nameOf(route.settlementAId),
    },
    {
      key: 'mode',
      header: 'Transport',
      cell: (route) => route.mode,
      sort: (route) => route.mode,
    },
    {
      key: 'traffic',
      header: 'Traffic',
      cell: (route) => `${Math.round(route.traffic * 100)}%`,
      sort: (route) => route.traffic,
      align: 'right',
    },
    {
      key: 'peak',
      header: 'Peak',
      cell: (route) => `${Math.round(route.peakTraffic * 100)}%`,
      sort: (route) => route.peakTraffic,
      align: 'right',
    },
    {
      key: 'span',
      header: 'Span',
      cell: (route) => (
        <span className={route.endedYear !== undefined ? 'text-[var(--ink-faint)]' : ''}>
          {yearRange(route.foundedYear, route.endedYear)}
        </span>
      ),
      sort: (route) => route.foundedYear,
    },
  ];

  const facets: Facet<TradeRoute>[] = [
    {
      key: 'status',
      label: 'Status',
      options: [
        { value: 'open', label: 'Open', match: (route) => route.endedYear === undefined },
        { value: 'closed', label: 'Closed', match: (route) => route.endedYear !== undefined },
        { value: 'prosperous', label: 'Prosperous', match: (route) => route.status === 'Prosperous' },
        { value: 'declining', label: 'Declining', match: (route) => route.status === 'Declining' },
      ],
    },
    {
      key: 'mode',
      label: 'Transport',
      options: ['Overland', 'River', 'Coastal'].map((mode) => ({
        value: mode,
        label: mode,
        match: (route: TradeRoute) => route.mode === mode,
      })),
    },
  ];

  return (
    <DataTable
      rows={routes}
      columns={columns}
      facets={facets}
      searchText={(route) =>
        `${world.nameOf(route.settlementAId)} ${world.nameOf(route.settlementBId)} ${route.mode}`
      }
      placeholder="Search trade routes…"
      initialSort={{ key: 'traffic', descending: true }}
      emptyMessage="No trade route was established here."
    />
  );
}

export function FigurePage({ world, figure }: { world: World; figure: Figure }) {
  const lastYear = world.export.meta.endYear;
  const firstYear = Math.max(world.export.meta.startYear, figure.birthYear);
  const [selectedYear, setSelectedYear] = useState(lastYear);
  const [showChronicle, setShowChronicle] = useState(false);

  useEffect(() => {
    setSelectedYear(lastYear);
    setShowChronicle(false);
  }, [figure.id, lastYear]);

  const atLatest = selectedYear === lastYear;
  const deathYear = figure.deathYear;
  const deadAtPoint = deathYear !== undefined && deathYear <= selectedYear;
  const age = (deadAtPoint ? deathYear : selectedYear) - figure.birthYear;
  const allEvents = world.eventsFor(figure.id);
  const visibleEvents = allEvents.filter((event) => event.year <= selectedYear);
  const visibleTitles = figure.titles
    .filter((title) => title.fromYear <= selectedYear)
    .map((title) => ({
      ...title,
      toYear:
        title.toYear !== undefined && title.toYear <= selectedYear ? title.toYear : undefined,
    }));
  const activeTitle =
    visibleTitles.find((title) => title.toYear === undefined) ??
    [...visibleTitles].sort((a, b) => b.fromYear - a.fromYear)[0];

  // A rank is never laid down, so the operative rung is simply the last one reached by the
  // year being read — the same shape the titles above have, minus the ending.
  const visibleService = figure.service.filter((step) => step.year <= selectedYear);
  const rank = visibleService[visibleService.length - 1];

  const house = dynastyOf(world, figure.dynastyId);
  const culture = cultureOf(world, figure.cultureId);
  const occupationEvent = [...visibleEvents]
    .reverse()
    .find((event) => event.kind === 'OccupationTaken');
  const trade = atLatest
    ? figure.occupation && figure.occupation !== 'None'
      ? (OCCUPATION_LABELS[figure.occupation] ?? figure.occupation)
      : undefined
    : occupationEvent?.data?.occupation;
  const role = activeTitle?.title ?? trade;
  const hasFamily =
    figure.motherId !== undefined ||
    figure.fatherId !== undefined ||
    figure.spouseIds.length > 0 ||
    figure.childIds.length > 0;
  const claimed = atLatest ? treasuresOwnedBy(world, figure.id) : [];
  const lifeUndertakings = (figure.undertakings ?? [])
    .map((undertaking) => undertakingAt(undertaking, selectedYear))
    .filter((undertaking): undertaking is Undertaking => undertaking !== undefined)
    .sort((a, b) => {
      if (a.state === 'Active' && b.state !== 'Active') return -1;
      if (b.state === 'Active' && a.state !== 'Active') return 1;
      return (b.endYear ?? b.startYear) - (a.endYear ?? a.startYear);
    })
    .slice(0, 4);
  const importantRelationships = (figure.bonds ?? [])
    .filter((bond) => visibleBondAt(bond, selectedYear))
    .sort((a, b) => relationshipImportance(b) - relationshipImportance(a))
    .slice(0, 6);
  const formativeMemories = (figure.memories ?? [])
    .filter((memory) => visibleMemoryAt(memory, selectedYear))
    .sort((a, b) => b.intensity - a.intensity || b.lastReinforcedYear - a.lastReinforcedYear)
    .slice(0, 6);
  const seenInTheSky = (figure.observations ?? [])
    .filter((observation) => observation.year <= selectedYear)
    .sort((a, b) => a.year - b.year);
  const heldAboutTheSky = (figure.claims ?? [])
    .filter((claim) => claim.year <= selectedYear)
    .map((claim) =>
      claim.settledYear !== undefined && claim.settledYear > selectedYear
        ? {
            ...claim,
            verdict: claim.register === 'Measured' ? ('Standing' as const) : ('NotTestable' as const),
            settledYear: undefined,
          }
        : claim,
    )
    .sort((a, b) => a.year - b.year);
  const quarrels = (figure.disputes ?? [])
    .map((dispute) => disputeAt(dispute, selectedYear))
    .filter((dispute): dispute is Dispute => dispute !== undefined)
    .sort((a, b) => {
    if (a.outcome === 'Open' && b.outcome !== 'Open') return -1;
    if (b.outcome === 'Open' && a.outcome !== 'Open') return 1;
    return (b.endYear ?? b.startYear) - (a.endYear ?? a.startYear);
  });
  const conspiracies = (atLatest
    ? [...(figure.plots ?? [])]
    : (figure.plots ?? [])
        .map((plot) => plotAt(plot, figure.id, selectedYear))
        .filter((plot): plot is Plot => plot !== undefined)
  )
    .sort((a, b) => (b.endYear ?? b.startYear) - (a.endYear ?? a.startYear));
  const guardianships = (figure.guardianships ?? [])
    .filter((guardianship) => guardianship.startYear <= selectedYear)
    .map((guardianship) =>
      guardianship.endYear !== undefined && guardianship.endYear > selectedYear
        ? { ...guardianship, end: 'Ongoing' as const, endYear: undefined }
        : guardianship,
    )
    .sort(
    (a, b) => b.startYear - a.startYear || a.guardianId.localeCompare(b.guardianId),
  );
  const mentorships = (figure.mentorships ?? [])
    .filter((mentorship) => mentorship.startYear <= selectedYear)
    .sort(
    (a, b) => b.startYear - a.startYear || a.mentorId.localeCompare(b.mentorId),
  );
  const injuries = (figure.injuries ?? []).filter((injury) => injury.year <= selectedYear);
  const campaigns = (figure.campaigns ?? [])
    .filter((campaign) => campaign.year <= selectedYear)
    .map((campaign) => ({
      ...campaign,
      promotionYear:
        campaign.promotionYear !== undefined && campaign.promotionYear <= selectedYear
          ? campaign.promotionYear
          : undefined,
    }));
  const journeys = (figure.journeys ?? []).filter((journey) => journey.year <= selectedYear);
  const episodes = buildBiographyEpisodes(figure, allEvents, selectedYear);
  const positionPlace =
    activeTitle?.scopeId ??
    [...journeys].reverse().find((journey) => journey.returnSettlementId)?.returnSettlementId ??
    (atLatest ? figure.residenceSettlementId : undefined);
  const background =
    figure.background && figure.background.introducedYear <= selectedYear
      ? figure.background
      : undefined;
  const hasLifeSummary =
    background !== undefined ||
    guardianships.length > 0 ||
    mentorships.length > 0 ||
    conspiracies.length > 0 ||
    lifeUndertakings.length > 0 ||
    importantRelationships.length > 0 ||
    formativeMemories.length > 0 ||
    quarrels.length > 0 ||
    seenInTheSky.length > 0 ||
    heldAboutTheSky.length > 0 ||
    injuries.length > 0;

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={house ? `${role ?? 'Of the house of'} · ${house.name}` : (role ?? 'Figure')}
        title={figure.name}
        meta={
          <>
            <Badge tone={deadAtPoint ? 'muted' : 'accent'}>
              {deadAtPoint ? 'Deceased' : `Living in ${selectedYear}`}
            </Badge>
            <span className="text-[var(--ink-faint)]">
              {deadAtPoint ? yearRange(figure.birthYear, figure.deathYear) : `born ${figure.birthYear}`}
              {' · '}aged {age}
            </span>
          </>
        }
      />

      <Panel
        title="Biography at a point in time"
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <label className="text-xs text-[var(--ink-faint)]">
              Year{' '}
              <input
                type="number"
                aria-label="Biography year"
                min={firstYear}
                max={lastYear}
                value={selectedYear}
                onChange={(event) => {
                  const year = Number(event.target.value);
                  if (Number.isFinite(year)) {
                    setSelectedYear(Math.max(firstYear, Math.min(lastYear, year)));
                  }
                }}
                className="w-20 rounded border border-[var(--rule)] bg-[var(--input)] px-2 py-1 text-xs text-[var(--ink)]"
              />
            </label>
            <button
              type="button"
              onClick={() => setSelectedYear(lastYear)}
              disabled={atLatest}
              className="rounded border border-[var(--rule)] px-2 py-1 text-xs disabled:opacity-40"
            >
              Latest year
            </button>
          </div>
        }
      >
        <label htmlFor={`biography-year-${figure.id}`} className="block text-sm">
          Through year <strong>{selectedYear}</strong>
        </label>
        <input
          id={`biography-year-${figure.id}`}
          type="range"
          min={firstYear}
          max={lastYear}
          value={selectedYear}
          onChange={(event) => setSelectedYear(Number(event.target.value))}
          className="mt-2 w-full accent-[var(--primary)]"
        />
        <div className="mt-1 flex justify-between text-xs text-[var(--ink-faint)]">
          <span>{firstYear}</span>
          <span>{lastYear}</span>
        </div>
        {!atLatest && (
          <p className="mt-2 text-xs text-[var(--ink-faint)]">
            Later outcomes and facts without a historical snapshot are hidden.
          </p>
        )}
      </Panel>

      <Panel title="Life at a glance">
        <div className="grid gap-5 lg:grid-cols-2">
          <section>
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
              Position
            </h3>
            <p className="text-sm">
              {activeTitle ? (
                <>
                  {activeTitle.title} of{' '}
                  <EntityLink world={world} id={activeTitle.scopeId ?? activeTitle.civilizationId} />
                </>
              ) : (
                trade ?? 'No recorded adult position yet'
              )}
            </p>
            {activeTitle && trade && (
              <p className="mt-1 text-xs text-[var(--ink-faint)]">Occupation · {trade}</p>
            )}
            {rank && (
              <p className="mt-1 text-xs text-[var(--ink-faint)]">Rank · {rank.title}</p>
            )}
            {positionPlace && (
              <p className="mt-1 text-xs text-[var(--ink-faint)]">
                {atLatest ? 'Residence' : 'Last recorded place'} ·{' '}
                <EntityLink world={world} id={positionPlace} />
              </p>
            )}
          </section>

          <section>
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
              Origins and upbringing
            </h3>
            {background ? (
              <p className="text-sm">
                Entered the record in {background.introducedYear} through{' '}
                {CAREER_FAMILY_LABELS[background.careerFamily]} at{' '}
                <EntityLink world={world} id={background.originSettlementId} />.
              </p>
            ) : guardianships.length > 0 || mentorships.length > 0 ? (
              <p className="text-sm">
                {guardianships.length > 0 && `${guardianships.length} recorded guardianship`}
                {guardianships.length > 1 && 's'}
                {guardianships.length > 0 && mentorships.length > 0 && ' · '}
                {mentorships.length > 0 && `${mentorships.length} recorded mentorship`}
                {mentorships.length > 1 && 's'}
              </p>
            ) : (
              <p className="text-sm text-[var(--ink-faint)]">No exceptional upbringing was recorded.</p>
            )}
          </section>

          <section>
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
              Important relationships
            </h3>
            {importantRelationships.length > 0 ? (
              <ul className="space-y-1 text-sm">
                {importantRelationships.slice(0, 3).map((bond) => (
                  <li key={bond.otherId}>
                    <EntityLink world={world} id={bond.otherId} />
                    <span className="ml-2 text-xs text-[var(--ink-faint)]">
                      {bond.kinds.map((kind) => BOND_LABELS[kind] ?? kind).join(', ')} ·{' '}
                      {relationshipReading(bond)}
                    </span>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-sm text-[var(--ink-faint)]">No durable relationship is visible yet.</p>
            )}
          </section>

          <section>
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
              What they carried
            </h3>
            {formativeMemories.length > 0 || injuries.length > 0 ? (
              <ul className="space-y-1 text-sm">
                {formativeMemories.slice(0, 2).map((memory, index) => (
                  <MemoryLine
                    key={`${memory.kind}:${memory.year}:${memory.aboutId ?? index}`}
                    world={world}
                    memory={memory}
                  />
                ))}
                {injuries.slice(-1).map((injury) => (
                  <li key={`${injury.causeId}:${injury.year}`}>
                    <span className="text-[var(--ink-faint)]">{injury.year} · </span>
                    {injury.detail} at <EntityLink world={world} id={injury.causeId} />
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-sm text-[var(--ink-faint)]">No formative memory or wound is visible yet.</p>
            )}
          </section>
        </div>

        {(lifeUndertakings.some((undertaking) => undertaking.state === 'Active') ||
          quarrels.some((dispute) => dispute.outcome === 'Open') ||
          conspiracies.some((plot) => plot.outcome === 'Ongoing')) && (
          <section className="mt-5 border-t border-[var(--rule)] pt-4">
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
              Open threads
            </h3>
            <ul className="space-y-1.5 text-sm">
              {lifeUndertakings
                .filter((undertaking) => undertaking.state === 'Active')
                .slice(0, 2)
                .map((undertaking) => (
                  <li key={`open-undertaking:${undertaking.id}`}>
                    Undertaking · {undertaking.objective}
                    {undertaking.targetId && (
                      <>
                        {' · '}
                        <EntityLink world={world} id={undertaking.targetId} />
                      </>
                    )}
                  </li>
                ))}
              {quarrels
                .filter((dispute) => dispute.outcome === 'Open')
                .slice(0, 2)
                .map((dispute) => (
                  <li key={`open-dispute:${dispute.id}:${dispute.otherId}`}>
                    Unresolved {DISPUTE_STAGE_LABELS[dispute.stage].toLowerCase()} with{' '}
                    <EntityLink world={world} id={dispute.otherId} />
                  </li>
                ))}
              {conspiracies
                .filter((plot) => plot.outcome === 'Ongoing')
                .slice(0, 2)
                .map((plot) => (
                  <li key={`open-plot:${plot.leaderId}:${plot.id}`}>
                    {plot.publicYear === undefined ? 'Retrospectively recorded' : 'Revealed'} conspiracy
                    involving{' '}
                    <EntityLink
                      world={world}
                      id={plot.viewpoint === 'Target' ? plot.leaderId : plot.targetId}
                    />
                  </li>
                ))}
            </ul>
          </section>
        )}

        {episodes.length > 0 && (
          <section className="mt-5 border-t border-[var(--rule)] pt-4">
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
              Episodes that shaped this life
            </h3>
            <BiographyEpisodeList world={world} episodes={episodes} />
          </section>
        )}
      </Panel>

      <Panel title="Details">
        <dl>
          <Field label="Sex">{figure.sex}</Field>
          <Field label="House">
            {house ? (
              <EntityLink world={world} id={house.id} />
            ) : (
              <span className="text-[var(--ink-faint)]">Of no recorded house</span>
            )}
          </Field>
          <Field label="Civilization">
            <EntityLink world={world} id={figure.civilizationId} />
          </Field>
          <Field label="Culture">
            <EntityLink world={world} id={figure.cultureId} />
          </Field>
          <Field label="Faith">
            {figure.religionId ? (
              <EntityLink world={world} id={figure.religionId} />
            ) : (
              <span className="text-[var(--ink-faint)]">None recorded</span>
            )}
          </Field>
          <Field label="Born">
            {figure.birthYear}
            {figure.birthSettlementId && (
              <>
                {' in '}
                <EntityLink world={world} id={figure.birthSettlementId} />
              </>
            )}
          </Field>
          <Field label="Occupation">{trade ?? 'None recorded yet'}</Field>
          {figure.origin !== 'Unrecorded' && ORIGIN_LABELS[figure.origin] && (
            <Field label="Rose from">{ORIGIN_LABELS[figure.origin]}</Field>
          )}
          {deadAtPoint && figure.deathYear !== undefined && (
            <Field label="Died">
              {figure.deathYear}
              {figure.deathCause !== 'Unknown' && (
                <span className="ml-2 text-[var(--ink-faint)]">
                  of {figure.deathDetail ?? DEATH_LABELS[figure.deathCause] ?? figure.deathCause}
                </span>
              )}
            </Field>
          )}
          {visibleService.length > 0 && (
            <Field label="Service">
              <ul className="space-y-0.5">
                {visibleService.map((step, index) => (
                  <li key={index}>
                    {step.title} of <EntityLink world={world} id={step.civilizationId} />
                    <span className="ml-2 text-[var(--ink-faint)]">{step.year}</span>
                    {step.claim && (
                      <span className="ml-2 text-[var(--ink-faint)]">{step.claim}</span>
                    )}
                  </li>
                ))}
              </ul>
            </Field>
          )}
          <Field label="Titles">
            {visibleTitles.length === 0 ? (
              <span className="text-[var(--ink-faint)]">None</span>
            ) : (
              <ul className="space-y-0.5">
                {visibleTitles.map((title, index) => (
                  <li key={index}>
                    {title.title} of <EntityLink world={world} id={title.civilizationId} />
                    <span className="ml-2 text-[var(--ink-faint)]">
                      {yearRange(title.fromYear, title.toYear)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Field>
        </dl>
      </Panel>

      <Panel title="Disposition">
        {figure.disposition === undefined ? (
          <NotInThisExport what="A figure's disposition" version={world.schema.version} />
        ) : (
        <>
        <Dials dials={dispositionDials(figure.disposition, culture)} />
        <p className="mt-3 text-xs leading-relaxed text-[var(--ink-faint)]">
          Their own inclinations, on the dials their people hold.
          {culture && (
            <>
              {' '}
              Ticks mark <EntityLink world={world} id={culture.id} />
              &rsquo;s reading of each: everyone is rolled around the values they were born to
              {figure.religionId ? (
                <>
                  , then pulled toward what <EntityLink world={world} id={figure.religionId} />{' '}
                  teaches
                </>
              ) : null}
              , so the gap is the person rather than the people.
            </>
          )}{' '}
          Centralism is rolled around what the office itself invites rather than around a culture,
          so it carries no tick.
          {figure.disposition.independence !== undefined &&
            ' Independence is how far they let their people govern them: a follower stays near ' +
              'the ticks, a rebel answers with their own inclinations.'}
          {visibleTitles.length === 0 &&
            ' Recorded for everyone, though it only ever governed anything for those who came to rule.'}
        </p>
        </>
        )}
      </Panel>

      {hasLifeSummary && (
        <Panel title="Episode ledger">
          <div className="space-y-5">
            {(background || guardianships.length > 0 || mentorships.length > 0) && (
              <section>
                <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
                  Origins and upbringing
                </h3>
                {background && (
                  <p className="text-sm">
                    Became part of the record in {background.introducedYear}, having risen through{' '}
                    {CAREER_FAMILY_LABELS[background.careerFamily]} at{' '}
                    <EntityLink world={world} id={background.originSettlementId} />
                    {background.institutionId &&
                      background.institutionId !== background.originSettlementId && (
                        <>
                          {' through '}
                          <EntityLink world={world} id={background.institutionId} />
                        </>
                      )}
                    {background.sponsorId && (
                      <>
                        {', backed by '}
                        <EntityLink world={world} id={background.sponsorId} />
                      </>
                    )}
                    .
                  </p>
                )}
                {guardianships.length > 0 && (
                  <ul className={`${background ? 'mt-2 ' : ''}space-y-1.5 text-sm`}>
                    {guardianships.map((guardianship) => {
                      const wasGuardian = guardianship.guardianId === figure.id;
                      return (
                        <li key={`${guardianship.guardianId}:${guardianship.wardId}:${guardianship.startYear}`}>
                          <span className="text-[var(--ink-faint)]">{guardianship.startYear} · </span>
                          {wasGuardian ? 'Guardian of ' : 'Guarded by '}
                          <EntityLink
                            world={world}
                            id={wasGuardian ? guardianship.wardId : guardianship.guardianId}
                          />
                          <span className="ml-2 text-xs text-[var(--ink-faint)]">
                            {guardianship.end === 'Ongoing'
                              ? 'ongoing'
                              : `until ${guardianship.endYear ?? guardianship.startYear}`}
                          </span>
                        </li>
                      );
                    })}
                  </ul>
                )}
                {mentorships.length > 0 && (
                  <ul
                    className={`${background || guardianships.length > 0 ? 'mt-2 ' : ''}space-y-1.5 text-sm`}
                  >
                    {mentorships.map((mentorship) => {
                      const taught = mentorship.mentorId === figure.id;
                      return (
                        <li key={`${mentorship.mentorId}:${mentorship.apprenticeId}:${mentorship.startYear}`}>
                          <span className="text-[var(--ink-faint)]">{mentorship.startYear} · </span>
                          {taught ? 'Mentored ' : 'Mentored in '}
                          {taught ? (
                            <EntityLink world={world} id={mentorship.apprenticeId} />
                          ) : (
                            <>
                              {CAREER_FAMILY_LABELS[mentorship.careerFamily]} by{' '}
                              <EntityLink world={world} id={mentorship.mentorId} />
                            </>
                          )}
                          {mentorship.locationId && (
                            <span className="text-[var(--ink-faint)]">
                              {' at '}
                              <EntityLink world={world} id={mentorship.locationId} />
                            </span>
                          )}
                        </li>
                      );
                    })}
                  </ul>
                )}
              </section>
            )}

            {lifeUndertakings.length > 0 && (
              <section>
                <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
                  Undertakings
                </h3>
                <div className="space-y-3">
                  {lifeUndertakings.map((undertaking) => (
                    <LifeUndertaking
                      key={`${undertaking.id}:${undertaking.startYear}`}
                      world={world}
                      undertaking={undertaking}
                    />
                  ))}
                </div>
              </section>
            )}

            {importantRelationships.length > 0 && (
              <section>
                <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
                  Important relationships
                </h3>
                <ul className="space-y-2 text-sm">
                  {importantRelationships.map((bond) => (
                    <li key={bond.otherId} className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
                      <EntityLink world={world} id={bond.otherId} />
                      <span className="flex flex-wrap gap-1">
                        {bond.kinds.map((kind) => (
                          <Badge
                            key={kind}
                            tone={kind === 'Rival' || kind === 'Enemy' ? 'muted' : 'neutral'}
                          >
                            {BOND_LABELS[kind] ?? kind}
                          </Badge>
                        ))}
                      </span>
                      <span className="text-xs text-[var(--ink-faint)]">
                        {relationshipReading(bond)} · since {bond.sinceYear} ·{' '}
                        {eventReading(bond.lastEventKind)} in {bond.lastChangedYear}
                        {bond.lastEntityId &&
                          bond.lastEntityId !== bond.otherId &&
                          bond.lastEntityId !== figure.id && (
                          <>
                            {' · '}
                            <EntityLink world={world} id={bond.lastEntityId} />
                          </>
                        )}
                      </span>
                    </li>
                  ))}
                </ul>
              </section>
            )}

            {formativeMemories.length > 0 && (
              <section>
                <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
                  Formative memories
                </h3>
                {atLatest && <FeelingBadges feelings={figure.feelings} />}
                <ul className="mt-2 space-y-1.5 text-sm">
                  {formativeMemories.map((memory, index) => (
                    <MemoryLine
                      key={`${memory.kind}:${memory.year}:${memory.aboutId ?? index}`}
                      world={world}
                      memory={memory}
                    />
                  ))}
                </ul>
              </section>
            )}

            {quarrels.length > 0 && (
              <section>
                <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
                  Quarrels
                </h3>
                <div className="space-y-3">
                  {quarrels.map((dispute) => (
                    <Quarrel
                      key={`${dispute.id}:${dispute.otherId}:${dispute.startYear}`}
                      world={world}
                      dispute={dispute}
                      self={figure.id}
                    />
                  ))}
                </div>
              </section>
            )}

            {conspiracies.length > 0 && (
              <section>
                <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
                  Conspiracies
                </h3>
                <div className="space-y-3">
                  {conspiracies.map((plot) => (
                    <Conspiracy
                      key={`${plot.leaderId}:${plot.id}`}
                      world={world}
                      plot={plot}
                      self={figure.id}
                      historical={!atLatest}
                    />
                  ))}
                </div>
              </section>
            )}

            {seenInTheSky.length > 0 && (
              <section>
                <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
                  Recorded in the sky
                </h3>
                <ul className="space-y-1.5 text-sm">
                  {seenInTheSky.map((seen, index) => (
                    <li key={`${seen.cometIndex}:${seen.year}:${index}`}>
                      <span className="text-[var(--ink-faint)]">{seen.year} · </span>
                      {APPARITION_LABELS[seen.grade] ?? seen.grade}
                      {seen.settlementId && (
                        <>
                          {' at '}
                          <EntityLink world={world} id={seen.settlementId} />
                        </>
                      )}
                      <span className="ml-2 text-xs text-[var(--ink-faint)]">
                        {seen.interval !== undefined
                          ? `${seen.interval} years after the last their people had written down`
                          : 'the first their people had written down'}
                      </span>
                    </li>
                  ))}
                </ul>
              </section>
            )}

            {heldAboutTheSky.length > 0 && (
              <section>
                <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
                  Held about the sky
                </h3>
                <ul className="space-y-2 text-sm">
                  {heldAboutTheSky.map((claim) => (
                    <li key={`${claim.id}:${claim.year}`} className="border-l border-[var(--line)] pl-3">
                      <p className="flex flex-wrap items-baseline gap-2">
                        <span>
                          {claim.year} · {claim.reading}
                        </span>
                        <Badge
                          tone={
                            claim.verdict === 'Confirmed'
                              ? 'accent'
                              : claim.verdict === 'Refuted'
                                ? 'muted'
                                : 'neutral'
                          }
                        >
                          {claim.register}
                        </Badge>
                      </p>
                      <p className="mt-0.5 text-xs text-[var(--ink-faint)]">
                        {CLAIM_VERDICT_LABELS[claim.verdict] ?? claim.verdict}
                        {claim.predictedYear !== undefined && ` · looked for it in ${claim.predictedYear}`}
                        {claim.settledYear !== undefined && ` · settled ${claim.settledYear}`}
                        {claim.settledYear !== undefined &&
                          !claim.claimantSawTheAnswer &&
                          ', after their death'}
                      </p>
                      {claim.restsOnYears.length > 0 && (
                        <p className="mt-0.5 text-xs text-[var(--ink-faint)]">
                          From sightings in {claim.restsOnYears.join(', ')}
                        </p>
                      )}
                    </li>
                  ))}
                </ul>
              </section>
            )}

            {injuries.length > 0 && (
              <section>
                <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-[var(--ink-faint)]">
                  Wounds carried
                </h3>
                <ul className="space-y-1.5 text-sm">
                  {injuries.map((injury, index) => (
                    <li key={`${injury.causeId}:${injury.year}:${index}`}>
                      <span className="text-[var(--ink-faint)]">{injury.year} · </span>
                      {injury.detail}{' '}
                      {injury.sourceKind === 'DuelFought' ? 'at the hand of' : 'at'}{' '}
                      <EntityLink world={world} id={injury.causeId} />
                      <span className="ml-2 text-xs text-[var(--ink-faint)]">
                        {injury.permanent
                          ? 'permanent'
                          : injury.recoveryYear !== undefined && injury.recoveryYear <= selectedYear
                            ? `recovered by ${injury.recoveryYear}`
                            : 'still recovering'}
                      </span>
                    </li>
                  ))}
                </ul>
              </section>
            )}
          </div>
        </Panel>
      )}

      {hasFamily && atLatest && (
        <Panel title="Family">
          <FamilyTree world={world} figure={figure} />
        </Panel>
      )}

      {campaigns.length > 0 && (
        <Panel title="Campaigns">
          <CampaignList world={world} campaigns={campaigns} />
        </Panel>
      )}

      {journeys.length > 0 && (
        <Panel title="Travels">
          <JourneyList world={world} journeys={journeys} throughYear={selectedYear} />
        </Panel>
      )}

      {claimed.length > 0 && (
        <Panel title="Treasures">
          <ArtifactTable world={world} artifacts={claimed} />
        </Panel>
      )}

      <Panel
        title="Complete chronicle"
        actions={
          <button
            type="button"
            onClick={() => setShowChronicle((shown) => !shown)}
            className="rounded border border-[var(--rule)] px-2 py-1 text-xs"
          >
            {showChronicle ? 'Hide chronicle' : 'Show chronicle'}
          </button>
        }
      >
        {showChronicle ? (
          <EventList world={world} events={visibleEvents} viewpoint={figure.id} />
        ) : (
          <p className="text-sm text-[var(--ink-faint)]">
            The complete event list is available on demand; the biography above keeps the causal
            episodes in view first.
          </p>
        )}
      </Panel>
    </div>
  );
}

function CampaignList({ world, campaigns }: { world: World; campaigns: Campaign[] }) {
  return (
    <ul className="space-y-1.5 text-sm">
      {campaigns.map((campaign, index) => (
        <li key={`${campaign.warId}:${campaign.battleId ?? 'war'}:${campaign.role}:${index}`}>
          <span className="text-[var(--ink-faint)]">{campaign.year} · </span>
          {CAMPAIGN_ROLE_LABELS[campaign.role] ?? campaign.role}
          {' at '}
          <EntityLink world={world} id={campaign.battleId ?? campaign.warId} />
          <span className="ml-2 text-[var(--ink-faint)]">
            {campaign.triumphant === undefined
              ? 'outcome unsettled'
              : campaign.triumphant
                ? 'triumphant'
                : 'defeated'}
          </span>
          {campaign.fate !== 'Unresolved' && (
            <span className="ml-2 text-[var(--ink-faint)]">
              · {campaign.fate === 'ReturnedUnharmed' ? 'returned unharmed' : campaign.fate.toLowerCase()}
            </span>
          )}
          {campaign.renownGained > 0 && (
            <span className="ml-2 text-[var(--ink-faint)]">
              · renown +{campaign.renownGained}
            </span>
          )}
          {campaign.traumatized && (
            <span className="ml-2 text-[var(--ink-faint)]">· carried trauma</span>
          )}
          {campaign.deserted && (
            <span className="ml-2 text-[var(--ink-faint)]">· deserted</span>
          )}
          {campaign.promotionYear !== undefined && (
            <span className="ml-2 text-[var(--ink-faint)]">
              · led to promotion in {campaign.promotionYear}
            </span>
          )}
        </li>
      ))}
    </ul>
  );
}

function JourneyList({
  world,
  journeys,
  throughYear,
}: {
  world: World;
  journeys: Journey[];
  throughYear: number;
}) {
  const groups = groupJourneys(journeys, throughYear);

  return (
    <ul className="space-y-1.5 text-sm">
      {groups.map((group) => {
        const journey = group.journeys[0];
        const repeated = group.journeys.length > 1;
        const durations = group.journeys.map((item) => item.durationDays);
        const shortest = Math.min(...durations);
        const longest = Math.max(...durations);
        const duration = shortest === longest ? `${shortest}` : `${shortest}–${longest}`;
        return (
          <li key={group.key}>
            <span className="text-[var(--ink-faint)]">
              {repeated ? `${group.firstYear}–${group.lastYear}` : journey.year} ·{' '}
            </span>
            {repeated && `${group.journeys.length} `}
            {JOURNEY_KIND_LABELS[journey.kind] ?? journey.kind}
            {repeated ? ' journeys' : ''}
            {' from '}
            <EntityLink world={world} id={journey.fromSettlementId} />
            {' to '}
            <EntityLink world={world} id={journey.toSettlementId} />
            {journey.viaId && (
              <>
                {' via '}
                <EntityLink world={world} id={journey.viaId} />
              </>
            )}
            <span className="text-[var(--ink-faint)]">
              {' · '}
              {duration} {shortest === 1 && longest === 1 ? 'day' : 'days'}
              {repeated ? ' each' : ''}
              {!repeated && journey.returnYear !== undefined && journey.returnYear > journey.year
                ? ` · returned ${journey.returnYear}`
                : ''}
            </span>
            {repeated && <span className="text-[var(--ink-faint)]"> — all returned</span>}
            {!repeated && journey.outcome && journey.outcome !== 'Returned' && (
              <span className="text-[var(--ink-faint)]">
                {' — '}
                {JOURNEY_OUTCOME_LABELS[journey.outcome] ?? journey.outcome}
              </span>
            )}
          </li>
        );
      })}
    </ul>
  );
}

/**
 * One quarrel, read from the page it is on.
 *
 * The record is shared by both parties, so the only thing that changes between their two
 * pages is the voice: the aggrieved party fell out with someone, and the other party was
 * fallen out with. Printing the same episode twice with two different subjects is the
 * point — it is one fact about two lives, not two facts.
 */
function Quarrel({
  world,
  dispute,
  self,
}: {
  world: World;
  dispute: Dispute;
  self: EntityId;
}) {
  const open = dispute.outcome === 'Open';
  // The cause is worth a link only when it points somewhere the line does not already name.
  // An exposed plot's cause is the person it was against, which on their own page is this page.
  const cause =
    dispute.sourceEntityId && dispute.sourceEntityId !== self && dispute.sourceEntityId !== dispute.otherId
      ? dispute.sourceEntityId
      : undefined;
  return (
    <article className="border-l border-[var(--line)] pl-3">
      <p className="flex flex-wrap items-center gap-2 text-sm font-medium">
        <span>
          {dispute.opened ? 'Fell out with' : 'Was fallen out with by'}{' '}
          <EntityLink world={world} id={dispute.otherId} />
        </span>
        <Badge tone={open ? 'accent' : 'muted'}>
          {open ? DISPUTE_STAGE_LABELS[dispute.stage] : DISPUTE_OUTCOME_LABELS[dispute.outcome]}
        </Badge>
      </p>
      <p className="mt-1 text-xs text-[var(--ink-faint)]">
        {DISPUTE_CAUSE_LABELS[dispute.cause] ?? dispute.cause} · {dispute.startYear}
        {dispute.endYear !== undefined && dispute.endYear !== dispute.startYear
          ? `–${dispute.endYear}`
          : ''}
        {cause && (
          <>
            {' · '}
            <EntityLink world={world} id={cause} />
          </>
        )}
      </p>
      {dispute.resolution && (
        <p className="mt-1 text-xs text-[var(--ink-faint)]">Ended when {dispute.resolution}</p>
      )}
      {dispute.arbiterId && (
        <p className="mt-1 text-sm">
          Judged by <EntityLink world={world} id={dispute.arbiterId} />
        </p>
      )}
      {dispute.acts.length > 0 && (
        <ol className="mt-2 space-y-1 text-xs text-[var(--ink-faint)]">
          {dispute.acts.map((act, index) => (
            <li key={`${act.year}:${act.stage}:${index}`}>
              {act.year} · {act.detail}
            </li>
          ))}
        </ol>
      )}
    </article>
  );
}

/**
 * One conspiracy, read from the page of someone who was in it.
 *
 * Two clocks, and keeping them apart is the point. `startYear` is when it began, which almost
 * nobody knew at the time; `publicYear` is when the world found out, and where it is absent the
 * world never did — so the page says so rather than quietly presenting a secret as public record.
 */
function Conspiracy({
  world,
  plot,
  self,
  historical = false,
}: {
  world: World;
  plot: Plot;
  self: EntityId;
  historical?: boolean;
}) {
  const open = plot.outcome === 'Ongoing';
  const revealed = plot.publicYear !== undefined;

  return (
    <article className="border-l border-[var(--line)] pl-3">
      <p className="flex flex-wrap items-center gap-2 text-sm font-medium">
        <span>
          {plot.viewpoint === 'Leader'
            ? 'Conspired against'
            : plot.viewpoint === 'Target'
              ? 'Was the target of a conspiracy led by'
              : 'Joined a conspiracy against'}{' '}
          <EntityLink
            world={world}
            id={plot.viewpoint === 'Target' ? plot.leaderId : plot.targetId}
          />
          {plot.viewpoint === 'Member' && (
            <>
              {', led by '}
              <EntityLink world={world} id={plot.leaderId} />
            </>
          )}
        </span>
        <Badge tone={open ? 'accent' : 'muted'}>
          {open ? PLOT_PHASE_LABELS[plot.phase] : PLOT_OUTCOME_LABELS[plot.outcome]}
        </Badge>
      </p>
      <p className="mt-1 text-xs text-[var(--ink-faint)]">
        {PLOT_OBJECTIVE_LABELS[plot.objective] ?? plot.objective} ·{' '}
        {PLOT_CAUSE_LABELS[plot.cause] ?? plot.cause} · {plot.startYear}
        {plot.endYear !== undefined && plot.endYear !== plot.startYear ? `–${plot.endYear}` : ''}
      </p>
      <p className="mt-1 text-xs text-[var(--ink-faint)]">
        {revealed
          ? `Known to the world from ${plot.publicYear}`
          : 'Never known to the world; recorded here in retrospect'}
      </p>
      {plot.resolution && (
        <p className="mt-1 text-xs text-[var(--ink-faint)]">Ended when {plot.resolution}</p>
      )}
      {plot.members.length > 0 && (
        <ul className="mt-2 space-y-1 text-sm">
          {plot.members.map((member) => (
            <li key={`${member.figureId}:${member.joinedYear}`}>
              <span className="text-[var(--ink-faint)]">{member.joinedYear} · </span>
              <EntityLink world={world} id={member.figureId} />
              <span className="text-[var(--ink-faint)]">
                {', '}
                {PLOT_TIE_LABELS[member.tie] ?? member.tie}
              </span>
            </li>
          ))}
        </ul>
      )}
      {plot.betrayerId && (
        <p className="mt-1 text-sm">
          Given up by <EntityLink world={world} id={plot.betrayerId} />
        </p>
      )}
      {plot.acts.length > 0 && (
        <ol className="mt-2 space-y-1 text-xs text-[var(--ink-faint)]">
          {plot.acts.map((act, index) => (
            <li key={`${act.year}:${act.phase}:${index}`}>
              {act.year} · {act.detail}
              {!act.known && <span className="ml-1 italic">— secret at the time</span>}
            </li>
          ))}
        </ol>
      )}
      {plot.targetId !== self && open && !historical && (
        <p className="mt-1 text-xs text-[var(--ink-faint)]">
          {plot.access >= 0.65
            ? 'Close access to the target'
            : plot.access >= 0.35
              ? 'Some access to the target'
              : 'Little access to the target'}
          {' · '}
          {plot.suspicion >= 0.5
            ? 'the court is watching'
            : plot.secrecy >= 0.7
              ? 'closely guarded'
              : 'rumours spreading'}
        </p>
      )}
    </article>
  );
}

function BiographyEpisodeList({
  world,
  episodes,
}: {
  world: World;
  episodes: BiographyEpisode[];
}) {
  const readings: Record<BiographyEpisode['kind'], string> = {
    Undertaking: 'Completed an undertaking',
    Conflict: 'A conflict reached its outcome',
    Plot: 'A conspiracy became a defining political episode',
    Campaign: 'A campaign carried a lasting consequence',
  };

  return (
    <ol className="space-y-2 text-sm">
      {episodes.map((episode) => (
        <li
          key={episode.key}
          className="border-l border-[var(--line)] pl-3"
          data-source-events={episode.sourceEventIds.join(',')}
          title={
            episode.sourceEventIds.length > 0
              ? `Evidence: events ${episode.sourceEventIds.join(', ')}`
              : 'Evidence: structured life record'
          }
        >
          <span className="text-[var(--ink-faint)]">
            {yearRange(episode.startYear, episode.endYear)} ·{' '}
          </span>
          {readings[episode.kind]}
          {episode.primaryId && (
            <>
              {' involving '}
              <EntityLink world={world} id={episode.primaryId} />
            </>
          )}
          .
          <span
            className="ml-2 text-xs text-[var(--ink-faint)]"
            aria-label={
              episode.sourceEventIds.length > 0
                ? `Evidence events ${episode.sourceEventIds.join(', ')}`
                : 'Evidence from the structured life record'
            }
          >
            {episode.kind}
            {episode.sourceEventIds.length > 0 &&
              ` · ${episode.sourceEventIds.length} source ${episode.sourceEventIds.length === 1 ? 'event' : 'events'}`}
          </span>
        </li>
      ))}
    </ol>
  );
}

function LifeUndertaking({ world, undertaking }: { world: World; undertaking: Undertaking }) {
  const decisive = undertaking.steps.slice(-3);
  return (
    <article className="border-l border-[var(--line)] pl-3">
      <p className="flex flex-wrap items-center gap-2 text-sm font-medium">
        <span>{undertaking.objective}</span>
        <Badge tone={undertaking.state === 'Active' ? 'accent' : 'muted'}>
          {undertaking.state}
        </Badge>
      </p>
      <p className="mt-1 text-xs text-[var(--ink-faint)]">
        Begun {undertaking.startYear} · motive: {MEMORY_LABELS[undertaking.motive] ?? undertaking.motive}
        {undertaking.motiveEntityId && (
          <>
            {' · '}
            <EntityLink world={world} id={undertaking.motiveEntityId} />
          </>
        )}
        {' · '}
        {undertaking.progress} of {undertaking.requiredProgress} stages
        {undertaking.state === 'Active' ? ` · due by ${undertaking.deadlineYear}` : ''}
      </p>
      {undertaking.outcome && (
        <p className="mt-1 text-xs text-[var(--ink-faint)]">Outcome: {undertaking.outcome}</p>
      )}
      {undertaking.targetId && (
        <p className="mt-1 text-sm">
          Concerns <EntityLink world={world} id={undertaking.targetId} />
        </p>
      )}
      {undertaking.participantIds.length > 0 && (
        <p className="mt-1 text-sm">
          With{' '}
          {undertaking.participantIds.map((id, index) => (
            <span key={id}>
              {index > 0 ? ', ' : ''}
              <EntityLink world={world} id={id} />
            </span>
          ))}
        </p>
      )}
      {undertaking.sponsorId && (
        <p className="mt-1 text-sm">
          Sponsored by <EntityLink world={world} id={undertaking.sponsorId} />
        </p>
      )}
      {decisive.length > 0 && (
        <ol className="mt-2 space-y-1 text-xs text-[var(--ink-faint)]">
          {decisive.map((step, index) => (
            <li key={`${step.year}:${step.sourceKind}:${step.subjectId ?? index}`}>
              {step.year} · {step.outcome}
              {step.subjectId && (
                <>
                  {' · '}
                  <EntityLink world={world} id={step.subjectId} />
                </>
              )}
              {!step.subjectId && step.placeId && (
                <>
                  {' · '}
                  <EntityLink world={world} id={step.placeId} />
                </>
              )}
            </li>
          ))}
        </ol>
      )}
    </article>
  );
}

function FeelingBadges({ feelings }: { feelings: Figure['feelings'] }) {
  const entries: [string, number][] = [
    ['Grieving', feelings?.grief ?? 0],
    ['Afraid', feelings?.fear ?? 0],
    ['Angry', feelings?.anger ?? 0],
    ['Proud', feelings?.pride ?? 0],
    ['Loyal', feelings?.loyalty ?? 0],
  ];
  const visible = entries.filter(([, value]) => value >= 0.18);

  if (visible.length === 0) return null;
  return (
    <div className="flex flex-wrap gap-1">
      {visible.map(([label, value]) => (
        <Badge key={label} tone={value >= 0.65 ? 'accent' : 'muted'}>
          {label}
        </Badge>
      ))}
    </div>
  );
}

function MemoryLine({ world, memory }: { world: World; memory: SalientMemory }) {
  return (
    <li>
      <span className="text-[var(--ink-faint)]">{memory.year} · </span>
      {MEMORY_LABELS[memory.kind] ?? memory.kind}
      {memory.aboutId && (
        <>
          {' · '}
          <EntityLink world={world} id={memory.aboutId} />
        </>
      )}
      {memory.locationId && memory.locationId !== memory.aboutId && (
        <span className="text-[var(--ink-faint)]">
          {' at '}
          <EntityLink world={world} id={memory.locationId} />
        </span>
      )}
      <span className="ml-2 text-xs text-[var(--ink-faint)]">
        {memory.intensity >= 0.7 ? 'vivid' : memory.intensity >= 0.35 ? 'enduring' : 'fading'}
      </span>
    </li>
  );
}

function relationshipImportance(bond: FigureBond): number {
  const roleWeight = bond.kinds.some((kind) =>
    [
      'Spouse',
      'Parent',
      'Child',
      'Sibling',
      'Friend',
      'Lover',
      'Mentor',
      'Guardian',
      'Ward',
      'Patron',
      'Rival',
      'Enemy',
      'CoConspirator',
    ].includes(kind),
  )
    ? 0.8
    : 0.2;
  return (
    roleWeight +
    Math.abs(bond.affection) +
    Math.abs(bond.trust) +
    bond.obligation +
    bond.fear +
    bond.grievance
  );
}

function relationshipReading(bond: FigureBond): string {
  if (bond.grievance >= 0.65) return 'a bitter grievance';
  if (bond.fear >= 0.55) return 'feared';
  if (bond.trust <= -0.45) return 'deeply distrusted';
  if (bond.obligation >= 0.55) return 'bound by duty';
  if (bond.trust >= 0.55) return 'deeply trusted';
  if (bond.affection >= 0.45) return 'held dear';
  if (bond.affection <= -0.35) return 'disliked';
  return bond.lastCause.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
}

function eventReading(kind: string): string {
  return kind.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
}

/**
 * Three generations around one person: parents above, spouses beside, children below.
 *
 * Deliberately not a drawn tree. A rendered graph looks impressive on a king with two
 * children and falls apart on one with eleven, and the thing this view is actually for is
 * getting to the next person in one click — which a list of links does better than a
 * diagram, at any family size. Each child carries their own children's count, so the depth
 * of a line is visible without expanding it.
 */
function FamilyTree({ world, figure }: { world: World; figure: Figure }) {
  const parents = figures(world, [figure.motherId, figure.fatherId].filter(Boolean) as EntityId[]);
  const spouses = figures(world, figure.spouseIds);
  const children = figures(world, figure.childIds);

  return (
    <div className="space-y-3 text-sm">
      <Relations world={world} label="Parents" people={parents} />
      <Relations world={world} label="Married" people={spouses} />
      <Relations world={world} label="Children" people={children} />
    </div>
  );
}

function Relations({
  world,
  label,
  people,
}: {
  world: World;
  label: string;
  people: Figure[];
}) {
  if (people.length === 0) return null;

  return (
    <div>
      <div className="mb-1 text-[0.7rem] font-medium tracking-wide uppercase text-[var(--ink-faint)]">
        {label}
      </div>
      <ul className="space-y-0.5">
        {people.map((person) => (
          <li key={person.id} className="flex flex-wrap items-baseline gap-x-2">
            <EntityLink world={world} id={person.id} />
            <span className="text-xs text-[var(--ink-faint)]">
              {yearRange(person.birthYear, person.deathYear)}
              {person.titles.length > 0 && ` · ${person.titles[0].title}`}
              {person.childIds.length > 0 &&
                ` · ${person.childIds.length} ${person.childIds.length === 1 ? 'child' : 'children'}`}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * A list of rulers in accession order, marking where the crown changed house.
 *
 * The house break is the interesting event in any list of rulers, and it is invisible in a
 * plain sequence of names — so it is called out where it happens rather than left to be
 * inferred by opening each reign in turn.
 */
function Succession({ world, rulerIds }: { world: World; rulerIds: EntityId[] }) {
  const rulers = figures(world, rulerIds);

  if (rulers.length === 0) {
    return <p className="text-sm text-[var(--ink-faint)]">None recorded.</p>;
  }

  return (
    <ol className="space-y-1 text-sm">
      {rulers.map((ruler, index) => {
        const previous = index > 0 ? rulers[index - 1] : undefined;
        const changedHouse =
          previous !== undefined && ruler.dynastyId !== previous.dynastyId;
        // By kind. This read `title.title !== 'Regent'` until offices existed, which meant a
        // ruler who had earlier been a marshal or a governor had that posting rendered as
        // their reign — wrongly, and without anything failing to say so.
        const reign = ruler.titles.find((title) => title.kind === 'Ruler');

        return (
          <li key={`${ruler.id}-${index}`} className="flex flex-wrap items-baseline gap-x-2">
            <span className="w-6 shrink-0 text-right text-xs tabular-nums text-[var(--ink-faint)]">
              {index + 1}
            </span>
            <EntityLink world={world} id={ruler.id} />
            {reign && (
              <span className="text-xs tabular-nums text-[var(--ink-faint)]">
                {yearRange(reign.fromYear, reign.toYear)}
              </span>
            )}
            {changedHouse && ruler.dynastyId && (
              <span className="text-xs text-[var(--ink-faint)]">
                — the crown passed to <EntityLink world={world} id={ruler.dynastyId} />
              </span>
            )}
          </li>
        );
      })}
    </ol>
  );
}

export function DynastyPage({ world, house }: { world: World; house: Dynasty }) {
  const members = figures(world, house.memberIds);
  const living = members.filter((member) => member.deathYear === undefined);
  const founder = figureOf(world, house.founderId);

  // Blood only — a house that counted its consorts as members could never die out.
  const consorts = new Map<EntityId, Figure>();
  for (const member of members) {
    for (const spouseId of member.spouseIds) {
      const spouse = figureOf(world, spouseId);
      if (spouse && spouse.dynastyId !== house.id) consorts.set(spouse.id, spouse);
    }
  }

  const thrones = new Set(figures(world, house.rulerIds).map((r) => r.civilizationId));

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow="House"
        title={house.name}
        meta={
          <>
            <Badge tone={house.endedYear === undefined ? 'accent' : 'muted'}>
              {house.endedYear === undefined ? 'Extant' : 'Died out'}
            </Badge>
            <span className="text-[var(--ink-faint)]">
              {yearRange(house.foundedYear, house.endedYear)}
            </span>
          </>
        }
      />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Rulers" value={house.rulerIds.length} hint="Reigns held, across all realms" />
        <Stat label="Blood" value={`${living.length} / ${members.length}`} hint="Living of all born" />
        <Stat label="Married in" value={consorts.size} />
        <Stat label="Thrones" value={thrones.size} />
      </div>

      <Panel title="Details">
        <dl>
          <Field label="Founder">
            <EntityLink world={world} id={house.founderId} />
            {founder && (
              <span className="ml-2 text-[var(--ink-faint)]">
                {yearRange(founder.birthYear, founder.deathYear)}
              </span>
            )}
          </Field>
          <Field label="Rose in">
            <EntityLink world={world} id={house.originCivilizationId} />
          </Field>
          <Field label="Culture">
            <EntityLink world={world} id={house.cultureId} />
          </Field>
        </dl>
      </Panel>

      <Panel title={`Reigns (${house.rulerIds.length})`}>
        <Succession world={world} rulerIds={house.rulerIds} />
      </Panel>

      <Panel title={`Members (${members.length})`}>
        <MemberTable world={world} members={members} />
      </Panel>

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(house.id)} />
      </Panel>
    </div>
  );
}

function MemberTable({ world, members }: { world: World; members: Figure[] }) {
  const columns: Column<Figure>[] = [
    {
      key: 'name',
      header: 'Name',
      cell: (f) => <EntityLink world={world} id={f.id} />,
      sort: (f) => f.name,
    },
    { key: 'sex', header: 'Sex', cell: (f) => f.sex, sort: (f) => f.sex },
    {
      key: 'title',
      header: 'Held',
      cell: (f) =>
        f.titles.length === 0 ? (
          <span className="text-[var(--ink-faint)]">—</span>
        ) : (
          f.titles[0].title
        ),
      sort: (f) => f.titles[0]?.title ?? '',
    },
    {
      key: 'born',
      header: 'Born',
      cell: (f) => f.birthYear,
      sort: (f) => f.birthYear,
      align: 'right',
    },
    {
      key: 'died',
      header: 'Died',
      cell: (f) =>
        f.deathYear === undefined ? (
          <span className="text-[var(--ink-faint)]">—</span>
        ) : (
          `${f.deathYear}, ${f.deathDetail ?? DEATH_LABELS[f.deathCause] ?? f.deathCause}`
        ),
      sort: (f) => f.deathYear ?? Number.MAX_SAFE_INTEGER,
    },
    {
      key: 'children',
      header: 'Children',
      cell: (f) => f.childIds.length,
      sort: (f) => f.childIds.length,
      align: 'right',
    },
  ];

  return (
    <DataTable
      rows={members}
      columns={columns}
      searchText={(f) => `${f.name} ${f.titles[0]?.title ?? ''}`}
      placeholder="Search the house…"
      initialSort={{ key: 'born', descending: false }}
    />
  );
}

export function RegionPage({ world, region }: { world: World; region: Region }) {
  const settlements = world.export.settlements.filter((s) => s.regionId === region.id);
  const holySites = world.export.holySites.filter((site) => site.regionId === region.id);

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={`Region · ${region.biome}`}
        title={region.name}
        meta={
          <>
            {region.hasRiver && <Badge>River</Badge>}
            {region.isCoastal && <Badge>Coastal</Badge>}
            {!region.isLand && <Badge tone="muted">Open water</Badge>}
            {region.owner && (
              <span>
                claimed by <EntityLink world={world} id={region.owner} />
              </span>
            )}
          </>
        }
      />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
        <Stat label="Habitability" value={region.habitability.toFixed(2)} />
        <Stat label="Fertility" value={region.fertility.toFixed(2)} />
        <Stat label="Mean height" value={`${Math.round(region.meanHeight)} m`} />
        <Stat label="Extent" value={`${region.width} × ${region.height}`} />
        <Stat
          label="Corner"
          value={`${region.minX}, ${region.minZ}`}
          hint="World coordinates of its north-west corner"
        />
      </div>

      <Panel title="Ground">
        <dl>
          <Field label="Biome">{region.biome}</Field>
          <Field label="Terrain">
            {region.isLand ? 'Land' : 'Open water'}
            {region.hasRiver && ' · a river runs through it'}
            {region.isCoastal && ' · meets the sea'}
            {!region.hasRiver && !region.isCoastal && region.isLand && ' · no river, no coast'}
          </Field>
          <Field label="Claimed by">
            {region.owner ? (
              <EntityLink world={world} id={region.owner} />
            ) : (
              <span className="text-[var(--ink-faint)]">Unclaimed at the end of the run</span>
            )}
          </Field>
        </dl>
      </Panel>

      <Panel title="Held by">
        <Tenures world={world} region={region} />
      </Panel>

      <Panel title="Neighbours">
        <div className="flex flex-wrap gap-x-3 gap-y-1.5 text-sm">
          {region.adjacent.map((id) => (
            <EntityLink key={id} world={world} id={id} />
          ))}
        </div>
      </Panel>

      {settlements.length > 0 && (
        <Panel title="Settlements">
          <SettlementTable world={world} settlements={settlements} />
        </Panel>
      )}

      {holySites.length > 0 && (
        <Panel title="Holy sites">
          <HolySiteTable world={world} sites={holySites} />
        </Panel>
      )}

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(region.id)} separateRegister />
      </Panel>
    </div>
  );
}

/**
 * Every realm that ever held one region, and for how long.
 *
 * Replayed from the chronicle rather than read from the export, which only carries the last
 * owner. A frontier province that changed hands three times is the most interesting thing a
 * region has to say about itself, and the flat field says none of it.
 */
function Tenures({ world, region }: { world: World; region: Region }) {
  const held = world.timeline.historyOf(region.id);

  if (held.length === 0) {
    return (
      <p className="text-sm text-[var(--ink-faint)]">
        No realm ever claimed this ground.
      </p>
    );
  }

  return (
    <ol className="space-y-1.5 text-sm">
      {held.map((tenure, index) => (
        <li key={index} className="flex items-baseline justify-between gap-3">
          <span className="flex items-center gap-2">
            <span
              className="inline-block h-2.5 w-2.5 shrink-0 rounded-full"
              style={{ background: world.colourOf(tenure.owner) }}
            />
            <EntityLink world={world} id={tenure.owner} />
          </span>
          <span className="shrink-0 tabular-nums text-[var(--ink-faint)]">
            {yearRange(tenure.since, tenure.until)}
            {tenure.until !== undefined && (
              <span className="ml-2">
                {tenure.until - tenure.since}{' '}
                {tenure.until - tenure.since === 1 ? 'year' : 'years'}
              </span>
            )}
          </span>
        </li>
      ))}
    </ol>
  );
}

/**
 * How much land a realm held, year by year, with the years it spent at war behind it.
 *
 * The single number the export ships — regions held at the end — cannot distinguish a realm
 * that grew steadily from one that was carved up and clawed its way back. Put the wars behind
 * the curve and the causation is visible without a word of explanation: the steps up land on
 * the bands.
 */
function ExtentChart({ world, civ }: { world: World; civ: Civilization }) {
  const { startYear, endYear } = world.export.meta;
  const series = world.timeline.extentOf(civ.id);
  const wars = warsOf(world, civ.id);

  const peak = Math.max(1, ...series);
  const peakYear = startYear + series.indexOf(peak);
  const width = 300;
  const height = 60;
  const top = 6;

  const x = (year: number) =>
    ((year - startYear) / Math.max(1, endYear - startYear)) * width;
  const y = (count: number) => height - (count / peak) * (height - top);

  // One step per year, drawn as steps rather than a smooth line: territory changes on the
  // day a treaty is signed, and a diagonal would imply a province arriving by degrees.
  const steps = series
    .map((count, index) => `${index === 0 ? 'M' : 'L'}${x(startYear + index)} ${y(count)}`)
    .join('');

  const colour = world.colourOf(civ.id);

  return (
    <div>
      <svg viewBox={`0 0 ${width} ${height}`} className="h-24 w-full" preserveAspectRatio="none">
        {/* Neutral rather than a war-like red: realm colours run the whole wheel, and one of
            them is always red. A band that reads as "shaded" beside every hue is worth more
            than one that reads as "bloody" beside seven and vanishes beside the eighth. */}
        {wars.map((war) => (
          <rect
            key={war.id}
            x={x(war.startYear)}
            y={0}
            width={Math.max(0.6, x(war.endYear ?? endYear) - x(war.startYear))}
            height={height}
            fill="var(--ink)"
            fillOpacity={0.1}
          />
        ))}

        <path d={`${steps}L${x(endYear)} ${height}L${x(startYear)} ${height}z`} fill={colour} fillOpacity={0.22} />
        <path
          d={steps}
          fill="none"
          stroke={colour}
          strokeWidth={1.5}
          vectorEffect="non-scaling-stroke"
        />
      </svg>

      <div className="mt-1 flex flex-wrap items-baseline justify-between gap-x-4 text-xs text-[var(--ink-faint)]">
        <span className="tabular-nums">
          {startYear} – {endYear}
        </span>
        <span>
          <span className="tabular-nums">{peak}</span> regions at its height, in{' '}
          <span className="tabular-nums">{peakYear}</span>
          {wars.length > 0 && (
            <span> · shaded where it was at war</span>
          )}
        </span>
      </div>
    </div>
  );
}

/**
 * A faith: where it was first preached, who follows it, and what it broke from.
 *
 * The congregation is drawn for the selected end state, but the curve above it is replayed —
 * a faith that took half a continent and then lost it says so in one glance, which the flat
 * settlement list underneath cannot.
 */
export function ReligionPage({ world, religion }: { world: World; religion: Religion }) {
  const following = world.export.settlements.filter(
    (s) => s.religionId === religion.id && s.abandonedYear === undefined,
  );

  const faithful = world.export.figures.filter((f) => f.religionId === religion.id);
  const notable = faithful.filter((f) => f.titles.length > 0);
  const offshoots = world.export.religions.filter((r) => r.parentId === religion.id);
  const relics = world.export.artifacts.filter((a) => a.religionId === religion.id);
  const holySites = world.export.holySites.filter((site) => site.religionId === religion.id);

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={religion.parentId ? 'Faith · an offshoot' : 'Faith'}
        title={religion.name}
        meta={
          <>
            <Badge tone={religion.endedYear === undefined ? 'accent' : 'muted'}>
              {religion.endedYear === undefined ? 'Followed' : 'Forgotten'}
            </Badge>
            <Badge>{DEITY_LABELS[religion.character.deity]}</Badge>
            <Badge>{AUTHORITY_LABELS[religion.character.authority]}</Badge>
            <span className="text-[var(--ink-faint)]">
              {yearRange(religion.foundedYear, religion.endedYear)}
            </span>
          </>
        }
      />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
        <Stat label="Settlements" value={following.length} hint="Following it now" />
        <Stat label="Figures" value={faithful.length} hint={`${notable.length} held office`} />
        <Stat label="At its height" value={religion.peakSettlements} />
        <Stat
          label="Fervour"
          value={religion.fervour.toFixed(2)}
          hint="How hard it presses outwards"
        />
        <Stat label="Offshoots" value={offshoots.length} />
      </div>

      <Panel title="Following">
        <FollowingChart world={world} religion={religion} />
      </Panel>

      <Panel title="Beliefs">
        <dl>
          <Field label="Gods">{DEITY_LABELS[religion.character.deity]}</Field>
          <Field label="Afterlife">{AFTERLIFE_LABELS[religion.character.afterlife]}</Field>
          <Field label="Soul">{SOUL_LABELS[religion.character.soul]}</Field>
          <Field label="Virtue">{DOGMA_LABELS[religion.character.dogma]}</Field>
        </dl>
      </Panel>

      <Panel title="Church">
        <dl>
          <Field label="Authority">{AUTHORITY_LABELS[religion.character.authority]}</Field>
          <Field label="Clergy">
            {CLERGY_LABELS[religion.character.clergy]}
            {religion.character.celibateClergy ? ' · celibate' : ''}
          </Field>
          <Field label="Wealth">{WEALTH_LABELS[religion.character.wealth]}</Field>
          <Field label="Prayer">{PRAYER_LABELS[religion.character.prayer]}</Field>
          <Field label="Diet">{DIET_LABELS[religion.character.diet]}</Field>
          <Field label="Dress">{DRESS_LABELS[religion.character.dress]}</Field>
          <Field label="Festival">{FESTIVAL_LABELS[religion.character.festival]}</Field>
        </dl>
      </Panel>

      <Panel title="Temper">
        <Dials dials={faithDials(religion.character)} />
      </Panel>

      <Panel title="Details">
        <dl>
          <Field label="First preached">
            <EntityLink world={world} id={religion.originSettlementId} /> in {religion.foundedYear}
          </Field>
          <Field label="By">
            <EntityLink world={world} id={religion.founderId} />
          </Field>
          <Field label="Arose among">
            <EntityLink world={world} id={religion.cultureId} />
          </Field>
          {religion.parentId && (
            <Field label="Broke from">
              <EntityLink world={world} id={religion.parentId} />
            </Field>
          )}
          {offshoots.length > 0 && (
            <Field label="Broken by">
              <span className="flex flex-wrap gap-x-3 gap-y-1">
                {offshoots.map((r) => (
                  <EntityLink key={r.id} world={world} id={r.id} />
                ))}
              </span>
            </Field>
          )}
        </dl>
      </Panel>

      {relics.length > 0 && (
        <Panel title="Sacred to it">
          <ArtifactTable world={world} artifacts={relics} />
        </Panel>
      )}

      {holySites.length > 0 && (
        <Panel title={`Holy sites (${holySites.length})`}>
          <HolySiteTable world={world} sites={holySites} />
        </Panel>
      )}

      {notable.length > 0 && (
        <Panel title={`Notable faithful (${notable.length})`}>
          <MemberTable world={world} members={notable} />
        </Panel>
      )}

      {following.length > 0 && (
        <Panel title={`Settlements (${following.length})`}>
          <SettlementTable world={world} settlements={following} />
        </Panel>
      )}

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(religion.id)} />
      </Panel>
    </div>
  );
}

/** A temple, church or sanctuary, including independent locations beyond settlement walls. */
export function HolySitePage({ world, site }: { world: World; site: HolySite }) {
  // Absent in every export before schema 45. The site itself is still a place with a faith,
  // a region and a chronicle, so the page stands; only the written account of it is missing.
  const { description } = site;
  const dedicateeDeed =
    description?.dedicateeEventId !== undefined
      ? world.export.events[description.dedicateeEventId]
      : undefined;

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={`Holy site · ${HOLY_SITE_LABELS[site.kind]}`}
        title={site.name}
        meta={
          <>
            <Badge tone={site.settlementId ? 'accent' : 'muted'}>
              {site.settlementId ? 'Within a settlement' : 'Independent location'}
            </Badge>
            {description && <Badge>{SACRED_TRADITION_LABELS[description.tradition]}</Badge>}
          </>
        }
      />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Kind" value={HOLY_SITE_LABELS[site.kind]} />
        <Stat label="Founded" value={site.foundedYear} />
        <Stat label="Scale" value={description?.scale ?? '—'} />
        <Stat
          label="Dedication"
          value={description ? HOLY_SITE_DEDICATION_LABELS[description.dedicationKind] : '—'}
        />
      </div>

      <Panel title="The place">
        {description ? (
          <div className="space-y-5">
            <HolySitePassage
              heading="Dedication"
              text={description.dedication}
              mention={description.dedicateeId}
              evidence={dedicateeDeed}
              world={world}
            />
            <HolySitePassage heading="Style & visuals" text={description.style} />
            <HolySitePassage heading="Atmosphere" text={description.atmosphere} />
            <HolySitePassage heading="Size" text={description.capacity} />
            <HolySitePassage
              heading={description.hasStatue ? 'Statue' : 'Focal point'}
              text={description.focalPoint}
            />
            <HolySitePassage heading="Offering area" text={description.offering} />
          </div>
        ) : (
          <NotInThisExport
            what="A written account of the place"
            since={45}
            version={world.schema.version}
          />
        )}
      </Panel>

      <Panel title="Details">
        <dl>
          <Field label="Faith">
            <EntityLink world={world} id={site.religionId} />
          </Field>
          <Field label="Region">
            <EntityLink world={world} id={site.regionId} />
          </Field>
          <Field label="Location">
            {site.settlementId ? (
              <EntityLink world={world} id={site.settlementId} />
            ) : (
              <span className="text-[var(--ink-faint)]">
                A distinct site outside any settlement
              </span>
            )}
          </Field>
          <Field label="Tradition">
            {description ? SACRED_TRADITION_LABELS[description.tradition] : '—'}
          </Field>
          <Field label="Position">
            <span className="tabular-nums">
              {site.x}, {site.z}
            </span>
          </Field>
        </dl>
      </Panel>

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(site.id)} />
      </Panel>
    </div>
  );
}

function HolySitePassage({
  heading,
  text,
  mention,
  evidence,
  world,
}: {
  heading: string;
  text: string;
  mention?: EntityId;
  evidence?: HistoryEvent;
  world?: World;
}) {
  return (
    <article>
      <h3 className="text-lg font-medium">{heading}</h3>
      <p className="mt-1 text-sm leading-relaxed text-[var(--ink-soft)]">{text}</p>
      {world && mention && (
        <div className="mt-2 space-y-1 text-xs text-[var(--ink-faint)]">
          <div className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
            <span className="font-medium tracking-wide uppercase">Honours</span>
            <EntityLink world={world} id={mention} />
          </div>
          {evidence && (
            <div className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
              <span className="font-medium tracking-wide uppercase">Recorded deed</span>
              <span className="tabular-nums">{evidence.year}</span>
              <NarratedEvent world={world} event={evidence} viewpoint={mention} />
            </div>
          )}
        </div>
      )}
    </article>
  );
}

/** How many settlements followed a faith, year by year. */
function FollowingChart({ world, religion }: { world: World; religion: Religion }) {
  const { startYear, endYear } = world.export.meta;
  const { timeline } = world;

  const series: number[] = [];
  for (let year = startYear; year <= endYear; year++) {
    series.push(timeline.followingAt(religion.id, year));
  }

  const peak = Math.max(1, ...series);
  const width = 300;
  const height = 60;
  const top = 6;

  const x = (year: number) => ((year - startYear) / Math.max(1, endYear - startYear)) * width;
  const y = (count: number) => height - (count / peak) * (height - top);

  const steps = series
    .map((count, index) => `${index === 0 ? 'M' : 'L'}${x(startYear + index)} ${y(count)}`)
    .join('');

  return (
    <div>
      <svg viewBox={`0 0 ${width} ${height}`} className="h-24 w-full" preserveAspectRatio="none">
        <path
          d={`${steps}L${x(endYear)} ${height}L${x(startYear)} ${height}z`}
          fill="var(--accent)"
          fillOpacity={0.2}
        />
        <path
          d={steps}
          fill="none"
          stroke="var(--accent)"
          strokeWidth={1.5}
          vectorEffect="non-scaling-stroke"
        />
      </svg>

      <div className="mt-1 flex flex-wrap items-baseline justify-between gap-x-4 text-xs text-[var(--ink-faint)]">
        <span className="tabular-nums">
          {startYear} – {endYear}
        </span>
        <span className="tabular-nums">{peak} settlements at its height</span>
      </div>
    </div>
  );
}

/**
 * One made thing, and everywhere it has been.
 *
 * The provenance list is the page. An object that was made in one realm, looted into a second
 * and lost when a third burned the place down carries three centuries of history in five lines,
 * and every one of them is a link.
 */
export function ArtifactPage({ world, artifact }: { world: World; artifact: Artifact }) {
  const copies = artifact.tomeContents?.copies ?? [];
  const sections = artifact.tomeContents?.sections ?? [];
  const subjectMarker = artifact.name.indexOf(' of ');
  const briefSubject =
    subjectMarker >= 0
      ? artifact.name.slice(subjectMarker + 4)
      : world.nameOf(artifact.originSettlementId);
  const briefSubjectId =
    artifact.tomeContents?.subjectId ?? uniqueEntityNamed(world, briefSubject);
  const briefSubjectRole = briefSubjectId
    ? (KIND_LABELS[kindOf(briefSubjectId)] ?? 'Entity').toLowerCase()
    : 'named subject';

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={`${ARTIFACT_LABELS[artifact.kind] ?? artifact.kind} · made ${artifact.createdYear}`}
        title={artifact.name}
        meta={
          artifact.lostYear === undefined ? (
            <>
              <Badge tone="accent">Held</Badge>
              <span>
                at <EntityLink world={world} id={artifact.holderId} />
                {artifact.ownerId ? (
                  <>
                    {', claimed by '}
                    <EntityLink world={world} id={artifact.ownerId} />
                  </>
                ) : null}
              </span>
            </>
          ) : (
            <Badge tone="muted">Lost in {artifact.lostYear}</Badge>
          )
        }
      />

      <Panel title="Details">
        <dl>
          <Field label="Made at">
            <EntityLink world={world} id={artifact.originSettlementId} />
          </Field>
          <Field label="Made for">
            <EntityLink world={world} id={artifact.creatorId} />
          </Field>
          {artifact.ownerId && (
            <Field label="Claimed by">
              <EntityLink world={world} id={artifact.ownerId} />
            </Field>
          )}
          {artifact.religionId && (
            <Field label="Sacred to">
              <EntityLink world={world} id={artifact.religionId} />
            </Field>
          )}
          {artifact.tomeContents && (
            <>
              <Field label="Contents">
                {TOME_CONTENT_LABELS[artifact.tomeContents.kind] ?? artifact.tomeContents.kind}
              </Field>
              <Field label="Subject">
                <EntityLink world={world} id={artifact.tomeContents.subjectId} />
              </Field>
              {artifact.tomeContents.contextId && (
                <Field label="Campaign">
                  <EntityLink world={world} id={artifact.tomeContents.contextId} />
                </Field>
              )}
              <Field label="Circulation">
                {(artifact.tomeContents.copyLimit ?? 0) === 0
                  ? 'Unique manuscript'
                  : copies.length === 0
                    ? 'Not yet copied'
                    : `${copies.length} additional settlement ${copies.length === 1 ? 'copy' : 'copies'}`}
              </Field>
            </>
          )}
          <Field label="Changed hands">
            {artifact.provenance.length - 1}{' '}
            {artifact.provenance.length === 2 ? 'time' : 'times'}
          </Field>
        </dl>
      </Panel>

      {artifact.kind === 'Tome' && (
        <Panel title="Contents">
          {sections.length > 0 ? (
            <div className="space-y-5">
              {sections.map((section, index) => (
                <article key={`${section.heading}-${index}`}>
                  <h3 className="text-lg font-medium">
                    {section.heading}
                    {section.year ? (
                      <span className="ml-2 text-xs font-normal text-[var(--ink-faint)]">
                        {section.year}
                      </span>
                    ) : null}
                  </h3>
                  <p className="mt-1 text-sm leading-relaxed text-[var(--ink-soft)]">
                    {section.text}
                  </p>
                  {section.references.length > 0 && (
                    <div className="mt-2 flex flex-wrap items-baseline gap-x-2 gap-y-1 text-xs text-[var(--ink-faint)]">
                      <span className="font-medium tracking-wide uppercase">Mentions</span>
                      {section.references.map((id) => (
                        <EntityLink key={id} world={world} id={id} />
                      ))}
                    </div>
                  )}
                </article>
              ))}
            </div>
          ) : artifact.tomeContents ? (
            <p className="text-sm leading-relaxed text-[var(--ink-soft)]">
              A brief written account concerning the {briefSubjectRole}{' '}
              <EntityLink world={world} id={artifact.tomeContents.subjectId} />.
            </p>
          ) : (
            <p className="text-sm leading-relaxed text-[var(--ink-soft)]">
              A brief written account concerning the {briefSubjectRole}{' '}
              {briefSubjectId ? (
                <>
                  <EntityLink world={world} id={briefSubjectId} />.
                </>
              ) : (
                <>{briefSubject}.</>
              )}
            </p>
          )}
        </Panel>
      )}

      {copies.length > 0 && (
        <Panel title="Circulation">
          <ol className="space-y-1.5 text-sm">
            {copies.map((copy, index) => (
              <li key={`${copy.year}-${copy.settlementId}-${index}`} className="flex items-baseline gap-3">
                <span className="w-14 shrink-0 text-right tabular-nums text-[var(--ink-faint)]">
                  {copy.year}
                </span>
                <span>
                  Copied at <EntityLink world={world} id={copy.settlementId} /> from the exemplar at{' '}
                  <EntityLink world={world} id={copy.sourceSettlementId} />
                </span>
              </li>
            ))}
          </ol>
        </Panel>
      )}

      <Panel title="Provenance">
        <ol className="space-y-1.5 text-sm">
          {artifact.provenance.map((holding, index) => (
            <li key={index} className="flex items-baseline gap-3">
              <span className="w-14 shrink-0 text-right tabular-nums text-[var(--ink-faint)]">
                {holding.year}
              </span>
              <span>
                {holding.settlementId ? (
                  <EntityLink world={world} id={holding.settlementId} />
                ) : (
                  <span className="text-[var(--ink-faint)]">lost</span>
                )}
                {holding.ownerId && (
                  <>
                    {', '}
                    <EntityLink world={world} id={holding.ownerId} />
                  </>
                )}
                <span className="ml-2 text-[var(--ink-faint)]">{holding.how}</span>
              </span>
            </li>
          ))}
        </ol>
      </Panel>

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(artifact.id)} />
      </Panel>
    </div>
  );
}

/** Resolve a legacy book's name-derived subject only when the name is unambiguous. */
function uniqueEntityNamed(world: World, name: string): EntityId | undefined {
  let found: EntityId | undefined;

  for (const id of world.byId.keys()) {
    if (world.nameOf(id) !== name) continue;
    if (found !== undefined) return undefined;
    found = id;
  }

  return found;
}

export function ArtifactTable({ world, artifacts }: { world: World; artifacts: Artifact[] }) {
  const columns: Column<Artifact>[] = [
    {
      key: 'name',
      header: 'Artifact',
      cell: (a) => <EntityLink world={world} id={a.id} />,
      sort: (a) => a.name,
    },
    {
      key: 'kind',
      header: 'Kind',
      cell: (a) => ARTIFACT_LABELS[a.kind] ?? a.kind,
      sort: (a) => a.kind,
    },
    {
      key: 'made',
      header: 'Made',
      cell: (a) => a.createdYear,
      sort: (a) => a.createdYear,
      align: 'right',
    },
    {
      key: 'origin',
      header: 'Made at',
      cell: (a) => <EntityLink world={world} id={a.originSettlementId} />,
      sort: (a) => world.nameOf(a.originSettlementId),
    },
    {
      key: 'held',
      header: 'Held at',
      cell: (a) =>
        a.lostYear === undefined ? (
          <EntityLink world={world} id={a.holderId} />
        ) : (
          <Badge tone="muted">lost {a.lostYear}</Badge>
        ),
      sort: (a) => (a.holderId ? world.nameOf(a.holderId) : '~'),
    },
    {
      key: 'owner',
      header: 'Claimed by',
      cell: (a) =>
        a.ownerId ? (
          <EntityLink world={world} id={a.ownerId} />
        ) : (
          <span className="text-[var(--ink-faint)]">treasury</span>
        ),
      sort: (a) => (a.ownerId ? world.nameOf(a.ownerId) : '~'),
    },
    {
      key: 'moves',
      header: 'Moves',
      cell: (a) => a.provenance.length - 1,
      sort: (a) => a.provenance.length,
      align: 'right',
    },
  ];

  const facets: Facet<Artifact>[] = [
    {
      key: 'kind',
      label: 'Kind',
      options: present(artifacts.map((a) => a.kind)).map((kind) => ({
        value: kind,
        label: ARTIFACT_LABELS[kind] ?? kind,
        match: (a: Artifact) => a.kind === kind,
      })),
    },
    {
      key: 'fate',
      label: 'Fate',
      options: [
        { value: 'held', label: 'Still held', match: (a) => a.lostYear === undefined },
        { value: 'lost', label: 'Lost', match: (a) => a.lostYear !== undefined },
        { value: 'moved', label: 'Changed hands', match: (a) => a.provenance.length > 1 },
        { value: 'sacred', label: 'Sacred to a faith', match: (a) => a.religionId !== undefined },
      ],
    },
  ];

  return (
    <DataTable
      rows={artifacts}
      columns={columns}
      facets={facets}
      searchText={(a) =>
        `${a.name} ${a.kind} ${a.tomeContents?.kind ?? ''} ${
          a.tomeContents?.sections.map((s) => s.text).join(' ') ?? ''
        }`
      }
      placeholder="Search artifacts…"
      initialSort={{ key: 'made' }}
      emptyMessage="Nothing was made here worth remembering."
    />
  );
}

type CultureTitleStyle = {
  office: string;
  titles: readonly string[];
};

/** The culture-specific vocabulary behind the offices recorded on figure pages. */
const CULTURE_OFFICE_STYLES: Record<
  Culture['government'],
  readonly CultureTitleStyle[]
> = {
  Chiefdom: [
    { office: 'Regent', titles: ['Regent'] },
    { office: 'Consort', titles: ["Chief's Wife", "Chief's Husband"] },
    { office: 'Marshal', titles: ['War-leader'] },
    { office: 'High priest', titles: ['Elder'] },
    { office: 'Governor', titles: ['Headman'] },
  ],
  Monarchy: [
    { office: 'Regent', titles: ['Regent'] },
    { office: 'Consort', titles: ['Queen', 'Prince Consort'] },
    { office: 'Marshal', titles: ['Marshal'] },
    { office: 'High priest', titles: ['High Priest'] },
    { office: 'Governor', titles: ['Governor'] },
  ],
  Theocracy: [
    { office: 'Regent', titles: ['Regent'] },
    { office: 'Consort', titles: ['Consort'] },
    { office: 'Marshal', titles: ['Champion'] },
    { office: 'High priest', titles: ['Hierophant'] },
    { office: 'Governor', titles: ['Warden'] },
  ],
  Oligarchy: [
    { office: 'Regent', titles: ['Regent'] },
    { office: 'Consort', titles: ['Consort'] },
    { office: 'Marshal', titles: ['Strategos'] },
    { office: 'High priest', titles: ['High Priest'] },
    { office: 'Governor', titles: ['Eparch'] },
  ],
  Republic: [
    { office: 'Regent', titles: ['Regent'] },
    { office: 'Consort', titles: ['Consort'] },
    { office: 'Marshal', titles: ['Praetor'] },
    { office: 'High priest', titles: ['Pontifex'] },
    { office: 'Governor', titles: ['Prefect'] },
  ],
};

export function CulturePage({ world, culture }: { world: World; culture: Culture }) {
  const civs = world.export.civilizations.filter((civ) => civ.cultureId === culture.id);
  const titleStyles: readonly CultureTitleStyle[] = [
    { office: 'Ruler', titles: [culture.rulerTitle] },
    ...CULTURE_OFFICE_STYLES[culture.government],
  ];

  // No event kind currently references a culture, so this is normally empty. Rendered
  // conditionally rather than removed, since later milestones may add culture-level events.
  const cultureEvents = world.eventsFor(culture.id);

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={`Culture · ${culture.government}`}
        title={culture.name}
        meta={<Badge>Rulers styled &ldquo;{culture.rulerTitle}&rdquo;</Badge>}
      />

      <Panel title="Details">
        <dl>
          <Field label="Government">{culture.government}</Field>
          <Field label="Succession">
            {SUCCESSION_LABELS[culture.successionLaw] ?? culture.successionLaw}
          </Field>
          <Field label="Term">
            {culture.termYears > 0 ? (
              `${culture.termYears} years, then the office is filled again`
            ) : (
              <span className="text-[var(--ink-faint)]">Held for life</span>
            )}
          </Field>
        </dl>
      </Panel>

      <Panel title="Titles">
        <dl>
          {titleStyles.map(({ office, titles }) => (
            <Field key={office} label={office}>
              {titles.join(' / ')}
            </Field>
          ))}
        </dl>
      </Panel>

      <div className="grid gap-5 lg:grid-cols-2">
        <Panel title="Cultural values">
          <Dials dials={valueDials(culture)} />
          <p className="mt-3 text-xs leading-relaxed text-[var(--ink-faint)]">
            Fixed at worldgen and read by the systems rather than hard-coded into them. What a
            realm is actually governed by is this, moved toward its ruler and its recent past —
            see any of its civilizations below.
          </p>
        </Panel>
        <Panel title="Naming language">
          <LexiconPanel culture={culture} />
        </Panel>
      </div>

      <Panel title="Civilizations">
        <ul className="space-y-1 text-sm">
          {civs.map((civ) => (
            <li key={civ.id}>
              <EntityLink world={world} id={civ.id} />
              <span className="ml-2 text-[var(--ink-faint)]">
                {yearRange(civ.foundedYear, civ.endedYear)}
              </span>
            </li>
          ))}
        </ul>
      </Panel>

      {cultureEvents.length > 0 && (
        <Panel title="Chronicle">
          <EventList world={world} events={cultureEvents} />
        </Panel>
      )}
    </div>
  );
}

/**
 * Shows how a culture's names are built.
 *
 * The corpus blend and sound shifts are the whole recipe, and seeing them next to sample
 * output is what makes an invented language legible rather than just decorative — you can
 * read "slavic + semitic, b→p" and then see Ekallatograd and understand where it came from.
 */
function LexiconPanel({ culture }: { culture: Culture }) {
  const { lexicon } = culture;

  if (!lexicon || lexicon.sources.length === 0) {
    return (
      <p className="text-sm text-[var(--ink-faint)]">
        This world was generated with placeholder names.
      </p>
    );
  }

  const total = lexicon.sources.reduce((sum, s) => sum + s.weight, 0);

  return (
    <div className="space-y-3 text-sm">
      <div>
        <div className="mb-1.5 text-[0.7rem] font-medium tracking-wide uppercase text-[var(--ink-faint)]">
          Roots
        </div>
        <div className="space-y-1">
          {lexicon.sources.map((source) => (
            <div key={source.family} className="flex items-center gap-2">
              <span className="w-20 shrink-0 capitalize">{source.family}</span>
              <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-[var(--rule)]">
                <div
                  className="h-full rounded-full bg-[var(--accent)]"
                  style={{ width: `${Math.round((source.weight / total) * 100)}%` }}
                />
              </div>
            </div>
          ))}
        </div>
      </div>

      {lexicon.soundShifts.length > 0 && (
        <div>
          <div className="mb-1.5 text-[0.7rem] font-medium tracking-wide uppercase text-[var(--ink-faint)]">
            Sound shifts
          </div>
          <div className="flex flex-wrap gap-1">
            {lexicon.soundShifts.map((shift) => (
              <code
                key={shift}
                className="rounded border border-[var(--rule)] px-1.5 py-0.5 font-mono text-xs"
              >
                {shift}
              </code>
            ))}
          </div>
        </div>
      )}

      <div>
        <div className="mb-1 text-[0.7rem] font-medium tracking-wide uppercase text-[var(--ink-faint)]">
          The language would also produce
        </div>
        <p className="text-[var(--ink-soft)]">{lexicon.sampleNames.join(' · ')}</p>
        <p className="mt-0.5 text-[var(--ink-soft)]">{lexicon.samplePlaces.join(' · ')}</p>
      </div>
    </div>
  );
}

function faithDials(character: FaithCharacter): Dial[] {
  return [
    {
      label: 'Fervour',
      value: character.fervour,
      hint: 'How hard it presses outwards',
    },
    {
      label: 'Zealotry',
      value: character.zealotry,
      hint: 'How hard a congregation defends what it already believes',
    },
    {
      label: 'Tolerance',
      value: character.tolerance,
      hint: 'Whether it will coexist rather than overwrite a neighbour',
    },
    {
      label: 'Schism',
      value: character.schismProneness,
      hint: 'How readily a large congregation splits',
    },
    {
      label: 'Syncretism',
      value: character.syncretism,
      hint: 'How readily it treats a neighbour’s faith as kin',
    },
  ];
}

/**
 * The six dials, in the order the engine declares them.
 *
 * One table drives a culture's own values, the values a realm is actually governed by, and a
 * person's disposition — because they are the same six dials, and a reader comparing a king
 * against his people should not have to check that the rows line up.
 */
const VALUE_DIALS: [label: string, key: keyof Values, hint: string][] = [
  ['Aggression', 'aggression', 'How readily it reaches for war'],
  ['Expansionism', 'expansionism', 'How hard it presses at its borders'],
  ['Piety', 'piety', 'How much weight it gives its faith'],
  ['Tradition', 'tradition', 'How tightly it holds to what it has always done'],
  ['Mercantile', 'mercantile', 'How much it lives by trade'],
  ['Learning', 'learning', 'How much it writes, copies and keeps'],
];

/** Dials for one set of values, optionally ticked against another set of the same six. */
function valueDials(values: Values, against?: Values): Dial[] {
  return VALUE_DIALS.map(([label, key, hint]) => ({
    label,
    value: values[key],
    against: against?.[key],
    hint,
  }));
}

/**
 * A person's own inclinations: the culture's six, plus one their culture has no reading of.
 *
 * Centralism is rolled around what the office itself invites rather than around the people —
 * a chief has few instruments to appoint with and a hierarch has many — so it is shown
 * without a tick rather than against a baseline that does not exist.
 */
function dispositionDials(disposition: Disposition, against?: Values): Dial[] {
  return [
    ...valueDials(disposition, against),
    {
      label: 'Centralism',
      value: disposition.centralism,
      hint: 'How much they insist on deciding things themselves',
    },
    {
      label: 'Independence',
      value: disposition.independence,
      against: against ? 0.30 - against.tradition * 0.10 : undefined,
      hint: 'Follower at one end, rebel at the other. Followers are the common case',
    },
  ];
}

/** The four decaying measures of what a realm or a place has lately been through. */
function fortuneDials(fortunes: Fortunes): Dial[] {
  return [
    {
      label: 'Weariness',
      value: fortunes.weariness,
      hint: 'Bled and knows it — damps aggression, inclines it to trade. Halves in twelve years',
    },
    {
      label: 'Calamity',
      value: fortunes.calamity,
      hint: 'Hurt by something it cannot fight — damps expansion, drives it to the temple',
    },
    { label: 'Triumph', value: fortunes.triumph, hint: 'It is going well, and everyone can feel it' },
    {
      label: 'Grievance',
      value: fortunes.grievance,
      hint: 'Ground lost and not recovered. Halves in twenty-five years, so it outlives the exhaustion',
    },
  ];
}

export function HolySiteTable({ world, sites }: { world: World; sites: HolySite[] }) {
  const columns: Column<HolySite>[] = [
    {
      key: 'name',
      header: 'Name',
      cell: (site) => <EntityLink world={world} id={site.id} />,
      sort: (site) => site.name,
    },
    {
      key: 'kind',
      header: 'Kind',
      cell: (site) => HOLY_SITE_LABELS[site.kind],
      sort: (site) => site.kind,
    },
    {
      key: 'dedication',
      header: 'Dedication',
      cell: (site) =>
        site.description ? HOLY_SITE_DEDICATION_LABELS[site.description.dedicationKind] : '—',
      sort: (site) => site.description?.dedicationKind ?? '',
    },
    {
      key: 'tradition',
      header: 'Tradition',
      cell: (site) => (site.description ? SACRED_TRADITION_LABELS[site.description.tradition] : '—'),
      sort: (site) => site.description?.tradition ?? '',
    },
    {
      key: 'faith',
      header: 'Faith',
      cell: (site) => <EntityLink world={world} id={site.religionId} />,
      sort: (site) => world.nameOf(site.religionId),
    },
    {
      key: 'location',
      header: 'Location',
      cell: (site) =>
        site.settlementId ? (
          <EntityLink world={world} id={site.settlementId} />
        ) : (
          <span>
            <EntityLink world={world} id={site.regionId} />
            <Badge tone="muted">independent</Badge>
          </span>
        ),
      sort: (site) => world.nameOf(site.settlementId ?? site.regionId),
    },
    {
      key: 'founded',
      header: 'Founded',
      cell: (site) => site.foundedYear,
      sort: (site) => site.foundedYear,
      align: 'right',
    },
  ];

  const facets: Facet<HolySite>[] = [
    {
      key: 'setting',
      label: 'Setting',
      options: [
        { value: 'settlement', label: 'Within settlements', match: (site) => site.settlementId !== undefined },
        { value: 'independent', label: 'Independent sites', match: (site) => site.settlementId === undefined },
      ],
    },
    {
      key: 'kind',
      label: 'Kind',
      options: present(sites.map((site) => site.kind)).map((kind) => ({
        value: kind,
        label: HOLY_SITE_LABELS[kind],
        match: (site: HolySite) => site.kind === kind,
      })),
    },
    {
      key: 'tradition',
      label: 'Tradition',
      options: present(sites.map((site) => site.description?.tradition)).map((tradition) => ({
        value: tradition,
        label: SACRED_TRADITION_LABELS[tradition],
        match: (site: HolySite) => site.description?.tradition === tradition,
      })),
    },
    {
      key: 'dedication',
      label: 'Dedication',
      options: present(sites.map((site) => site.description?.dedicationKind)).map(
        (kind: HolySiteDedicationKind) => ({
          value: kind,
          label: HOLY_SITE_DEDICATION_LABELS[kind],
          match: (site: HolySite) => site.description?.dedicationKind === kind,
        }),
      ),
    },
  ];

  return (
    <DataTable
      rows={sites}
      columns={columns}
      facets={facets}
      searchText={(site) =>
        `${site.name} ${site.kind} ${site.description?.tradition ?? ''} ${site.description?.dedicationKind ?? ''} ${site.description?.dedication ?? ''} ${site.description?.style ?? ''} ${world.nameOf(site.religionId)} ${world.nameOf(site.settlementId ?? site.regionId)}`
      }
      placeholder="Search holy sites…"
      initialSort={{ key: 'founded', descending: false }}
      emptyMessage="No holy site was established here."
    />
  );
}

export function SettlementTable({
  world,
  settlements,
}: {
  world: World;
  settlements: Settlement[];
}) {
  const columns: Column<Settlement>[] = [
    {
      key: 'name',
      header: 'Name',
      cell: (s) => (
        <span className="flex items-center gap-1.5">
          <EntityLink world={world} id={s.id} />
          {s.isCapital && <Badge tone="accent">seat</Badge>}
        </span>
      ),
      sort: (s) => s.name,
    },
    { key: 'tier', header: 'Tier', cell: (s) => s.tier, sort: (s) => s.population },
    {
      key: 'trade',
      header: 'Known for',
      cell: (s) => (
        <span className={s.specialization === 'None' ? 'text-[var(--ink-faint)]' : ''}>
          {SPECIALIZATION_LABELS[s.specialization] ?? s.specialization}
        </span>
      ),
      sort: (s) => s.specialization,
    },
    {
      key: 'population',
      header: 'Population',
      cell: (s) => s.population.toLocaleString(),
      sort: (s) => s.population,
      align: 'right',
    },
    {
      key: 'founded',
      header: 'Founded',
      cell: (s) => s.foundedYear,
      sort: (s) => s.foundedYear,
      align: 'right',
    },
    {
      key: 'status',
      header: 'Status',
      cell: (s) =>
        s.abandonedYear === undefined ? (
          <span className="text-[var(--ink-faint)]">—</span>
        ) : (
          <Badge tone="muted">abandoned {s.abandonedYear}</Badge>
        ),
      sort: (s) => s.abandonedYear ?? Number.MAX_SAFE_INTEGER,
    },
  ];

  const facets: Facet<Settlement>[] = [
    {
      key: 'status',
      label: 'Status',
      options: [
        { value: 'standing', label: 'Standing', match: (s) => s.abandonedYear === undefined },
        { value: 'gone', label: 'Abandoned', match: (s) => s.abandonedYear !== undefined },
        { value: 'seat', label: 'Seat of a realm', match: (s) => s.isCapital },
        { value: 'walled', label: 'Walled', match: (s) => s.isFortified },
      ],
    },
    {
      key: 'tier',
      label: 'Tier',
      options: TIER_ORDER.map((tier) => ({
        value: tier,
        label: tier,
        match: (s: Settlement) => s.tier === tier,
      })),
    },
    {
      key: 'trade',
      label: 'Known for',
      options: present(settlements.map((s) => s.specialization)).map((specialization) => ({
        value: specialization,
        label: SPECIALIZATION_LABELS[specialization] ?? specialization,
        match: (s: Settlement) => s.specialization === specialization,
      })),
    },
    ...realmFacet(world, settlements, (s) => [s.civilizationId]),
  ];

  return (
    <DataTable
      rows={settlements}
      columns={columns}
      facets={facets}
      searchText={(s) => `${s.name} ${s.tier} ${s.specialization}`}
      placeholder="Search settlements…"
      initialSort={{ key: 'population', descending: true }}
    />
  );
}

/**
 * A "whose?" facet, offered only when the rows actually span more than one realm.
 *
 * The same tables serve a global index and a single realm's page. Rather than a flag at every
 * call site, the filter simply does not appear when it would have one option.
 */
function realmFacet<T>(
  world: World,
  rows: T[],
  civsOf: (row: T) => (EntityId | undefined)[],
  label = 'Realm',
): Facet<T>[] {
  const involved = new Set<EntityId>();
  for (const row of rows) {
    for (const id of civsOf(row)) {
      if (id !== undefined) involved.add(id);
    }
  }

  if (involved.size < 2) return [];

  return [
    {
      key: 'realm',
      label,
      options: world.export.civilizations
        .filter((civ) => involved.has(civ.id))
        .map((civ) => ({
          value: civ.id,
          label: civ.name,
          match: (row: T) => civsOf(row).includes(civ.id),
        })),
    },
  ];
}

/** The distinct values actually present, in first-seen order, minus the "none" placeholder. */
/**
 * The distinct values worth offering as a filter.
 *
 * Drops `undefined` as well as the two "no answer" members, because an export older than the
 * field being faceted on has neither — see `compat.ts`. A facet offering `undefined` renders
 * a blank checkbox that matches nothing.
 */
function present<T extends string>(values: (T | undefined)[]): T[] {
  return [...new Set(values)].filter(
    (value): value is T => value !== undefined && value !== 'None' && value !== 'Unknown',
  );
}

// ---------------------------------------------------------------------------
// Wars and battles
// ---------------------------------------------------------------------------

function battleStamp(year: number, day: number): string {
  return day === 0 ? `${year}` : `${year}.${day}`;
}

function battleSpan(battle: Battle): string {
  const began = battleStamp(battle.year, battle.day);
  if (battle.endYear === undefined || battle.endDay === undefined) return `${began}–`;

  const ended = battleStamp(battle.endYear, battle.endDay);
  return began === ended ? began : `${began}–${ended}`;
}

function battleOutcome(world: World, battle: Battle): string {
  if (!battle.wasSiege) return `${world.nameOf(battle.victorId)} prevailed`;

  switch (battle.siegeOutcome) {
    case 'Carried':
      return `${world.nameOf(battle.attackerId)} carried the siege`;
    case 'Relieved':
      return `${world.nameOf(battle.defenderId)} relieved the siege`;
    case 'Lifted':
      return 'Siege lifted';
    default:
      return 'Siege ongoing';
  }
}

/**
 * A war: who fought it, where it was decided, and what it moved.
 *
 * The battle list is the spine of the page, because a war is only legible through the
 * engagements that decided it — a name, two coalitions and an outcome say nothing about
 * whether the thing was a border skirmish or twenty years at the same walls.
 */
export function WarPage({ world, war }: { world: World; war: War }) {
  const battles = battlesOf(world, war);
  const events = world.eventsFor(war.id);
  const dead = war.attackerLosses + war.defenderLosses;
  const victor = victorOf(war);
  const duration = (war.endYear ?? world.export.meta.endYear) - war.startYear;
  const sieges = battles.filter((battle) => battle.wasSiege).length;
  const fieldBattles = battles.length - sieges;

  return (
    <div className="space-y-5 pb-2">
      <PageTitle
        eyebrow={CAUSE_LABELS[war.cause] ?? 'War'}
        title={war.name}
        meta={
          <>
            <Badge tone={war.outcome === 'Ongoing' ? 'accent' : 'muted'}>
              {OUTCOME_LABELS[war.outcome] ?? war.outcome}
            </Badge>
            <span className="text-[var(--ink-faint)]">
              {yearRange(war.startYear, war.endYear)}
            </span>
          </>
        }
      />

      <div className="grid gap-5 xl:grid-cols-[minmax(0,1.28fr)_minmax(24rem,0.92fr)] xl:items-start">
        <div className="min-w-0 space-y-5">
          <section className="he-war-metrics grid grid-cols-2 overflow-hidden rounded-lg border border-[var(--rule)] bg-[var(--panel)] sm:grid-cols-4">
            <WarMetric
              icon={<IconSwords />}
              label="Battles"
              value={battles.length}
            />
            <WarMetric
              icon={<IconPeople />}
              label="Dead"
              value={dead.toLocaleString()}
              tone="danger"
              hint="Across both coalitions"
            />
            <WarMetric
              icon={<IconCity />}
              label="Territory"
              value={war.cededRegionIds.length}
              unit={war.cededRegionIds.length === 1 ? 'region' : 'regions'}
              hint="Changed hands at the peace"
            />
            <WarMetric
              icon={<IconClock />}
              label="Length"
              value={`${duration}y`}
              tone="accent"
            />
          </section>

          <Panel title="Belligerents">
            <dl>
              <Field label="Declared by">
                <EntityLink world={world} id={war.aggressorId} />
              </Field>
              <Field label="Declared on">
                <EntityLink world={world} id={war.defenderId} />
              </Field>
              {war.claimedRelicId && (
                <Field label="Relic sought">
                  <EntityLink world={world} id={war.claimedRelicId} />
                </Field>
              )}
              {war.aggressorReligionId && war.defenderReligionId && (
                <Field label="Faiths">
                  <EntityLink world={world} id={war.aggressorReligionId} />
                  {' against '}
                  <EntityLink world={world} id={war.defenderReligionId} />
                </Field>
              )}
              {war.attackers.length > 1 && (
                <Field label="Attacking">
                  <Coalition world={world} ids={war.attackers} />
                </Field>
              )}
              {war.defenders.length > 1 && (
                <Field label="Defending">
                  <Coalition world={world} ids={war.defenders} />
                </Field>
              )}
              <Field label="Outcome">
                {OUTCOME_LABELS[war.outcome] ?? war.outcome}
                {victor && (
                  <>
                    {' — '}
                    <EntityLink world={world} id={war.attackers.includes(victor) ? war.aggressorId : war.defenderId} />
                  </>
                )}
              </Field>
              {war.cededRegionIds.length > 0 && (
                <Field label="Ceded">
                  <Coalition world={world} ids={war.cededRegionIds} />
                </Field>
              )}
            </dl>
          </Panel>

          <Panel title="Battles">
            {battles.length === 0 ? (
              <p className="p-4 text-sm text-[var(--ink-faint)]">
                The two sides never came within reach of each other.
              </p>
            ) : (
              <BattleTable world={world} battles={battles} />
            )}
          </Panel>

          <Panel title="Context & consequences">
            <div className="grid gap-0 md:grid-cols-3 md:divide-x md:divide-[var(--rule)]">
              <WarConsequence
                eyebrow="The peace"
                text={
                  war.cededRegionIds.length === 0
                    ? 'No region changed hands when the fighting ended.'
                    : `${war.cededRegionIds.length.toLocaleString()} ${war.cededRegionIds.length === 1 ? 'region changed' : 'regions changed'} hands at the peace.`
                }
              />
              <WarConsequence
                eyebrow="The cost"
                text={`${war.attackerLosses.toLocaleString()} fell among the attackers and ${war.defenderLosses.toLocaleString()} among the defenders.`}
              />
              <WarConsequence
                eyebrow="The campaign"
                text={`${counted(sieges, 'siege')} and ${counted(fieldBattles, 'field battle')} were recorded across ${duration} ${duration === 1 ? 'year' : 'years'}.`}
              />
            </div>
          </Panel>
        </div>

        <div className="min-w-0">
          <Panel title="Chronicle">
            <EventList world={world} events={events} timeline />
          </Panel>
        </div>
      </div>
    </div>
  );
}

function WarMetric({
  icon,
  label,
  value,
  unit,
  hint,
  tone = 'neutral',
}: {
  icon: ReactNode;
  label: string;
  value: ReactNode;
  unit?: string;
  hint?: string;
  tone?: 'neutral' | 'accent' | 'danger';
}) {
  const valueTone = {
    neutral: 'text-[var(--ink)]',
    accent: 'text-[var(--primary)]',
    danger: 'text-[var(--error)]',
  }[tone];

  return (
    <div
      title={hint}
      className="he-war-metric flex min-h-28 flex-col items-center justify-center px-3 py-4 text-center"
    >
      <span className="mb-1.5 text-xl text-[var(--ink-soft)]">{icon}</span>
      <span className={`he-data text-2xl ${valueTone}`}>{value}</span>
      <span className="he-label mt-1 text-[10px]">{label}</span>
      {unit && <span className="text-[11px] text-[var(--ink-faint)]">{unit}</span>}
    </div>
  );
}

function WarConsequence({ eyebrow, text }: { eyebrow: string; text: string }) {
  return (
    <div className="border-b border-[var(--rule)] px-1 py-3 first:pt-0 last:border-b-0 last:pb-0 md:border-b-0 md:px-5 md:py-0 md:first:pl-0 md:last:pr-0">
      <div className="he-label mb-2 text-[10px]">{eyebrow}</div>
      <p className="text-sm leading-relaxed text-[var(--ink-soft)]">{text}</p>
    </div>
  );
}

function counted(value: number, noun: string): string {
  return `${value.toLocaleString()} ${noun}${value === 1 ? '' : 's'}`;
}

/** The winning coalition's principal, or undefined when nobody won. */
function victorOf(war: War): EntityId | undefined {
  if (war.outcome === 'AggressorVictory') return war.aggressorId;
  if (war.outcome === 'DefenderVictory') return war.defenderId;
  return undefined;
}

function presentAt(world: World, battleId: EntityId) {
  const rows: { figure: Figure; campaign: Campaign }[] = [];
  for (const figure of world.export.figures) {
    for (const campaign of figure.campaigns ?? []) {
      if (campaign.battleId === battleId) rows.push({ figure, campaign });
    }
  }
  return rows;
}

function Coalition({ world, ids }: { world: World; ids: EntityId[] }) {
  return (
    <span className="flex flex-wrap gap-x-2 gap-y-1">
      {ids.map((id, index) => (
        <span key={id}>
          <EntityLink world={world} id={id} />
          {index < ids.length - 1 && <span className="text-[var(--ink-faint)]">,</span>}
        </span>
      ))}
    </span>
  );
}

/**
 * One engagement.
 *
 * Commanders are the reason this is a page rather than a row: a ruler who led in person
 * links a battle straight into the family tree, and a ruler who died at one is how a
 * house ends.
 */
export function BattlePage({ world, battle }: { world: World; battle: Battle }) {
  const war = warOf(world, battle.warId);
  const present = presentAt(world, battle.id);

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={war ? war.name : battle.wasSiege ? 'Siege' : 'Battle'}
        title={battle.name}
        meta={
          <>
            <Badge tone={battle.siegeOutcome === 'Ongoing' ? 'accent' : 'muted'}>
              {battleOutcome(world, battle)}
            </Badge>
            {battle.wasSiege && <Badge>Siege</Badge>}
            {battle.sacked && <Badge tone="muted">Sacked</Badge>}
            <span className="text-[var(--ink-faint)]">{battleSpan(battle)}</span>
          </>
        }
      />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat
          label="Attacker"
          value={battle.attackerStrength.toLocaleString()}
          hint={`${world.nameOf(battle.attackerId)} · ${battle.attackerLosses.toLocaleString()} lost`}
        />
        <Stat
          label="Defender"
          value={battle.defenderStrength.toLocaleString()}
          hint={`${world.nameOf(battle.defenderId)} · ${battle.defenderLosses.toLocaleString()} lost`}
        />
        <Stat label="Dead" value={(battle.attackerLosses + battle.defenderLosses).toLocaleString()} />
        <Stat label="When" value={battleSpan(battle)} />
      </div>

      <Panel title="Details">
        <dl>
          <Field label="War">
            <EntityLink world={world} id={battle.warId} />
          </Field>
          <Field label="Attacker">
            <EntityLink world={world} id={battle.attackerId} />
          </Field>
          <Field label="Defender">
            <EntityLink world={world} id={battle.defenderId} />
          </Field>
          {battle.wasSiege && <Field label="Outcome">{battleOutcome(world, battle)}</Field>}
          <Field label="Ground">
            <EntityLink world={world} id={battle.regionId} />
          </Field>
          {battle.settlementId && (
            <Field label={battle.wasSiege ? 'Besieged' : 'Fought over'}>
              <EntityLink world={world} id={battle.settlementId} />
              {battle.sacked && <span className="ml-2 text-[var(--ink-faint)]">put to the sack</span>}
            </Field>
          )}
          {battle.attackerCommanderId && (
            <Field label="Led the attack">
              <EntityLink world={world} id={battle.attackerCommanderId} />
            </Field>
          )}
          {battle.defenderCommanderId && (
            <Field label="Led the defence">
              <EntityLink world={world} id={battle.defenderCommanderId} />
            </Field>
          )}
        </dl>
      </Panel>

      {present.length > 0 && (
        <Panel title="Present">
          <ul className="space-y-1.5 text-sm">
            {present.map(({ figure, campaign }) => (
              <li key={`${figure.id}:${campaign.role}`}>
                <EntityLink world={world} id={figure.id} />
                <span className="ml-2 text-[var(--ink-faint)]">
                  {CAMPAIGN_ROLE_LABELS[campaign.role] ?? campaign.role}
                  {campaign.triumphant === undefined
                    ? ''
                    : campaign.triumphant
                      ? ' · triumphant'
                      : ' · defeated'}
                </span>
              </li>
            ))}
          </ul>
        </Panel>
      )}

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(battle.id)} />
      </Panel>
    </div>
  );
}

export function BattleTable({ world, battles }: { world: World; battles: Battle[] }) {
  const columns: Column<Battle>[] = [
    {
      key: 'name',
      header: 'Battle',
      cell: (b) => (
        <span className="flex items-center gap-1.5">
          <EntityLink world={world} id={b.id} />
          {b.sacked && <Badge tone="muted">sacked</Badge>}
        </span>
      ),
      sort: (b) => b.name,
    },
    {
      key: 'year',
      header: 'When',
      cell: (b) => battleSpan(b),
      sort: (b) => b.year * 1000 + b.day,
      align: 'right',
    },
    {
      key: 'victor',
      header: 'Outcome',
      cell: (b) => battleOutcome(world, b),
      sort: (b) => battleOutcome(world, b),
    },
    {
      key: 'strength',
      header: 'Strength',
      cell: (b) => `${b.attackerStrength.toLocaleString()} / ${b.defenderStrength.toLocaleString()}`,
      sort: (b) => b.attackerStrength + b.defenderStrength,
      align: 'right',
    },
    {
      key: 'losses',
      header: 'Dead',
      cell: (b) => (b.attackerLosses + b.defenderLosses).toLocaleString(),
      sort: (b) => b.attackerLosses + b.defenderLosses,
      align: 'right',
    },
  ];

  const facets: Facet<Battle>[] = [
    {
      key: 'kind',
      label: 'Kind',
      options: [
        { value: 'siege', label: 'Siege', match: (b) => b.wasSiege },
        { value: 'field', label: 'Field battle', match: (b) => !b.wasSiege },
        { value: 'sack', label: 'Ended in a sacking', match: (b) => b.sacked },
        {
          value: 'royal',
          label: 'A ruler led in person',
          match: (b) => b.attackerCommanderId !== undefined || b.defenderCommanderId !== undefined,
        },
      ],
    },
    {
      key: 'side',
      label: 'Decided for',
      options: [
        {
          value: 'attacker',
          label: 'The attacker',
          match: (b) =>
            b.wasSiege ? b.siegeOutcome === 'Carried' : b.victorId === b.attackerId,
        },
        {
          value: 'defender',
          label: 'The defender',
          match: (b) =>
            b.wasSiege ? b.siegeOutcome === 'Relieved' : b.victorId === b.defenderId,
        },
      ],
    },
    ...realmFacet(world, battles, (b) => [b.attackerId, b.defenderId], 'Fought by'),
  ];

  return (
    <DataTable
      rows={battles}
      columns={columns}
      facets={facets}
      searchText={(b) => `${b.name} ${world.nameOf(b.victorId)}`}
      placeholder="Search battles…"
      initialSort={{ key: 'year' }}
      emptyMessage="No battles."
    />
  );
}

/**
 * A realm's standing with everyone it has met.
 *
 * Directed, so this is one realm's own view and the other side's page will usually
 * disagree — which is the model rather than an inconsistency, and the reason a beaten
 * realm comes back for its province a generation later.
 */
export function DiplomacyPanel({ world, civ }: { world: World; civ: Civilization }) {
  const allies = new Map(civ.allies.map((pact) => [pact.civilizationId, pact.sinceYear]));
  const ranked = [...civ.relations].sort((a, b) => b.opinion - a.opinion);

  if (ranked.length === 0) {
    return (
      <p className="p-4 text-sm text-[var(--ink-faint)]">
        No other realm ever came within reach.
      </p>
    );
  }

  return (
    <ul className="divide-y divide-[var(--rule)]">
      {ranked.map((relation) => (
        <li key={relation.civilizationId} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 py-2 text-sm">
          <span className="min-w-40 flex-1">
            <EntityLink world={world} id={relation.civilizationId} />
          </span>
          <OpinionBar opinion={relation.opinion} />
          <span className="w-28 text-right text-xs text-[var(--ink-faint)]">
            {standingLabel(relation.opinion)}
          </span>
          <span className="flex gap-1.5">
            {allies.has(relation.civilizationId) && (
              <Badge tone="accent">allied since {allies.get(relation.civilizationId)}</Badge>
            )}
            {truceHolds(world, relation) && (
              <Badge tone="muted">truce to {relation.truceUntilYear}</Badge>
            )}
          </span>
        </li>
      ))}
    </ul>
  );
}

function truceHolds(world: World, relation: Relation): boolean {
  return relation.truceUntilYear !== undefined
    && relation.truceUntilYear >= world.export.meta.endYear;
}

/** Diverging from the centre, so hostility and warmth read as opposite directions. */
function OpinionBar({ opinion }: { opinion: number }) {
  const width = Math.min(Math.abs(opinion), 1) * 50;
  const hostile = opinion < 0;

  return (
    <span className="relative hidden h-1.5 w-32 shrink-0 overflow-hidden rounded-full bg-[var(--rule)] sm:block">
      <span
        className={`absolute top-0 h-full ${hostile ? 'bg-[var(--ink-faint)]' : 'bg-[var(--accent)]'}`}
        style={hostile ? { right: '50%', width: `${width}%` } : { left: '50%', width: `${width}%` }}
      />
    </span>
  );
}

function standingLabel(opinion: number): string {
  if (opinion <= -0.6) return 'implacable';
  if (opinion <= -0.3) return 'hostile';
  if (opinion <= -0.1) return 'cold';
  if (opinion < 0.1) return 'indifferent';
  if (opinion < 0.28) return 'cordial';
  return 'friendly';
}

/** Every war a realm was a belligerent in, oldest first. */
export function warsOf(world: World, civId: EntityId): War[] {
  return world.export.wars.filter(
    (war) => war.attackers.includes(civId) || war.defenders.includes(civId),
  );
}

export function WarTable({ world, wars }: { world: World; wars: War[] }) {
  const columns: Column<War>[] = [
    {
      key: 'name',
      header: 'War',
      cell: (w) => <EntityLink world={world} id={w.id} />,
      sort: (w) => w.name,
    },
    {
      key: 'cause',
      header: 'Cause',
      cell: (w) => CAUSE_LABELS[w.cause] ?? w.cause,
      sort: (w) => w.cause,
    },
    {
      key: 'sides',
      header: 'Fought between',
      cell: (w) => (
        <span className="flex flex-wrap items-center gap-1">
          <EntityLink world={world} id={w.aggressorId} />
          <span className="text-[var(--ink-faint)]">v</span>
          <EntityLink world={world} id={w.defenderId} />
          {w.attackers.length + w.defenders.length > 2 && (
            <Badge tone="muted">+{w.attackers.length + w.defenders.length - 2}</Badge>
          )}
        </span>
      ),
      sort: (w) => world.nameOf(w.aggressorId),
    },
    {
      key: 'span',
      header: 'Span',
      cell: (w) => yearRange(w.startYear, w.endYear),
      sort: (w) => w.startYear,
    },
    {
      key: 'battles',
      header: 'Battles',
      cell: (w) => w.battleIds.length,
      sort: (w) => w.battleIds.length,
      align: 'right',
    },
    {
      key: 'outcome',
      header: 'Outcome',
      cell: (w) =>
        w.outcome === 'Stalemate' ? (
          <Badge tone="muted">exhaustion</Badge>
        ) : w.outcome === 'Ongoing' ? (
          <Badge tone="accent">ongoing</Badge>
        ) : (
          <span className="flex items-center gap-1">
            <EntityLink
              world={world}
              id={w.outcome === 'AggressorVictory' ? w.aggressorId : w.defenderId}
            />
            {w.cededRegionIds.length > 0 && (
              <Badge tone="muted">+{w.cededRegionIds.length}</Badge>
            )}
          </span>
        ),
      sort: (w) => w.outcome,
    },
  ];

  const facets: Facet<War>[] = [
    {
      key: 'cause',
      label: 'Cause',
      options: present(wars.map((w) => w.cause)).map((cause) => ({
        value: cause,
        label: CAUSE_LABELS[cause] ?? cause,
        match: (w: War) => w.cause === cause,
      })),
    },
    {
      key: 'outcome',
      label: 'Outcome',
      options: [
        ...present(wars.map((w) => w.outcome)).map((outcome) => ({
          value: outcome,
          label: OUTCOME_LABELS[outcome] ?? outcome,
          match: (w: War) => w.outcome === outcome,
        })),
        // The question the war list exists to answer: which of these actually moved a border?
        { value: 'ceded', label: 'Moved a border', match: (w: War) => w.cededRegionIds.length > 0 },
      ],
    },
    ...realmFacet(world, wars, (w) => [...w.attackers, ...w.defenders], 'Fought by'),
  ];

  return (
    <DataTable
      rows={wars}
      columns={columns}
      facets={facets}
      searchText={(w) => `${w.name} ${w.cause} ${world.nameOf(w.aggressorId)} ${world.nameOf(w.defenderId)}`}
      placeholder="Search wars…"
      initialSort={{ key: 'span' }}
      emptyMessage="This realm was never at war."
    />
  );
}
