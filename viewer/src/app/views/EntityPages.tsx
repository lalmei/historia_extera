import { EventList } from '../components/EventList';
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
import {
  battlesOf,
  cultureOf,
  dynastyOf,
  figureOf,
  figures,
  regionOf,
  settlementOf,
  treasuresOf,
  warOf,
  type World,
} from '../store';
import {
  ARTIFACT_LABELS,
  CAUSE_LABELS,
  DEATH_LABELS,
  OUTCOME_LABELS,
  SPECIALIZATION_LABELS,
  SUCCESSION_LABELS,
  TIER_ORDER,
  type Artifact,
  type Battle,
  type Civilization,
  type Culture,
  type Dynasty,
  type EntityId,
  type Figure,
  type Region,
  type Relation,
  type Religion,
  type Settlement,
  type War,
} from '../types';

/**
 * The entity pages.
 *
 * Every page follows the same shape: identity at the top, structured facts in the
 * middle, and this entity's slice of the chronicle at the bottom — pulled straight
 * from the export's per-entity index, so it costs an array lookup rather than a
 * scan. Every reference is an EntityLink, which is what makes
 * king → dynasty → war → battle → city browsable in the Legends-mode way.
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

      {culture && (
        <div className="grid gap-5 lg:grid-cols-2">
          <Panel title="Cultural values">
            <ValueBars culture={culture} />
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
        <EventList world={world} events={world.eventsFor(civ.id)} />
      </Panel>
    </div>
  );
}

export function SettlementPage({ world, settlement }: { world: World; settlement: Settlement }) {
  const region = regionOf(world, settlement.regionId);
  const treasures = treasuresOf(world, settlement.id);

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

      {treasures.length > 0 && (
        <Panel title="Treasury">
          <ArtifactTable world={world} artifacts={treasures} />
        </Panel>
      )}

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(settlement.id)} />
      </Panel>
    </div>
  );
}

export function FigurePage({ world, figure }: { world: World; figure: Figure }) {
  const age =
    figure.deathYear === undefined
      ? world.export.meta.endYear - figure.birthYear
      : figure.deathYear - figure.birthYear;

  const house = dynastyOf(world, figure.dynastyId);
  const hasFamily =
    figure.motherId !== undefined ||
    figure.fatherId !== undefined ||
    figure.spouseIds.length > 0 ||
    figure.childIds.length > 0;

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={
          house ? `${figure.titles[0]?.title ?? 'Of the house of'} · ${house.name}` : 'Figure'
        }
        title={figure.name}
        meta={
          <>
            <Badge tone={figure.deathYear === undefined ? 'accent' : 'muted'}>
              {figure.deathYear === undefined ? 'Living' : 'Deceased'}
            </Badge>
            <span className="text-[var(--ink-faint)]">
              {yearRange(figure.birthYear, figure.deathYear)} · aged {age}
            </span>
          </>
        }
      />

      <Panel title="Details">
        <dl>
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
          <Field label="Born">
            {figure.birthYear}
            {figure.birthSettlementId && (
              <>
                {' in '}
                <EntityLink world={world} id={figure.birthSettlementId} />
              </>
            )}
          </Field>
          {figure.deathYear !== undefined && (
            <Field label="Died">
              {figure.deathYear}
              {figure.deathCause !== 'Unknown' && (
                <span className="ml-2 text-[var(--ink-faint)]">
                  of {DEATH_LABELS[figure.deathCause] ?? figure.deathCause}
                </span>
              )}
            </Field>
          )}
          <Field label="Titles">
            {figure.titles.length === 0 ? (
              <span className="text-[var(--ink-faint)]">None</span>
            ) : (
              <ul className="space-y-0.5">
                {figure.titles.map((title, index) => (
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

      {hasFamily && (
        <Panel title="Family">
          <FamilyTree world={world} figure={figure} />
        </Panel>
      )}

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(figure.id)} />
      </Panel>
    </div>
  );
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
        const reign = ruler.titles.find((title) => title.title !== 'Regent');

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
          `${f.deathYear}, ${DEATH_LABELS[f.deathCause] ?? f.deathCause}`
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

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Habitability" value={region.habitability.toFixed(2)} />
        <Stat label="Fertility" value={region.fertility.toFixed(2)} />
        <Stat label="Mean height" value={`${Math.round(region.meanHeight)} m`} />
        <Stat label="Extent" value={`${region.width} × ${region.height}`} />
      </div>

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

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(region.id)} />
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

  const offshoots = world.export.religions.filter((r) => r.parentId === religion.id);
  const relics = world.export.artifacts.filter((a) => a.religionId === religion.id);

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
            <span className="text-[var(--ink-faint)]">
              {yearRange(religion.foundedYear, religion.endedYear)}
            </span>
          </>
        }
      />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Settlements" value={following.length} hint="Following it now" />
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
          {artifact.religionId && (
            <Field label="Sacred to">
              <EntityLink world={world} id={artifact.religionId} />
            </Field>
          )}
          <Field label="Changed hands">
            {artifact.provenance.length - 1}{' '}
            {artifact.provenance.length === 2 ? 'time' : 'times'}
          </Field>
        </dl>
      </Panel>

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
      searchText={(a) => `${a.name} ${a.kind}`}
      placeholder="Search artifacts…"
      initialSort={{ key: 'made' }}
      emptyMessage="Nothing was made here worth remembering."
    />
  );
}

export function CulturePage({ world, culture }: { world: World; culture: Culture }) {
  const civs = world.export.civilizations.filter((civ) => civ.cultureId === culture.id);

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

      <div className="grid gap-5 lg:grid-cols-2">
        <Panel title="Cultural values">
          <ValueBars culture={culture} />
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

function ValueBars({ culture }: { culture: Culture }) {
  const values: [string, number][] = [
    ['Aggression', culture.aggression],
    ['Expansionism', culture.expansionism],
    ['Piety', culture.piety],
    ['Tradition', culture.tradition],
    ['Mercantile', culture.mercantile],
  ];

  return (
    <dl className="space-y-2">
      {values.map(([label, value]) => (
        <div key={label} className="flex items-center gap-3">
          <dt className="w-28 shrink-0 text-sm text-[var(--ink-faint)]">{label}</dt>
          <dd className="flex min-w-0 flex-1 items-center gap-2">
            <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-[var(--rule)]">
              <div
                className="h-full rounded-full bg-[var(--accent)]"
                style={{ width: `${Math.round(value * 100)}%` }}
              />
            </div>
            <span className="w-10 text-right text-xs tabular-nums text-[var(--ink-faint)]">
              {value.toFixed(2)}
            </span>
          </dd>
        </div>
      ))}
    </dl>
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
function present<T extends string>(values: T[]): T[] {
  return [...new Set(values)].filter((value) => value !== 'None' && value !== 'Unknown');
}

// ---------------------------------------------------------------------------
// Wars and battles
// ---------------------------------------------------------------------------

/**
 * A war: who fought it, where it was decided, and what it moved.
 *
 * The battle list is the spine of the page, because a war is only legible through the
 * engagements that decided it — a name, two coalitions and an outcome say nothing about
 * whether the thing was a border skirmish or twenty years at the same walls.
 */
