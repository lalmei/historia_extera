import { EventList } from '../components/EventList';
import {
  Badge,
  type Column,
  DataTable,
  EntityLink,
  Field,
  PageTitle,
  Panel,
  Stat,
  yearRange,
} from '../components/common';
import { cultureOf, regionOf, settlementOf, type World } from '../store';
import type { Civilization, Culture, Figure, Region, Settlement } from '../types';

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
          </Field>
          <Field label="Rulers">
            {civ.rulerIds.length === 0 ? (
              <span className="text-[var(--ink-faint)]">None recorded</span>
            ) : (
              <span className="flex flex-wrap gap-x-2 gap-y-1">
                {civ.rulerIds.map((id, index) => (
                  <span key={id}>
                    <EntityLink world={world} id={id} />
                    {index < civ.rulerIds.length - 1 && <span className="text-[var(--ink-faint)]">,</span>}
                  </span>
                ))}
              </span>
            )}
          </Field>
        </dl>
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

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={`${settlement.tier} · ${world.nameOf(settlement.civilizationId)}`}
        title={settlement.name}
        meta={
          <>
            {settlement.isCapital && <Badge tone="accent">Capital</Badge>}
            {settlement.isFortified && <Badge>Fortified</Badge>}
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
        </dl>
      </Panel>

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

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={figure.titles[0]?.title ?? 'Figure'}
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
                <span className="ml-2 text-[var(--ink-faint)]">{figure.deathCause}</span>
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
          {figure.parentIds.length > 0 && (
            <Field label="Parents">
              {figure.parentIds.map((id) => (
                <span key={id} className="mr-2">
                  <EntityLink world={world} id={id} />
                </span>
              ))}
            </Field>
          )}
        </dl>
      </Panel>

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(figure.id)} />
      </Panel>
    </div>
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

      <Panel title="Chronicle">
        <EventList world={world} events={world.eventsFor(culture.id)} />
      </Panel>
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

  return (
    <DataTable
      rows={settlements}
      columns={columns}
      searchText={(s) => `${s.name} ${s.tier}`}
      placeholder="Search settlements…"
      initialSort={{ key: 'population', descending: true }}
    />
  );
}