export function WarPage({ world, war }: { world: World; war: War }) {
  const battles = battlesOf(world, war);
  const dead = war.attackerLosses + war.defenderLosses;
  const victor = victorOf(war);

  return (
    <div className="space-y-5">
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

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Battles" value={battles.length} />
        <Stat label="Dead" value={dead.toLocaleString()} hint="Across both coalitions" />
        <Stat label="Territory" value={`${war.cededRegionIds.length} regions`} hint="Changed hands at the peace" />
        <Stat
          label="Length"
          value={`${(war.endYear ?? world.export.meta.endYear) - war.startYear}y`}
        />
      </div>

      <Panel title="Belligerents">
        <dl>
          <Field label="Declared by">
            <EntityLink world={world} id={war.aggressorId} />
          </Field>
          <Field label="Declared on">
            <EntityLink world={world} id={war.defenderId} />
          </Field>
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

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(war.id)} />
      </Panel>
    </div>
  );
}

/** The winning coalition's principal, or undefined when nobody won. */
function victorOf(war: War): EntityId | undefined {
  if (war.outcome === 'AggressorVictory') return war.aggressorId;
  if (war.outcome === 'DefenderVictory') return war.defenderId;
  return undefined;
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

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={war ? war.name : battle.wasSiege ? 'Siege' : 'Battle'}
        title={battle.name}
        meta={
          <>
            <Badge tone="accent">{world.nameOf(battle.victorId)} prevailed</Badge>
            {battle.wasSiege && <Badge>Siege</Badge>}
            {battle.sacked && <Badge tone="muted">Sacked</Badge>}
            <span className="text-[var(--ink-faint)]">{battle.year}</span>
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
        <Stat label="Year" value={battle.year} />
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
    { key: 'year', header: 'Year', cell: (b) => b.year, sort: (b) => b.year, align: 'right' },
    {
      key: 'victor',
      header: 'Carried by',
      cell: (b) => <EntityLink world={world} id={b.victorId} />,
      sort: (b) => world.nameOf(b.victorId),
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
        { value: 'attacker', label: 'The attacker', match: (b) => b.victorId === b.attackerId },
        { value: 'defender', label: 'The defender', match: (b) => b.victorId === b.defenderId },
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
