import { useLanguage } from '../i18n/LanguageProvider';
import { translateIn } from '../i18n/translations.ts';
import { useState, type ReactNode } from 'react';
import type {
  CompanionRole,
  ExportCompanionPlanet,
  ExportComet,
  ExportCosmology,
  ExportCosmologyCheck,
  ExportGalaxy,
  ExportGiantAppearance,
  ExportSystemMoon,
  ExportTint,
  GalaxyMorphology,
  StarSpectralClass,
  WorldKind,
} from '../types';
import { bandMotion, COMPANION_ROLE_LABELS } from '../types';
import { PageTitle, Panel } from './common';
import type { World } from '../store';
import { NightSky } from './NightSky';

/**
 * The cosmology page: an atlas of the world's system, not a diagnostics dump.
 *
 * The order is the order a reader asks the questions in — what is this world, what system is it
 * in, what does its sky look like, where is that in the galaxy, how did it get there, how big is
 * it all, and how does that compare to the only planet the reader has stood on. The numbers that
 * only settle an argument — the exact figures behind every check — are still all here, but folded
 * away at the bottom so they stop competing with the picture.
 */
export function CosmologyPage({ world }: { world: World }) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const { designation, name, kind, cosmology } = world.export.world;
  const beginning = world.export.events.find((event) => event.kind === 'WorldCreated');
  const seed = world.export.meta.seed;

  if (!cosmology) {
    return (
      <div className="space-y-5">
        <PageTitle eyebrow={designation || name} title={tr("Cosmology")} />
        <Panel title={tr("Cosmology")}>
          <p className="text-sm text-[var(--ink-soft)]">{tr("This export predates stellar-system physics — regenerate the world to see habitable-zone diagnostics.")}</p>
        </Panel>
      </div>
    );
  }

  const c = cosmology;
  const systemName = systemNameOf(designation);

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={designation || name}
        title={tr("Cosmology")}
        meta={
          <span className="text-[var(--ink-faint)]">
            {c.galaxy ? (translateIn(language, 'inline', "{v0} galaxy · ", { v0: tr(morphologyLabel(c.galaxy.morphology)) })) : ''}
            {tr(starLabel(c.starClass))} · {kind === 'Moon' ? tr('habitable moon') : tr('habitable planet')}
          </span>
        }
      />

      <VitalSigns cosmology={c} kind={kind} starAgeGyr={beginning?.data?.starAgeGyr} />

      <SystemSection cosmology={c} kind={kind} name={name} systemName={systemName} />

      {c.galaxy && (
        <div className="grid gap-5 lg:grid-cols-5">
          <div className="lg:col-span-3">
            <Panel title={(translateIn(language, 'inline', "Night sky from {v0}", { v0: name }))}>
              <NightSky galaxy={c.galaxy} seed={seed ?? 0} />
            </Panel>
          </div>
          <div className="lg:col-span-2">
            <GalaxyCard galaxy={c.galaxy} name={name} orientation={c.orientation} />
          </div>
        </div>
      )}

      <CosmicTimeline
        data={beginning?.data}
        startYear={world.export.meta.startYear}
        metallicityFeH={c.galaxy?.location.metallicityFeH}
      />

      <div className="grid gap-5 lg:grid-cols-5">
        <div className="lg:col-span-3">
          <SizeComparison cosmology={c} kind={kind} name={name} />
        </div>
        <div className="space-y-5 lg:col-span-2">
          <EarthComparison cosmology={c} />
          <WhyThisWorldWorks checks={c.checks} isHabitable={c.isHabitable} />
        </div>
      </div>

      <PlanetFaces cosmology={c} kind={kind} name={name} />

      <ScientificDetails cosmology={c} kind={kind} name={name} />
      <GenerationDetails cosmology={c} />
    </div>
  );
}

/** "The planet Aybashel of the Cluain system" → "Cluain". */
function systemNameOf(designation: string | undefined): string | undefined {
  const match = /of the (.+) system/i.exec(designation ?? '');
  return match?.[1];
}

/**
 * The six or seven numbers that answer "what kind of world is this?" before any diagram loads.
 * Everything here is repeated in full further down; this strip exists so a reader never has to
 * scroll to learn the shape of the place.
 */
function VitalSigns({
  cosmology: c,
  kind,
  starAgeGyr,
}: {
  cosmology: ExportCosmology;
  kind: WorldKind;
  starAgeGyr?: string;
}) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const tiles: { value: string; label: string; note?: string }[] = [
    {
      value: (translateIn(language, 'inline', "{v0}-type", { v0: c.starClass })),
      label: tr('Host star'),
      note: `${c.starMassSolar.toFixed(2)} M☉ · ${c.luminositySolar.toFixed(2)} L☉`,
    },
    {
      value: `${c.worldRadiusEarth.toFixed(2)} R⊕`,
      label: kind === 'Moon' ? tr('Moon radius') : tr('Planet radius'),
      note: `${Math.round(c.worldRadiusEarth * EARTH_KM).toLocaleString()} km`,
    },
    {
      value: `${c.worldMassEarth.toFixed(2)} M⊕`,
      label: kind === 'Moon' ? tr('Moon mass') : tr('Planet mass'),
      note: `${c.meanDensityEarth != null ? `${c.meanDensityEarth.toFixed(2)} ρ⊕` : tr('density unrecorded')}`,
    },
    {
      value: `${c.surfaceGravityG.toFixed(2)} g`,
      label: tr('Surface gravity'),
      note: (translateIn(language, 'inline', "escape {v0} km/s", { v0: c.escapeVelocityKmS.toFixed(1) })),
    },
    {
      value: `${Math.round(c.surfaceTempK)} K`,
      label: tr('Surface temp'),
      note: `${(c.surfaceTempK - 273.15).toFixed(0)} °C`,
    },
    {
      value: `${c.orbitalDistanceAu.toFixed(2)} AU`,
      label: kind === 'Moon' ? tr('Parent orbit') : tr('Orbital distance'),
      note: (translateIn(language, 'inline', "HZ {v0}–{v1} AU", { v0: c.habitableZoneInnerAu.toFixed(2), v1: c.habitableZoneOuterAu.toFixed(2) })),
    },
    starAgeGyr
      ? {
          value: `${starAgeGyr} Gyr`,
          label: tr('System age'),
          note: (translateIn(language, 'inline', "of {v0} Gyr on the main sequence", { v0: c.starLifespanGyr.toFixed(1) })),
        }
      : {
          value: `${c.starLifespanGyr.toFixed(1)} Gyr`,
          label: tr('Stellar lifespan'),
          note: tr('main sequence'),
        },
  ];

  return (
    <div className="grid grid-cols-2 gap-px overflow-hidden rounded-lg border border-[var(--rule)] bg-[var(--rule)] sm:grid-cols-4 xl:grid-cols-7">
      {tiles.map((tile) => (
        <div key={tile.label} className="bg-[var(--panel)] px-3 py-2.5">
          <div className="he-data text-[15px] text-[var(--ink)]">{tile.value}</div>
          <div className="he-label mt-1">{tile.label}</div>
          {tile.note && (
            <div className="mt-0.5 truncate text-[11px] text-[var(--ink-faint)]" title={tile.note}>
              {tile.note}
            </div>
          )}
        </div>
      ))}
    </div>
  );
}

/**
 * The system map, given the room its importance deserves. One diagram at a time rather than two
 * crushed side by side: the full system and the habitable belt are the same picture at two
 * scales, and a reader wants whichever one answers the question they are holding.
 */
function SystemSection({
  cosmology: c,
  kind,
  name,
  systemName,
}: {
  cosmology: ExportCosmology;
  kind: WorldKind;
  name: string;
  systemName?: string;
}) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const [mode, setMode] = useState<'system' | 'zone'>('system');
  const companions = c.companions ?? [];
  const comets = c.comets ?? [];
  const moonCount = (c.homeMoons ?? []).length + (c.moons ?? []).length;
  const planetCount = companions.length + (kind === 'Planet' ? 1 : 0);

  const facts = [
    { value: tr(starLabel(c.starClass)), label: `${c.starMassSolar.toFixed(2)} M☉` },
    { value: (translateIn(language, 'inline', "{e0} planets", { e0: planetCount, f0: planetCount, f1: planetCount > 1 ? 's' : '' })), label: (translateIn(language, 'inline', "{v0} giants", { v0: companions.filter((b) => b.appearance).length })) },
    { value: (translateIn(language, 'inline', "{e0} {e1} [components/CosmologyPanel.tsx:217]", { e0: moonCount, e1: moonCount === 1 ? 'moon' : 'moons', f0: moonCount, f1: moonCount > 1 ? 's' : '' })), label: tr('in the system') },
    { value: (translateIn(language, 'inline', "{e0} comets", { e0: comets.length, f0: comets.length, f1: comets.length > 1 ? 's' : '' })), label: tr('tracked') },
    {
      value: c.snowLineAu != null ? `${c.snowLineAu.toFixed(2)} AU` : '—',
      label: tr('snow line'),
    },
    {
      value: `${c.orbitalDistanceAu.toFixed(2)} AU`,
      label: kind === 'Moon' ? (translateIn(language, 'inline', "{v0}'s parent", { v0: name })) : name,
    },
  ];

  return (
    <Panel
      title={systemName ? (translateIn(language, 'inline', "The {v0} system", { v0: systemName })) : tr('The host system')}
      actions={
        <div className="flex gap-1">
          <ViewToggle active={mode === 'system'} onClick={() => setMode('system')}>{tr("Full system")}</ViewToggle>
          <ViewToggle active={mode === 'zone'} onClick={() => setMode('zone')}>{tr("Habitable zone")}</ViewToggle>
        </div>
      }
    >
      <p className="mb-3 text-sm text-[var(--ink-soft)]">
        {kind === 'Moon'
          ? tr('A tidally locked habitable moon orbiting a gas giant inside the star’s liquid-water zone. An outer shepherd giant beyond the snow line keeps leftover rock from raining inward.')
          : tr('A standalone planet inside the host star’s liquid-water habitable zone. A shepherd giant beyond the snow line clears leftover planetesimals so the world is not late-bombarded for gigayears.')}
      </p>

      <SystemView
        cosmology={c}
        kind={kind}
        name={name}
        mode={mode}
        heightClass="h-[19rem] sm:h-[26rem]"
        wide
      />

      <p className="mt-2 text-xs text-[var(--ink-faint)]">
        {mode === 'system'
          ? tr('Fitted to the outermost planet · comet orbits run off the frame · icons are markers, not radii')
          : (translateIn(language, 'inline', "Fitted to the liquid-water belt around this {v0}", { v0: tr(starLabel(c.starClass)) }))}
      </p>

      <div className="mt-4 grid grid-cols-2 gap-3 border-t border-[var(--rule)] pt-3 sm:grid-cols-3 lg:grid-cols-6">
        {facts.map((fact) => (
          <div key={fact.label}>
            <div className="he-data text-sm text-[var(--ink)]">{fact.value}</div>
            <div className="truncate text-[11px] text-[var(--ink-faint)]" title={fact.label}>
              {fact.label}
            </div>
          </div>
        ))}
      </div>

      <details className="mt-3 text-xs text-[var(--ink-soft)]">
        <summary className="cursor-pointer list-none text-[var(--ink-faint)] [&::-webkit-details-marker]:hidden">{tr("▸ What the marks mean")}</summary>
        <MapKey cosmology={c} kind={kind} name={name} />
      </details>
    </Panel>
  );
}

function ViewToggle({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={`rounded px-2.5 py-1 text-xs transition-colors ${
        active
          ? 'bg-[var(--primary)] text-[var(--on-primary)]'
          : 'border border-[var(--rule)] text-[var(--ink-soft)] hover:bg-[var(--hover)]'
      }`}
    >
      {children}
    </button>
  );
}

/** Face-on and edge-on are one picture at two attitudes, so they share one card and one toggle. */
function GalaxyCard({
  galaxy,
  name,
  orientation,
}: {
  galaxy: ExportGalaxy;
  name: string;
  orientation?: ExportCosmology['orientation'];
}) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const [mode, setMode] = useState<'face' | 'edge'>('face');
  const loc = galaxy.location;

  return (
    <Panel
      title={tr("Our place in the galaxy")}
      actions={
        <div className="flex gap-1">
          <ViewToggle active={mode === 'face'} onClick={() => setMode('face')}>{tr("Face-on")}</ViewToggle>
          <ViewToggle active={mode === 'edge'} onClick={() => setMode('edge')}>{tr("Edge-on")}</ViewToggle>
        </div>
      }
    >
      <GalaxyView galaxy={galaxy} mode={mode} />
      <p className="mt-2 text-xs text-[var(--ink-faint)]">
        {mode === 'face'
          ? (translateIn(language, 'inline', "Gold ring is the habitable annulus; the mark is {v0}", { v0: name }))
          : tr('Height against galactocentric radius · same mark')}
      </p>
      <dl className="mt-3 space-y-1.5 border-t border-[var(--rule)] pt-3 text-sm">
        <Diag
          label={tr("Galaxy type")}
          value={(translateIn(language, 'inline', "{e0}{e1}", { e0: tr(morphologyLabel(galaxy.morphology)), e1: galaxy.spiralArmCount > 0 ? `, ${galaxy.spiralArmCount} arms` : '', f0: tr(morphologyLabel(galaxy.morphology)), f1: galaxy.spiralArmCount > 0 ? `, ${galaxy.spiralArmCount} bras` : '' }))}
        />
        <Diag label={tr("Galactic radius")} value={(translateIn(language, 'inline', "{v0} kpc from centre", { v0: loc.galactocentricRadiusKpc.toFixed(1) }))} />
        <Diag label={tr("Height off the plane")} value={`${loc.heightPc.toFixed(0)} pc`} />
        <Diag
          label={tr("Metallicity")}
          value={`[Fe/H] ${loc.metallicityFeH >= 0 ? '+' : ''}${loc.metallicityFeH.toFixed(2)}`}
        />
        <Diag
          label={tr("Position")}
          value={
            galaxy.morphology === 'Elliptical'
              ? tr('old spheroid')
              : loc.inSpiralArm
                ? tr('inside a spiral arm')
                : tr('between the arms')
          }
        />
        {orientation && (
          <Diag
            label={tr("Celestial pole")}
            value={(translateIn(language, 'inline', "{v0}° from the galactic pole · {v1}", { v0: orientation.poleTiltFromGalacticPoleDeg.toFixed(0), v1: tr(bandMotion(orientation.poleTiltFromGalacticPoleDeg)) }))}
          />
        )}
      </dl>
    </Panel>
  );
}

interface Milestone {
  key: string;
  /** Gyr since the Big Bang. */
  t: number;
  tick: string;
  tag: string;
  title: string;
  detail: string;
  future?: boolean;
}

/**
 * Deep time as an axis rather than a card grid.
 *
 * The dots sit at their real place on the age of the universe — enrichment and star formation
 * really are that far apart, and the world's assembly really does follow its star by a hair — with
 * a minimum spacing so two events a hundred million years apart stay separately clickable.
 */
function CosmicTimeline({
  data,
  startYear,
  metallicityFeH,
}: {
  data?: Record<string, string>;
  startYear: number;
  metallicityFeH?: number;
}) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const [selected, setSelected] = useState(0);

  if (
    !data?.universeAgeGyr ||
    !data.galaxyAgeGyr ||
    !data.starAgeGyr ||
    !data.worldAgeGyr ||
    !data.stellarEnrichmentGyr ||
    !data.worldFormationDelayMyr ||
    !data.starRemainingGyr ||
    !data.starNextStage ||
    !data.stellarFuture
  ) {
    return null;
  }

  const iron =
    metallicityFeH == null
      ? tr('metal-rich gas')
      : (translateIn(language, 'inline', "gas at [Fe/H] {v0}{v1}", { v0: metallicityFeH >= 0 ? '+' : '', v1: metallicityFeH.toFixed(2) }));
  const universe = Number(data.universeAgeGyr);
  const remaining = Number(data.starRemainingGyr);
  const since = (lookbackGyr: string) => universe - Number(lookbackGyr);

  const milestones: Milestone[] = [
    {
      key: 'universe',
      t: 0,
      tick: (translateIn(language, 'inline', "{v0} Gya", { v0: data.universeAgeGyr })),
      tag: tr('Universe'),
      title: tr('The universe began'),
      detail:
        tr('Hydrogen and helium came first; the iron and rock needed by this world did not yet exist.'),
    },
    {
      key: 'galaxy',
      t: since(data.galaxyAgeGyr),
      tick: (translateIn(language, 'inline', "{v0} Gya", { v0: data.galaxyAgeGyr })),
      tag: tr('Galaxy'),
      title: tr('The host galaxy began assembling'),
      detail: tr('Its first stars formed from nearly pristine gas while the galaxy was still growing.'),
    },
    {
      key: 'enrichment',
      t: since(data.stellarEnrichmentGyr),
      tick: (translateIn(language, 'inline', "{v0} Gya", { v0: data.stellarEnrichmentGyr })),
      tag: tr('Enrichment'),
      title: tr('Earlier stellar generations lived and died'),
      detail: (translateIn(language, 'inline', "Massive-star explosions and delayed Type Ia supernovae enriched {v0}, supplying the iron and other heavy elements used by the later system.", { v0: iron })),
    },
    {
      key: 'star',
      t: since(data.starAgeGyr),
      tick: (translateIn(language, 'inline', "{v0} Gya", { v0: data.starAgeGyr })),
      tag: tr('Star'),
      title: tr('The host star and its disk formed'),
      detail:
        tr('A metal-enriched molecular cloud collapsed into the star and a surrounding protoplanetary disk.'),
    },
    {
      key: 'world',
      t: since(data.worldAgeGyr),
      tick: (translateIn(language, 'inline', "{v0} Gya", { v0: data.worldAgeGyr })),
      tag: tr('World'),
      title: tr('The history world assembled'),
      detail: (translateIn(language, 'inline', "Its last large accretion followed the star by about {v0} million years.", { v0: data.worldFormationDelayMyr })),
    },
    {
      key: 'history',
      t: universe,
      tick: (translateIn(language, 'inline', "Year {v0}", { v0: startYear })),
      tag: tr('History'),
      title: tr('Recorded history began'),
      detail:
        tr("This is the Chronicle's first year, not the physical creation date of the galaxy, system, or world."),
    },
    {
      key: 'future',
      t: universe + remaining,
      tick: `+${data.starRemainingGyr} Gyr`,
      tag: tr('Ahead'),
      title: (translateIn(language, 'inline', "The host star becomes a {e0}", { e0: data.starNextStage, f0: tr(data.starNextStage) })),
      detail: data.stellarFuture,
      future: true,
    },
  ];

  // The axis runs from the Big Bang to the first recorded year. The star's remaining life is
  // hung off the end in a fixed slot rather than scaled: an M dwarf has two hundred gigayears
  // left, and putting that on the axis would collapse everything that has already happened into
  // the first few pixels.
  const past = milestones.filter((m) => !m.future);
  const positions = [
    ...spaceOut(past.map((m) => 0.05 + (m.t / universe) * 0.72)),
    0.95,
  ];
  const active = milestones[Math.min(selected, milestones.length - 1)];

  return (
    <Panel title={tr("Cosmic timeline")}>
      <p className="mb-5 text-sm text-[var(--ink-soft)]">
        {translateIn(language, 'cosmology', 'Cosmic timeline description', { year: startYear })}
      </p>

      <div className="overflow-x-auto pb-1">
       <div className="relative mx-1 h-[74px] min-w-[52rem]">
        <div
          className="absolute top-[30px] h-px bg-[var(--outline-variant)]"
          style={{
            left: `${positions[0] * 100}%`,
            width: `${(positions[past.length - 1] - positions[0]) * 100}%`,
          }}
        />
        <div
          className="absolute top-[30px] h-px border-t border-dashed border-[var(--rule)]"
          style={{
            left: `${positions[past.length - 1] * 100}%`,
            width: `${(0.95 - positions[past.length - 1]) * 100}%`,
          }}
        />
        {milestones.map((milestone, index) => {
          const isActive = index === (selected % milestones.length);
          return (
            <button
              key={milestone.key}
              type="button"
              onClick={() => setSelected(index)}
              aria-pressed={isActive}
              className="absolute top-0 flex w-20 -translate-x-1/2 flex-col items-center gap-1 text-center"
              style={{ left: `${positions[index] * 100}%` }}
            >
              <span
                className={`he-data text-[11px] leading-none ${
                  isActive ? 'text-[var(--ink)]' : 'text-[var(--ink-faint)]'
                }`}
              >
                {milestone.tick}
              </span>
              <span
                className={`h-2.5 w-2.5 rounded-full border transition-all ${
                  isActive
                    ? 'scale-125 border-[var(--primary)] bg-[var(--primary)]'
                    : milestone.future
                      ? 'border-[var(--tertiary)] bg-[var(--panel)]'
                      : 'border-[var(--outline)] bg-[var(--panel)]'
                }`}
              />
              <span
                className={`text-[11px] leading-tight ${
                  isActive ? 'text-[var(--ink)]' : 'text-[var(--ink-faint)]'
                }`}
              >
                {milestone.tag}
              </span>
            </button>
          );
        })}
       </div>
      </div>

      <div
        className={`mt-3 flex items-start gap-4 rounded-md border p-3 ${
          active.future
            ? 'border-[color-mix(in_srgb,var(--tertiary)_45%,var(--rule))] bg-[color-mix(in_srgb,var(--tertiary)_7%,var(--panel))]'
            : 'border-[var(--rule)] bg-[var(--input)]'
        }`}
      >
        <div className="min-w-0 flex-1">
          <div className="he-label">{active.tick}</div>
          <div className="mt-1 text-sm font-semibold text-[var(--ink)]">{active.title}</div>
          <p className="mt-1 text-xs leading-relaxed text-[var(--ink-soft)]">{active.detail}</p>
        </div>
        <div className="flex shrink-0 gap-1">
          <StepButton
            label={tr("Previous moment")}
            onClick={() => setSelected((i) => (i - 1 + milestones.length) % milestones.length)}
          >
            ‹
          </StepButton>
          <StepButton
            label={tr("Next moment")}
            onClick={() => setSelected((i) => (i + 1) % milestones.length)}
          >
            ›
          </StepButton>
        </div>
      </div>
    </Panel>
  );
}

function StepButton({
  label,
  onClick,
  children,
}: {
  label: string;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={label}
      className="h-7 w-7 rounded border border-[var(--rule)] text-[var(--ink-soft)] transition-colors hover:bg-[var(--hover)]"
    >
      {children}
    </button>
  );
}

/** Keeps points in their true order and near their true place, without letting any two collide. */
function spaceOut(fractions: number[], gap = 0.095): number[] {
  const out = fractions.map((f) => Math.min(0.94, Math.max(0.06, f)));
  for (let i = 1; i < out.length; i++) {
    if (out[i] - out[i - 1] < gap) out[i] = out[i - 1] + gap;
  }
  for (let i = out.length - 1; i > 0; i--) {
    if (out[i] > 0.94) out[i] = 0.94;
    if (out[i] - out[i - 1] < gap) out[i - 1] = out[i] - gap;
  }
  return out;
}

interface SizedBody {
  key: string;
  label: string;
  radiusEarth: number;
  massEarth: number;
  detail: string;
  color: string;
}

/**
 * Every body of the system drawn to one scale on one baseline, so the sizes can be read off the
 * picture instead of the captions. The star cannot join that line — at this scale its disc is
 * wider than the panel — so it is drawn where it honestly belongs: as a limb across the top.
 */
function SizeComparison({
  cosmology: c,
  kind,
  name,
}: {
  cosmology: ExportCosmology;
  kind: WorldKind;
  name: string;
}) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const [metric, setMetric] = useState<'radius' | 'mass'>('radius');
  const moons = c.moons ?? [];
  const homeMoons = c.homeMoons ?? [];
  const comets = c.comets ?? [];
  const habitableMoon = moons.find((moon) => moon.habitable);
  const parentRadius =
    kind === 'Moon' && c.parentGiantMassEarth != null
      ? 2 * Math.sqrt(Math.sqrt(c.parentGiantMassEarth))
      : undefined;

  const bodies: SizedBody[] = [
    ...(c.companions ?? []).map((body) => ({
      key: `comp-${body.role}-${body.semiMajorAxisAu}`,
      label: tr(shortCompanionLabel(body.role)),
      radiusEarth: body.radiusEarth,
      massEarth: body.massEarth,
      detail: `${body.semiMajorAxisAu.toFixed(2)} AU`,
      color: companionColor(body.role),
    })),
    ...(parentRadius != null && c.parentGiantMassEarth != null
      ? [
          {
            key: 'parent',
            label: tr('Parent giant'),
            radiusEarth: parentRadius,
            massEarth: c.parentGiantMassEarth,
            detail: `${c.orbitalDistanceAu.toFixed(2)} AU`,
            color: '#c48a3a',
          },
        ]
      : []),
    ...(kind === 'Planet' || !habitableMoon
      ? [
          {
            key: 'world',
            label: name,
            radiusEarth: c.worldRadiusEarth,
            massEarth: c.worldMassEarth,
            detail: tr('this world'),
            color: '#4ade80',
          },
        ]
      : []),
    ...moons.map((moon) => ({
      key: `moon-${moon.index}`,
      label: moon.habitable ? name : (translateIn(language, 'inline', "Moon {v0}", { v0: moon.name ?? moon.index })),
      radiusEarth: moon.radiusEarth,
      massEarth: moon.massEarth,
      detail: moon.habitable ? tr('this world') : tr('of the parent'),
      color: moon.habitable ? '#4ade80' : '#94a3b8',
    })),
    ...homeMoons.map((moon) => ({
      key: `home-moon-${moon.index}`,
      label: (translateIn(language, 'inline', "Moon {v0}", { v0: moon.name ?? moon.index })),
      radiusEarth: moon.radiusEarth,
      massEarth: moon.massEarth,
      detail: (translateIn(language, 'inline', "of {v0}", { v0: name })),
      color: '#94a3b8',
    })),
    ...(comets.length > 0
      ? [
          {
            key: 'comets',
            label: (translateIn(language, 'inline', "Comets ×{v0}", { v0: comets.length })),
            radiusEarth:
              Math.max(...comets.map((comet) => comet.nucleusRadiusKm)) / EARTH_KM,
            massEarth: Math.max(...comets.map((comet) => comet.massEarth)),
            detail: (translateIn(language, 'inline', "largest {v0} km", { v0: Math.max(...comets.map((comet) => comet.nucleusRadiusKm)).toFixed(1) })),
            color: '#cbd5e1',
          },
        ]
      : []),
  ].sort((a, b) => metricOf(b, metric) - metricOf(a, metric));

  const width = 640;
  const height = 200;
  const baseline = 150;
  const cell = width / Math.max(1, bodies.length);
  const largest = Math.max(...bodies.map((body) => metricOf(body, metric)), 1e-9);
  const maxPx = Math.min(46, cell * 0.44);
  const scale = maxPx / largest;
  const starEarth = (c.starRadiusSolar ?? 1) * 109.2;
  const starMassEarth = (c.starMassSolar ?? 1) * EARTH_MASSES_PER_SOLAR;
  const starPx =
    scale * (metric === 'radius' ? starEarth : Math.sqrt(starMassEarth));
  const starWidths = (2 * starPx) / width;

  return (
    <Panel
      title={tr("Size and mass, to scale")}
      actions={
        <div className="flex gap-1">
          <ViewToggle active={metric === 'radius'} onClick={() => setMetric('radius')}>{tr("Radius")}</ViewToggle>
          <ViewToggle active={metric === 'mass'} onClick={() => setMetric('mass')}>{tr("Mass")}</ViewToggle>
        </div>
      }
    >
      <svg
        viewBox={`0 0 ${width} ${height}`}
        className="w-full rounded-md border border-[var(--rule)] bg-[var(--canvas)]"
        role="img"
        aria-label={(translateIn(language, 'inline', "Every body of the system compared by {e0}", { e0: metric, f0: metric === 'radius' ? 'le rayon' : 'la masse' }))}
      >
        <defs>
          <radialGradient id="size-star" cx="50%" cy="100%" r="60%">
            <stop offset="0%" stopColor="#fde68a" />
            <stop offset="100%" stopColor="#f59e0b" />
          </radialGradient>
        </defs>
        <circle cx={width / 2} cy={26 - starPx} r={starPx} fill="url(#size-star)" opacity="0.92" />
        <text x={12} y={18} fontSize="11" fill="#3b2a08" fontWeight="600">
          {tr(starLabel(c.starClass))} · {(c.starRadiusSolar ?? 1).toFixed(2)} R☉
        </text>
        <line
          x1={0}
          y1={baseline}
          x2={width}
          y2={baseline}
          stroke="var(--outline-variant)"
          strokeDasharray="2 3"
        />
        {bodies.map((body, index) => {
          const cx = cell * (index + 0.5);
          const r = Math.max(1.4, scale * metricOf(body, metric));
          return (
            <g key={body.key}>
              <circle cx={cx} cy={baseline - r} r={r} fill={body.color} opacity="0.92" />
              <title>
                {`${body.label} · ${body.radiusEarth.toFixed(2)} R⊕ · ${formatMassEarth(body.massEarth)}`}
              </title>
              <text
                x={cx}
                y={baseline + 14}
                textAnchor="middle"
                fontSize="10"
                fill="var(--ink-soft)"
              >
                {body.label}
              </text>
              <text
                x={cx}
                y={baseline + 27}
                textAnchor="middle"
                fontSize="10"
                fill="var(--ink-faint)"
                fontFamily="var(--font-mono)"
              >
                {metric === 'radius'
                  ? `${body.radiusEarth < 0.01 ? body.radiusEarth.toExponential(0) : body.radiusEarth.toFixed(2)} R⊕`
                  : formatMassEarth(body.massEarth)}
              </text>
            </g>
          );
        })}
      </svg>
      <p className="mt-2 text-xs text-[var(--ink-faint)]">
        {metric === 'radius'
          ? tr('Discs share one radius scale and one baseline.')
          : tr('Disc area follows mass on one scale and one baseline.')}{' '}
        {translateIn(language, 'cosmology', 'Star scale description', {
          width: starWidths.toFixed(0),
        })}
      </p>
    </Panel>
  );
}

function metricOf(body: SizedBody, metric: 'radius' | 'mass'): number {
  return metric === 'radius' ? body.radiusEarth : Math.sqrt(Math.max(0, body.massEarth));
}

/**
 * Earth as the ruler. Every figure on this page is in Earth units already, but a ratio printed
 * next to a bar is read in a glance where "1.44 M⊕" is read as an abstraction.
 */
function EarthComparison({ cosmology: c }: { cosmology: ExportCosmology }) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const rows = [
    { label: tr('Mass'), value: `${c.worldMassEarth.toFixed(2)} M⊕`, ratio: c.worldMassEarth, earth: '1.00 M⊕' },
    {
      label: tr('Radius'),
      value: `${c.worldRadiusEarth.toFixed(2)} R⊕`,
      ratio: c.worldRadiusEarth,
      earth: '1.00 R⊕',
    },
    {
      label: tr('Gravity'),
      value: `${c.surfaceGravityG.toFixed(2)} g`,
      ratio: c.surfaceGravityG,
      earth: '1.00 g',
    },
    {
      label: tr('Surface temp'),
      value: `${Math.round(c.surfaceTempK)} K`,
      ratio: c.surfaceTempK / 288,
      earth: '288 K',
    },
    {
      label: tr('Year length'),
      value: `${Math.round(c.orbitalPeriodDays)} d`,
      ratio: c.orbitalPeriodDays / 365.25,
      earth: '365 d',
    },
    {
      label: tr('Escape velocity'),
      value: `${c.escapeVelocityKmS.toFixed(1)} km/s`,
      ratio: c.escapeVelocityKmS / 11.19,
      earth: '11.2 km/s',
    },
  ];

  return (
    <Panel title={tr("Compared to Earth")}>
      <ul className="space-y-3">
        {rows.map((row) => (
          <li key={row.label}>
            <div className="flex items-baseline justify-between gap-2 text-sm">
              <span className="text-[var(--ink-soft)]">{row.label}</span>
              <span className="he-data text-[var(--ink)]">
                {row.value}
                <span className="ml-2 text-[var(--ink-faint)]">{row.ratio.toFixed(2)}×</span>
              </span>
            </div>
            <EarthBar ratio={row.ratio} />
            <div className="mt-1 text-[11px] text-[var(--ink-faint)]">{translateIn(language, 'inline', "Earth")} {row.earth}</div>
          </li>
        ))}
      </ul>
    </Panel>
  );
}

/** Earth sits at the midpoint; a ratio of r lands at r/(r+1), so half and double are symmetric. */
function EarthBar({ ratio }: { ratio: number }) {
  const point = (ratio / (ratio + 1)) * 100;
  const left = Math.min(50, point);
  const width = Math.max(0.6, Math.abs(point - 50));

  return (
    <div className="relative mt-1.5 h-2 rounded-full bg-[var(--input)]">
      <div
        className="absolute inset-y-0 rounded-full bg-[var(--primary)]"
        style={{ left: `${left}%`, width: `${width}%` }}
      />
      <div className="absolute -inset-y-1 left-1/2 w-px -translate-x-1/2 bg-[var(--outline)]" />
    </div>
  );
}

/**
 * The consistency engine in the reader's language. The exact figure behind each check is still
 * one disclosure away; what belongs here is the claim the check is making about the world.
 */
function WhyThisWorldWorks({
  checks,
  isHabitable,
}: {
  checks: ExportCosmologyCheck[];
  isHabitable: boolean;
}) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  return (
    <Panel title={tr("Why this world works")}>
      <ul className="space-y-2 text-sm">
        {checks.map((check) => (
          <li key={check.label} className="flex gap-2">
            <span
              className={check.passed ? 'text-[var(--primary)]' : 'text-[var(--error)]'}
              aria-hidden
            >
              {check.passed ? '✓' : '✗'}
            </span>
            <span className={check.passed ? 'text-[var(--ink-soft)]' : 'text-[var(--error)]'}>
              {tr(plainCheck(check))}
            </span>
          </li>
        ))}
      </ul>
      <p className="mt-3 border-t border-[var(--rule)] pt-3 text-xs text-[var(--ink-faint)]">
        {isHabitable
          ? tr('Every check passed: liquid surface water is physically plausible here. ')
          : tr('At least one check failed — this world is outside the plausible band. ')}
        {translateIn(language, 'inline', "The figure behind each one is in the generation engine below.")}
      </p>
    </Panel>
  );
}

/**
 * Engine check labels are terse because they name the constraint; a reader wants the claim. Any
 * label the engine adds later falls through to its own wording rather than being dropped.
 */
function plainCheck(check: ExportCosmologyCheck): string {
  switch (check.label) {
    case 'Star lifespan':
      return 'The star lives long enough for complex life';
    case 'Habitable zone':
      return 'The orbit lies inside the liquid-water zone';
    case 'Atmosphere retention':
      return 'Gravity is strong enough to hold an atmosphere';
    case 'Surface temperature':
      return 'Surface liquid water is physically plausible';
    case 'Shepherd giant':
      return 'A giant beyond the snow line sweeps up the leftover debris';
    case 'Galactic habitable zone':
      return 'The system sits in the galaxy’s habitable annulus';
    case 'Metals for a crust':
      return 'Enough heavy elements for an iron core and workable ores';
    default:
      return `${check.label}: ${check.detail}`;
  }
}

function Disclosure({
  title,
  hint,
  children,
}: {
  title: string;
  hint?: string;
  children: ReactNode;
}) {
  return (
    <details className="group rounded-lg border border-[var(--rule)] bg-[var(--panel)]">
      <summary className="flex cursor-pointer list-none items-center gap-3 px-4 py-3 [&::-webkit-details-marker]:hidden">
        <span className="text-[var(--ink-faint)] transition-transform group-open:rotate-90">▸</span>
        <span className="he-label text-[var(--ink)]">{title}</span>
        {hint && <span className="hidden text-xs text-[var(--ink-faint)] sm:inline">{hint}</span>}
      </summary>
      <div className="border-t border-[var(--rule)] p-4">{children}</div>
    </details>
  );
}

function DetailGroup({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div>
      <div className="he-label mb-2">{title}</div>
      <dl className="space-y-2 text-sm">{children}</dl>
    </div>
  );
}

/** Everything the old page printed in one column, grouped by the question it answers. */
function ScientificDetails({
  cosmology: c,
  kind,
  name,
}: {
  cosmology: ExportCosmology;
  kind: WorldKind;
  name: string;
}) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const moons = c.moons ?? [];
  const homeMoons = c.homeMoons ?? [];
  const comets = c.comets ?? [];
  const moonOrbitR =
    c.moonOrbitalDistanceEarthRadii ?? moons.find((moon) => moon.habitable)?.orbitalDistanceEarthRadii;

  return (
    <Disclosure
      title={tr("Scientific details")}
      hint={tr("Stellar properties, planetary parameters, orbital mechanics, comets")}
    >
      <div className="grid gap-6 lg:grid-cols-2">
        <DetailGroup title={tr("Stellar properties")}>
          <Diag label={tr("Spectral class")} value={tr(starLabel(c.starClass))} />
          <Diag label={tr("Mass / radius")} value={`${c.starMassSolar.toFixed(2)} M☉ · ${(c.starRadiusSolar ?? 1).toFixed(2)} R☉`} />
          <Diag label={tr("Luminosity")} value={`${c.luminositySolar.toFixed(3)} L☉`} />
          <Diag label={tr("Main-sequence lifespan")} value={`${c.starLifespanGyr.toFixed(1)} Gyr`} />
          <Diag
            label={tr("Habitable zone")}
            value={`${c.habitableZoneInnerAu.toFixed(2)}–${c.habitableZoneOuterAu.toFixed(2)} AU`}
          />
          {c.snowLineAu != null && <Diag label={tr("Snow line")} value={`${c.snowLineAu.toFixed(2)} AU`} />}
        </DetailGroup>

        <DetailGroup title={(translateIn(language, 'inline', "Planetary parameters · {v0}", { v0: name }))}>
          <Diag
            label={tr("Mass / radius")}
            value={`${c.worldMassEarth.toFixed(2)} M⊕ · ${c.worldRadiusEarth.toFixed(2)} R⊕`}
          />
          {c.meanDensityEarth != null && (
            <Diag
              label={tr("Density / iron")}
              value={(translateIn(language, 'inline', "{v0} ρ⊕ · {v1}% iron · core {v2}%", { v0: c.meanDensityEarth.toFixed(2), v1: (c.bulkIronMassFraction * 100).toFixed(0), v2: (c.coreMassFraction * 100).toFixed(0) }))}
            />
          )}
          <Diag label={tr("Surface gravity")} value={`${c.surfaceGravityG.toFixed(2)} g`} />
          <Diag label={tr("Escape velocity")} value={`${c.escapeVelocityKmS.toFixed(1)} km/s`} />
          <Diag label={tr("Bond albedo")} value={c.bondAlbedo.toFixed(2)} />
          <Diag label={tr("Greenhouse warming")} value={`+${c.greenhouseDeltaC.toFixed(0)} °C`} />
          <Diag
            label={tr(tr("Equilibrium temp (no air)"))}
            value={`${(c.equilibriumTempK - 273.15).toFixed(0)} °C (${c.equilibriumTempK.toFixed(0)} K)`}
          />
          <Diag
            label={tr("Surface temp")}
            value={`${(c.surfaceTempK - 273.15).toFixed(0)} °C (${c.surfaceTempK.toFixed(0)} K)`}
          />
        </DetailGroup>

        <DetailGroup title={tr("Orbital mechanics")}>
          <Diag
            label={kind === 'Moon' ? tr('Parent distance to star') : tr('Distance to star')}
            value={`${c.orbitalDistanceAu.toFixed(3)} AU`}
          />
          <Diag
            label={kind === 'Moon' ? tr('Parent year') : tr('Year length')}
            value={(translateIn(language, 'inline', "{v0} Earth days", { v0: Math.round(c.orbitalPeriodDays) }))}
          />
          {kind === 'Moon' && moonOrbitR != null && (
            <Diag label={tr("Distance to parent")} value={formatMoonOrbit(moonOrbitR)} />
          )}
          {kind === 'Moon' && c.rocheLimitEarthRadii != null && (
            <Diag label={tr("Roche limit")} value={`${c.rocheLimitEarthRadii.toFixed(1)} R⊕`} />
          )}
          {kind === 'Moon' && c.moonDayLengthDays != null && (
            <Diag label={tr("Tidal day length")} value={(translateIn(language, 'inline', "{v0} Earth days", { v0: c.moonDayLengthDays.toFixed(1) }))} />
          )}
          {kind === 'Moon' && c.parentGiantMassEarth != null && (
            <Diag label={tr("Parent giant mass")} value={`${c.parentGiantMassEarth.toFixed(0)} M⊕`} />
          )}
          {(c.companions ?? []).map((body) => (
            <Diag
              key={`${body.role}-${body.semiMajorAxisAu}`}
              label={tr(body.roleLabel ?? COMPANION_ROLE_LABELS[body.role] ?? body.role)}
              value={`${body.semiMajorAxisAu.toFixed(2)} AU · ${Math.round(body.orbitalPeriodDays)} d · ${body.massEarth.toFixed(body.role === 'InnerRocky' ? 2 : 0)} M⊕${
                (body.moons?.length ?? 0) > 0 ? (translateIn(language, 'inline', " · {v0} moons", { v0: body.moons.length })) : ''
              }${body.appearance?.ring ? (translateIn(language, 'inline', " · ringed")) : ''}`}
            />
          ))}
          {moons
            .filter((moon) => !moon.habitable)
            .map((moon) => (
              <Diag
                key={`moon-${moon.index}`}
                label={(translateIn(language, 'inline', "Moon {v0} of parent", { v0: moon.index }))}
                value={`${formatMoonOrbit(moon.orbitalDistanceEarthRadii)} · ${moon.massEarth.toFixed(2)} M⊕ · ${moon.dayLengthDays.toFixed(1)} d`}
              />
            ))}
          {homeMoons.map((moon) => (
            <Diag
              key={`home-moon-${moon.index}`}
              label={(translateIn(language, 'inline', "Moon {v0}", { v0: moon.name ?? moon.index }))}
              value={(translateIn(language, 'inline', "{v0} · {v1} · month {v2} d", { v0: formatMoonOrbit(moon.orbitalDistanceEarthRadii), v1: formatMassEarth(moon.massEarth), v2: moon.dayLengthDays.toFixed(1) }))}
            />
          ))}
          {c.orientation && (
            <Diag
              label={tr("Celestial pole")}
              value={(translateIn(language, 'inline', "{v0}° from the galactic pole · {v1}", { v0: c.orientation.poleTiltFromGalacticPoleDeg.toFixed(0), v1: tr(bandMotion(c.orientation.poleTiltFromGalacticPoleDeg)) }))}
            />
          )}
        </DetailGroup>

        <DetailGroup title={comets.length > 0 ? (translateIn(language, 'inline', "Comets · {v0} tracked", { v0: comets.length })) : tr('Comets')}>
          {comets.length === 0 ? (
            <p className="text-sm text-[var(--ink-faint)]">{tr("No comets were recorded for this system.")}</p>
          ) : (
            comets.map((comet) => (
              <Diag
                key={`comet-${comet.index}`}
                label={(translateIn(language, 'inline', "Comet {v0}", { v0: comet.index }))}
                value={`q ${comet.perihelionAu.toFixed(2)} AU · Q ${comet.aphelionAu.toFixed(1)} AU · ${formatCometPeriod(comet.orbitalPeriodDays, language)} · ${comet.nucleusRadiusKm.toFixed(1)} km`}
              />
            ))
          )}
        </DetailGroup>
      </div>
    </Disclosure>
  );
}

/** The engine's own working: what it checked, against what threshold, and what it measured. */
function GenerationDetails({ cosmology: c }: { cosmology: ExportCosmology }) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  return (
    <Disclosure
      title={tr("Generation and consistency engine")}
      hint={tr("Constraints, assumptions, and validation checks")}
    >
      <p className="mb-3 text-sm text-[var(--ink-soft)]">{tr("Each check is a constraint the generator had to satisfy before this system was accepted. The figure in brackets is the threshold; the figure before it is what this system measured.")}</p>
      <ul className="space-y-1.5 text-xs">
        {c.checks.map((check) => (
          <li
            key={check.label}
            className={`rounded px-2 py-1.5 font-medium ${
              check.passed
                ? 'bg-[color-mix(in_srgb,var(--primary)_12%,transparent)] text-[var(--primary)]'
                : 'bg-[color-mix(in_srgb,var(--error)_12%,transparent)] text-[var(--error)]'
            }`}
          >
            {check.passed ? '✓' : '✗'} <span className="font-semibold">{tr(check.label)}</span> ·{' '}
            {check.detail}
          </li>
        ))}
      </ul>
      {c.isHabitable && (
        <p className="mt-3 text-xs font-semibold text-[var(--primary)]">{tr("All checks passed — liquid surface water is physically plausible.")}</p>
      )}
    </Disclosure>
  );
}

/**
 * Every planet of the system, one card each, ordered outward from the star. The giants carry a
 * face — banding, a spot, whatever rings they kept — because a giant is the one companion anyone
 * on the ground will actually look at; the inner rocky worlds carry only their orbit and their
 * bulk, which is all the system's history settles about them.
 */
function PlanetFaces({
  cosmology: c,
  kind,
  name,
}: {
  cosmology: ExportCosmology;
  kind: WorldKind;
  name: string;
}) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const cards: PlanetCard[] = (c.companions ?? []).map((body) => ({
    key: `${body.role}-${body.semiMajorAxisAu}`,
    label: tr(body.roleLabel ?? COMPANION_ROLE_LABELS[body.role] ?? body.role),
    semiMajorAxisAu: body.semiMajorAxisAu,
    massEarth: body.massEarth,
    radiusEarth: body.radiusEarth,
    orbitalPeriodDays: body.orbitalPeriodDays,
    appearance: body.appearance,
    tint: companionColor(body.role),
    moons: (body.moons ?? []).map((moon) =>
      moon.habitable && kind === 'Moon' ? `${moon.name ?? String(moon.index)} (${name})` : moon.name ?? String(moon.index),
    ),
  }));

  if (kind === 'Planet') {
    cards.push({
      key: 'home',
      label: name,
      home: true,
      semiMajorAxisAu: c.orbitalDistanceAu,
      massEarth: c.worldMassEarth,
      radiusEarth: c.worldRadiusEarth,
      orbitalPeriodDays: c.orbitalPeriodDays,
      tint: '#4ade80',
      moons: (c.homeMoons ?? []).map((moon) => moon.name ?? String(moon.index)),
    });
  }

  if (cards.length === 0) return null;
  cards.sort((a, b) => a.semiMajorAxisAu - b.semiMajorAxisAu);

  return (
    <Panel title={tr("The planets")}>
      <div className="grid items-start gap-3 md:grid-cols-2 xl:grid-cols-3">
        {cards.map((card) => (
          <PlanetFace key={card.key} card={card} />
        ))}
      </div>
    </Panel>
  );
}

interface PlanetCard {
  key: string;
  label: string;
  semiMajorAxisAu: number;
  massEarth: number;
  radiusEarth: number;
  orbitalPeriodDays: number;
  appearance?: ExportGiantAppearance;
  tint: string;
  moons: string[];
  home?: boolean;
}

function PlanetFace({ card }: { card: PlanetCard }) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const face = card.appearance;
  return (
    <div
      className={`rounded-md border p-3 ${
        card.home
          ? 'border-[var(--tertiary)] bg-[var(--input)]'
          : 'border-[var(--rule)] bg-[var(--input)]'
      }`}
    >
      <div className="flex items-baseline justify-between gap-2">
        <span className="text-sm font-semibold">
          {card.home ? card.label : tr(card.label)}
          {card.home && (
            <span className="ml-2 text-xs font-normal text-[var(--ink-faint)]">{tr("this world")}</span>
          )}
        </span>
        <span className="text-xs text-[var(--ink-faint)]">
          {card.semiMajorAxisAu.toFixed(2)} AU · {formatMassEarth(card.massEarth)}
        </span>
      </div>
      <div className="mt-2 flex items-center gap-3">
        {face ? <BandedDisc appearance={face} /> : <RockyDisc card={card} />}
        <dl className="flex-1 space-y-1 text-xs text-[var(--ink-soft)]">
          <FaceRow
            label={tr("Orbit")}
            value={`${card.semiMajorAxisAu.toFixed(3)} AU · ${formatCometPeriod(card.orbitalPeriodDays, language)}`}
          />
          <FaceRow
            label={tr("Bulk")}
            value={`${card.radiusEarth.toFixed(2)} R⊕ · ${formatMassEarth(card.massEarth)}`}
          />
          {face && (
            <>
              <FaceRow
                label={tr("Spin")}
                value={(translateIn(language, 'inline', "{e0} h{e1} · {e2} bands", { e0: face.rotationPeriodHours.toFixed(1), e1: face.retrograde ? ', retrograde' : '', e2: face.bandCount, f0: face.rotationPeriodHours.toFixed(1), f1: face.retrograde ? ', rétrograde' : '', f2: face.bandCount }))}
              />
              <FaceRow label={tr("Tilt")} value={(translateIn(language, 'inline', "{v0}° obliquity", { v0: face.obliquityDeg.toFixed(0) }))} />
              {face.ring ? (
                <FaceRow
                  label={tr("Rings")}
                  value={(translateIn(language, 'inline', "{e0}, {e1}–{e2} R{e3} · {e4}% open", { e0: face.ring.compositionLabel, e1: face.ring.innerRadiusPlanetRadii.toFixed(2), e2: face.ring.outerRadiusPlanetRadii.toFixed(2), e3: face.ring.divisionRadiusPlanetRadii > face.ring.innerRadiusPlanetRadii ? ', divided' : '', e4: (face.ringOpenness * 100).toFixed(0), f0: tr(face.ring.compositionLabel), f1: face.ring.innerRadiusPlanetRadii.toFixed(2), f2: face.ring.outerRadiusPlanetRadii.toFixed(2), f3: face.ring.divisionRadiusPlanetRadii > face.ring.innerRadiusPlanetRadii ? ', divisés' : '', f4: (face.ringOpenness * 100).toFixed(0) }))}
                />
              ) : (
                <FaceRow label={tr("Rings")} value={tr("none held")} />
              )}
              {face.ringBrightnessBoostMagnitudes < -0.01 && (
                <FaceRow
                  label={tr("Ring light")}
                  value={(translateIn(language, 'inline', "{v0} mag brighter", { v0: face.ringBrightnessBoostMagnitudes.toFixed(2) }))}
                />
              )}
              {face.storm && (
                <FaceRow
                  label={tr("Storm")}
                  value={(translateIn(language, 'inline', "{v0}, {v1}°{v2}, standing {v3} years", { v0: face.storm.name, v1: Math.abs(face.storm.latitudeDeg).toFixed(0), v2: face.storm.latitudeDeg >= 0 ? 'N' : 'S', v3: Math.round(face.storm.ageYears) }))}
                />
              )}
            </>
          )}
          <FaceRow
            label={tr("Moons")}
            value={card.moons.length === 0 ? (translateIn(language, 'inline', "none")) : card.moons.join(', ')}
          />
        </dl>
      </div>
    </div>
  );
}

/** A world with no face of its own: a lit disc in its own tint, scaled against Earth. */
function RockyDisc({ card }: { card: PlanetCard }) {
  const size = 72;
  const centre = size / 2;
  const radius = size * 0.28 * Math.min(1, Math.max(0.35, Math.cbrt(card.radiusEarth)));
  const uid = `rocky-${card.key.replace(/[^a-zA-Z0-9]/g, '')}`;

  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} role="presentation">
      <defs>
        <radialGradient id={uid} cx="32%" cy="30%" r="70%">
          <stop offset="0%" stopColor={card.tint} stopOpacity="1" />
          <stop offset="100%" stopColor={card.tint} stopOpacity="0.45" />
        </radialGradient>
        <clipPath id={`${uid}-clip`}>
          <circle cx={centre} cy={centre} r={radius} />
        </clipPath>
      </defs>
      <circle cx={centre} cy={centre} r={radius} fill={`url(#${uid})`} />
      <ellipse
        cx={centre + radius * 0.45}
        cy={centre}
        rx={radius * 0.5}
        ry={radius}
        fill="#020617"
        opacity="0.35"
        clipPath={`url(#${uid}-clip)`}
      />
    </svg>
  );
}

function FaceRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex gap-2">
      <dt className="w-16 shrink-0 text-[var(--ink-faint)]">{label}</dt>
      <dd className="flex-1">{value}</dd>
    </div>
  );
}

/** The giant as it would be seen: its bands, its storm, and its rings tipped by the obliquity. */
function BandedDisc({ appearance }: { appearance: ExportGiantAppearance }) {
  const size = 72;
  const centre = size / 2;
  const radius = size * 0.28;
  const light = tintToCss(appearance.bandLight);
  const dark = tintToCss(appearance.bandDark);
  const clip = `disc-${Math.round(appearance.ascendingNodeDeg)}-${appearance.bandCount}-${Math.round(appearance.rotationPeriodHours * 10)}`;
  const bands = Array.from({ length: appearance.bandCount }, (_, index) => index);
  const storm = appearance.storm;

  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} role="presentation">
      <defs>
        <clipPath id={clip}>
          <circle cx={centre} cy={centre} r={radius} />
        </clipPath>
      </defs>
      {appearance.ring && (
        <PlanetRingGlyph x={centre} y={centre} size={radius} appearance={appearance} />
      )}
      <circle cx={centre} cy={centre} r={radius} fill={light} />
      <g clipPath={`url(#${clip})`}>
        {bands.map((band) => (
          <rect
            key={band}
            x={centre - radius}
            y={centre - radius + (band * 2 * radius) / appearance.bandCount}
            width={radius * 2}
            height={(2 * radius) / appearance.bandCount}
            fill={band % 2 === 0 ? dark : light}
            opacity={0.85}
          />
        ))}
        {storm && (
          <ellipse
            cx={centre + radius * 0.25}
            cy={centre - (storm.latitudeDeg / 90) * radius}
            rx={(radius * storm.longitudeSpanDeg) / 180}
            ry={(radius * storm.latitudeSpanDeg) / 180}
            fill={tintToCss(storm.tint)}
          />
        )}
        <ellipse
          cx={centre + radius * 0.45}
          cy={centre}
          rx={radius * 0.5}
          ry={radius}
          fill="#020617"
          opacity="0.35"
        />
      </g>
    </svg>
  );
}

function Diag({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4 border-b border-[var(--rule)] pb-1.5">
      <dt className="text-[var(--ink-faint)]">{label}</dt>
      <dd className="he-data text-right text-[var(--ink)]">{value}</dd>
    </div>
  );
}

const EARTH_MASSES_PER_SOLAR = 332_946;
const EARTH_KM = 6371;

function formatMassEarth(massEarth: number, solar?: number): string {
  if (solar != null) return `${solar.toFixed(2)} M☉`;
  if (massEarth >= 10) return `${Math.round(massEarth).toLocaleString()} M⊕`;
  if (massEarth >= 0.01) return `${massEarth.toFixed(2)} M⊕`;
  const exp = massEarth.toExponential(1);
  return `${exp.replace('e', '×10^')} M⊕`;
}

function formatCometPeriod(days: number, language: string = 'en'): string {
  const years = days / 365.25;
  const locale = language;
  if (years >= 100) {
    return translateIn(language, 'cosmology', 'cometPeriod.centuries', {
      count: Math.round(years).toLocaleString(locale),
    });
  }
  if (years >= 2) {
    return translateIn(language, 'cosmology', 'cometPeriod.years', {
      count: years.toLocaleString(locale, { minimumFractionDigits: 1, maximumFractionDigits: 1 }),
    });
  }
  return translateIn(language, 'cosmology', 'cometPeriod.days', {
    count: Math.round(days).toLocaleString(locale),
  });
}

/** Earth radii plus kilometres — moons sit tens of thousands of km out, not AU. */
function formatMoonOrbit(earthRadii: number): string {
  const km = earthRadii * 6371;
  if (km >= 1_000_000) {
    return `${earthRadii.toFixed(0)} R⊕ · ${(km / 1_000_000).toFixed(2)} million km`;
  }
  return `${earthRadii.toFixed(0)} R⊕ · ${Math.round(km).toLocaleString()} km`;
}

function morphologyLabel(morphology: GalaxyMorphology): string {
  switch (morphology) {
    case 'BarredSpiral':
      return 'barred spiral';
    case 'UnbarredSpiral':
      return 'unbarred spiral';
    case 'Elliptical':
      return 'elliptical';
    default:
      return morphology;
  }
}

const GALAXY_DISK_KPC = 16;
const SOLAR_NEIGHBORHOOD_KPC = 8;

function GalaxyView({ galaxy, mode }: { galaxy: ExportGalaxy; mode: 'face' | 'edge' }) {
  return mode === 'face' ? <GalaxyFace galaxy={galaxy} /> : <GalaxyEdge galaxy={galaxy} />;
}

function GalaxyFace({ galaxy }: { galaxy: ExportGalaxy }) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const size = 560;
  const cx = 280;
  const cy = 280;
  const scale = 15.5;
  const r = (kpc: number) => kpc * scale;
  const loc = galaxy.location;
  const ox = cx + loc.galactocentricRadiusKpc * Math.cos(loc.azimuthRad) * scale;
  const oy = cy - loc.galactocentricRadiusKpc * Math.sin(loc.azimuthRad) * scale;

  return (
    <svg
      viewBox={`0 0 ${size} ${size}`}
      className="h-72 w-full rounded-md border border-[var(--rule)] bg-[var(--canvas)]"
      role="img"
      aria-label={tr("Face-on host galaxy")}
    >
      <circle cx={cx} cy={cy} r={r(GALAXY_DISK_KPC)} fill="none" stroke="var(--outline-variant)" />
      <circle
        cx={cx}
        cy={cy}
        r={r(galaxy.outerHabitableRadiusKpc)}
        fill="color-mix(in srgb, var(--tertiary) 28%, transparent)"
      />
      <circle cx={cx} cy={cy} r={r(galaxy.innerHabitableRadiusKpc)} fill="var(--canvas)" />
      <circle
        cx={cx}
        cy={cy}
        r={r(galaxy.outerHabitableRadiusKpc)}
        fill="none"
        stroke="var(--tertiary)"
        strokeWidth="1.5"
      />
      <circle
        cx={cx}
        cy={cy}
        r={r(galaxy.innerHabitableRadiusKpc)}
        fill="none"
        stroke="var(--tertiary)"
        strokeWidth="1.5"
      />
      {galaxy.morphology === 'Elliptical'
        ? [0.4, 0.7, 1.0, 1.4, 1.9].map((frac) => (
            <circle
              key={frac}
              cx={cx}
              cy={cy}
              r={r(galaxy.diskScaleLengthKpc * frac)}
              fill="none"
              stroke="#c4b48a"
              opacity="0.35"
            />
          ))
        : galaxy.morphology === 'BarredSpiral' && (
            <rect
              x={cx - r(galaxy.innerHabitableRadiusKpc * 0.55)}
              y={cy - 10}
              width={r(galaxy.innerHabitableRadiusKpc * 0.55) * 2}
              height="20"
              rx="8"
              fill="#6b5a3a"
              opacity="0.85"
            />
          )}
      <circle cx={cx} cy={cy} r="7" fill="#f2e6c2" />
      {Array.from({ length: galaxy.spiralArmCount }, (_, arm) => (
        <path
          key={arm}
          d={armPath(galaxy, arm, cx, cy, scale)}
          fill="none"
          stroke="#9ec5ff"
          strokeWidth="2.2"
          opacity="0.85"
        />
      ))}
      <line
        x1={cx}
        y1={cy}
        x2={ox}
        y2={oy}
        stroke="#8ec8ff"
        strokeWidth="1"
        strokeDasharray="4 4"
      />
      <circle cx={ox} cy={oy} r="6" fill="#ff5a5a" stroke="#fff4e0" strokeWidth="2" />
    </svg>
  );
}

function GalaxyEdge({ galaxy }: { galaxy: ExportGalaxy }) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const width = 400;
  const height = 280;
  const padX = 16;
  const padY = 20;
  const plotW = width - padX * 2;
  const plotH = 240;
  const midY = padY + plotH / 2;
  const extentPc =
    galaxy.morphology === 'Elliptical'
      ? Math.max(1600, galaxy.outerHabitableRadiusKpc * galaxy.axisRatio * 1000 * 1.2)
      : 1200;
  const xOf = (kpc: number) => padX + (kpc / GALAXY_DISK_KPC) * plotW;
  const yOf = (pc: number) => midY - (pc / extentPc) * (plotH / 2);
  const habitableH = Math.min(
    plotH - 16,
    galaxy.morphology === 'Elliptical'
      ? (galaxy.outerHabitableRadiusKpc * galaxy.axisRatio * 1000) / extentPc * plotH
      : (3 * galaxy.thinDiskScaleHeightPc) / extentPc * plotH,
  );
  const loc = galaxy.location;

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      className="h-72 w-full rounded-md border border-[var(--rule)] bg-[var(--canvas)]"
      role="img"
      aria-label={tr("Edge-on host galaxy")}
    >
      <rect
        x={padX}
        y={padY}
        width={plotW}
        height={plotH}
        fill="#121a2e"
        stroke="var(--outline-variant)"
      />
      <line x1={padX} y1={midY} x2={padX + plotW} y2={midY} stroke="#2a3654" />
      <rect
        x={xOf(galaxy.innerHabitableRadiusKpc)}
        y={midY - habitableH / 2}
        width={xOf(galaxy.outerHabitableRadiusKpc) - xOf(galaxy.innerHabitableRadiusKpc)}
        height={habitableH}
        fill="color-mix(in srgb, var(--tertiary) 40%, transparent)"
      />
      {galaxy.morphology === 'Elliptical' && (
        <ellipse
          cx={padX}
          cy={midY}
          rx={xOf(galaxy.diskScaleLengthKpc) - padX}
          ry={(galaxy.diskScaleLengthKpc * galaxy.axisRatio * 1000) / extentPc * (plotH / 2)}
          fill="none"
          stroke="#c4b48a"
          opacity="0.5"
        />
      )}
      <circle
        cx={xOf(loc.galactocentricRadiusKpc)}
        cy={yOf(loc.heightPc)}
        r="5"
        fill="#ff5a5a"
        stroke="#fff4e0"
        strokeWidth="2"
      />
    </svg>
  );
}

function armPath(
  galaxy: ExportGalaxy,
  arm: number,
  cx: number,
  cy: number,
  scale: number,
): string {
  const samples = 80;
  const inner = 1.2;
  let d = '';
  for (let i = 0; i <= samples; i++) {
    const radius = inner + (GALAXY_DISK_KPC - inner) * (i / samples);
    const angle = spiralArmAngle(galaxy, arm, radius);
    const x = cx + radius * Math.cos(angle) * scale;
    const y = cy - radius * Math.sin(angle) * scale;
    d += `${i === 0 ? 'M' : 'L'}${x.toFixed(2)} ${y.toFixed(2)} `;
  }
  return d;
}

function spiralArmAngle(galaxy: ExportGalaxy, armIndex: number, radiusKpc: number): number {
  if (galaxy.spiralArmCount <= 0) return 0;
  const pitchRad = (galaxy.spiralPitchDeg * Math.PI) / 180;
  const logTerm = Math.log(Math.max(0.5, radiusKpc) / SOLAR_NEIGHBORHOOD_KPC);
  const armPhase = logTerm / Math.tan(Math.max(0.05, pitchRad));
  return (2 * Math.PI * armIndex) / galaxy.spiralArmCount + armPhase;
}

function starLabel(starClass: StarSpectralClass): string {
  switch (starClass) {
    case 'M':
      return 'M-type star';
    case 'K':
      return 'K-type star';
    case 'G':
      return 'G-type star';
    case 'F':
      return 'F-type star';
    default:
      return 'host star';
  }
}

function SystemView({
  cosmology: c,
  kind,
  name,
  mode,
  heightClass = 'h-64',
  wide = false,
}: {
  cosmology: ExportCosmology;
  kind: WorldKind;
  name: string;
  mode: 'zone' | 'system';
  heightClass?: string;
  /** A canvas shaped for a full-width panel rather than a half-column card. */
  wide?: boolean;
}) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const companions = c.companions ?? [];
  const comets = c.comets ?? [];
  const width = wide ? 960 : 480;
  const height = wide ? 440 : 300;
  const cx = width / 2;
  const cy = height / 2;
  const farthestPlanet = companions.reduce(
    (max, body) => Math.max(max, body.semiMajorAxisAu),
    Math.max(c.habitableZoneOuterAu, c.orbitalDistanceAu, c.snowLineAu ?? 0),
  );
  // Fitted to the outermost planet, not the outermost comet: a comet with an aphelion five times
  // the last planet's orbit would squeeze the whole system into the middle of the frame. The
  // comet ellipses simply run off the edge, which is also what their orbits do.
  const maxAu = mode === 'zone' ? c.habitableZoneOuterAu * 1.45 : farthestPlanet * 1.15;
  const maxR = Math.min(cx, cy) - (wide ? 34 : 28);
  const r = (au: number) => (au / maxAu) * maxR;
  const uid = `${mode}-${c.starClass}-${Math.round(c.orbitalDistanceAu * 1000)}`;
  const star = starLook(c.starClass, mode === 'zone' ? 9 : 6);
  const isMoon = kind === 'Moon';
  const compact = mode === 'system';
  const worldR = compact ? 5 : 6;
  const giantR = isMoon ? (compact ? 6 : 10) : 0;
  const moons = c.moons ?? [];
  const angle = mode === 'zone' ? -0.55 : -0.35;
  const orbitR = r(c.orbitalDistanceAu);
  const worldX = cx + Math.cos(angle) * orbitR;
  const worldY = cy + Math.sin(angle) * orbitR;
  // The host giant sits at the habitable orbit and is drawn as the moon system below, so it is
  // kept out of the companion glyphs rather than doubled on its own orbit.
  const host = companions.find((body) => body.role === 'HostGiant');
  const visibleCompanions = companions.filter(
    (body) => body.role !== 'HostGiant' && body.semiMajorAxisAu <= maxAu * 0.98,
  );

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      className={`${heightClass} w-full rounded-md border border-[var(--rule)] bg-[var(--canvas)]`}
      role="img"
      aria-label={
        mode === 'zone'
          ? (translateIn(language, 'inline', "Habitable zone of {v0}", { v0: name }))
          : (translateIn(language, 'inline', "Full system around {v0}", { v0: tr(starLabel(c.starClass)) }))
      }
    >
      <PaintDefs uid={uid} star={star} />

      {mode === 'zone' && (
        <>
          <circle
            cx={cx}
            cy={cy}
            r={r(c.habitableZoneOuterAu)}
            fill="color-mix(in srgb, var(--primary) 16%, transparent)"
          />
          <circle cx={cx} cy={cy} r={r(c.habitableZoneInnerAu)} fill="var(--canvas)" />
        </>
      )}
      {mode === 'system' && (
        <circle
          cx={cx}
          cy={cy}
          r={r(c.habitableZoneOuterAu)}
          fill="color-mix(in srgb, var(--primary) 22%, transparent)"
        />
      )}

      <OrbitRing cx={cx} cy={cy} radius={r(c.habitableZoneInnerAu)} stroke="#ef4444" dashed />
      <OrbitRing cx={cx} cy={cy} radius={r(c.habitableZoneOuterAu)} stroke="#60a5fa" dashed />
      <OrbitRing cx={cx} cy={cy} radius={orbitR} stroke="var(--outline-variant)" />

      {mode === 'system' && c.snowLineAu != null && (
        <OrbitRing cx={cx} cy={cy} radius={r(c.snowLineAu)} stroke="#94a3b8" dotted />
      )}
      {visibleCompanions.map((body) => (
        <OrbitRing
          key={`orbit-${body.role}-${body.semiMajorAxisAu}`}
          cx={cx}
          cy={cy}
          radius={r(body.semiMajorAxisAu)}
          stroke="var(--outline-variant)"
        />
      ))}
      {mode === 'system' &&
        comets.map((comet) => (
          <CometOrbit key={`comet-orbit-${comet.index}`} comet={comet} cx={cx} cy={cy} scale={r} />
        ))}

      <circle cx={cx} cy={cy} r={star.size * 2.8} fill={`url(#${uid}-glow)`} />
      <circle cx={cx} cy={cy} r={star.size} fill={`url(#${uid}-star)`} />
      <ellipse
        cx={cx - star.size * 0.28}
        cy={cy - star.size * 0.32}
        rx={star.size * 0.38}
        ry={star.size * 0.22}
        fill="#fff8e1"
        opacity="0.45"
      />

      {visibleCompanions.map((body, index) => (
        <CompanionBody
          key={`${body.role}-${body.semiMajorAxisAu}`}
          uid={uid}
          body={body}
          cx={cx}
          cy={cy}
          radius={r(body.semiMajorAxisAu)}
          angle={angle + 1.9 + (index * 2 * Math.PI) / Math.max(3, visibleCompanions.length)}
          labeled={mode === 'system'}
        />
      ))}

      {isMoon ? (
        <MoonSystem
          uid={uid}
          gx={worldX}
          gy={worldY}
          giantR={giantR}
          moonR={worldR}
          name={name}
          moons={moons}
          appearance={host?.appearance}
          labeled
        />
      ) : (
        <TerrestrialBody
          uid={uid}
          x={worldX}
          y={worldY}
          radius={worldR}
          name={name}
          note={tr("habitable")}
          labelAngle={angle}
        />
      )}
    </svg>
  );
}

function MapKey({
  cosmology: c,
  kind,
  name,
}: {
  cosmology: ExportCosmology;
  kind: WorldKind;
  name: string;
}) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const items = [
    { color: '#ef4444', label: (translateIn(language, 'inline', "Inner HZ {v0} AU", { v0: c.habitableZoneInnerAu.toFixed(2) })) },
    { color: '#60a5fa', label: (translateIn(language, 'inline', "Outer HZ {v0} AU", { v0: c.habitableZoneOuterAu.toFixed(2) })) },
    ...(kind === 'Moon'
      ? [
          {
            color: '#d4b483',
            label: (translateIn(language, 'inline', "Parent giant · {v0} AU from star", { v0: c.orbitalDistanceAu.toFixed(2) })),
          },
          ...(c.moons ?? []).map((moon) => ({
            color: moon.habitable ? '#86efac' : '#94a3b8',
            label: (translateIn(language, 'inline', "{e0} · {e1} from parent", { e0: moon.habitable ? name : `Moon ${moon.index}`, e1: formatMoonOrbit(moon.orbitalDistanceEarthRadii), f0: moon.habitable ? name : `Lune ${moon.index}`, f1: formatMoonOrbit(moon.orbitalDistanceEarthRadii) })),
          })),
        ]
      : [
          {
            color: '#86efac',
            label: (translateIn(language, 'inline', "{v0} · {v1} AU from star", { v0: name, v1: c.orbitalDistanceAu.toFixed(2) })),
          },
        ]),
    ...(c.snowLineAu != null
      ? [{ color: '#94a3b8', label: (translateIn(language, 'inline', "Snow line {v0} AU", { v0: c.snowLineAu.toFixed(2) })) }]
      : []),
    ...(c.companions ?? []).filter((body) => body.role !== 'HostGiant').map((body) => ({
      color: companionColor(body.role),
      label: (translateIn(language, 'inline', "{e0} · {e1} AU from star", { e0: COMPANION_ROLE_LABELS[body.role] ?? body.role, e1: body.semiMajorAxisAu.toFixed(2), f0: tr(COMPANION_ROLE_LABELS[body.role] ?? body.role), f1: body.semiMajorAxisAu.toFixed(2) })),
    })),
    ...(c.comets ?? []).map((comet) => ({
      color: '#cbd5e1',
      label: (translateIn(language, 'inline', "Comet {v0} · q {v1} AU · {v2}", { v0: comet.index, v1: comet.perihelionAu.toFixed(2), v2: formatCometPeriod(comet.orbitalPeriodDays, language) })),
    })),
  ];

  return (
    <ul className="mt-3 flex flex-wrap gap-x-4 gap-y-1.5 text-[11px] text-[var(--ink-soft)]">
      {items.map((item) => (
        <li key={item.label} className="inline-flex items-center gap-1.5">
          <span className="h-2 w-2 rounded-full" style={{ background: item.color }} />
          {item.label}
        </li>
      ))}
    </ul>
  );
}

function PaintDefs({
  uid,
  star,
}: {
  uid: string;
  star: ReturnType<typeof starLook>;
}) {
  return (
    <defs>
      <radialGradient id={`${uid}-star`} cx="38%" cy="34%" r="62%">
        <stop offset="0%" stopColor={star.core} />
        <stop offset="55%" stopColor={star.mid} />
        <stop offset="100%" stopColor={star.edge} />
      </radialGradient>
      <radialGradient id={`${uid}-glow`} cx="50%" cy="50%" r="50%">
        <stop offset="0%" stopColor={star.glow} stopOpacity="0.55" />
        <stop offset="70%" stopColor={star.glow} stopOpacity="0.12" />
        <stop offset="100%" stopColor={star.glow} stopOpacity="0" />
      </radialGradient>
      <radialGradient id={`${uid}-world`} cx="32%" cy="30%" r="70%">
        <stop offset="0%" stopColor="#9ad4ff" />
        <stop offset="38%" stopColor="#3d8f6e" />
        <stop offset="78%" stopColor="#1a4a3a" />
        <stop offset="100%" stopColor="#0b1c18" />
      </radialGradient>
      <radialGradient id={`${uid}-atm`} cx="50%" cy="50%" r="50%">
        <stop offset="62%" stopColor="#7ec8ff" stopOpacity="0" />
        <stop offset="82%" stopColor="#7ec8ff" stopOpacity="0.35" />
        <stop offset="100%" stopColor="#7ec8ff" stopOpacity="0" />
      </radialGradient>
      <radialGradient id={`${uid}-giant`} cx="30%" cy="28%" r="72%">
        <stop offset="0%" stopColor="#f3d9a4" />
        <stop offset="40%" stopColor="#c48a3a" />
        <stop offset="100%" stopColor="#4a2a12" />
      </radialGradient>
      <radialGradient id={`${uid}-ice`} cx="30%" cy="28%" r="72%">
        <stop offset="0%" stopColor="#dbeafe" />
        <stop offset="45%" stopColor="#38bdf8" />
        <stop offset="100%" stopColor="#0c4a6e" />
      </radialGradient>
      <radialGradient id={`${uid}-rocky`} cx="32%" cy="30%" r="70%">
        <stop offset="0%" stopColor="#e7c6a0" />
        <stop offset="55%" stopColor="#a16207" />
        <stop offset="100%" stopColor="#431407" />
      </radialGradient>
    </defs>
  );
}

function CometOrbit({
  comet,
  cx,
  cy,
  scale,
}: {
  comet: ExportComet;
  cx: number;
  cy: number;
  scale: (au: number) => number;
}) {
  const a = 0.5 * (comet.perihelionAu + comet.aphelionAu);
  const e = comet.eccentricity;
  const b = a * Math.sqrt(Math.max(0, 1 - e * e));
  const omega = comet.argumentOfPeriapsisRad;
  const focusOffset = scale(a * e);
  const ox = cx - Math.cos(omega) * focusOffset;
  const oy = cy - Math.sin(omega) * focusOffset;
  const deg = (omega * 180) / Math.PI;
  return (
    <ellipse
      cx={ox}
      cy={oy}
      rx={scale(a)}
      ry={scale(b)}
      transform={`rotate(${deg} ${ox} ${oy})`}
      fill="none"
      stroke="#94a3b8"
      strokeWidth="0.85"
      strokeDasharray="2 5"
      opacity="0.55"
    />
  );
}

function OrbitRing({
  cx,
  cy,
  radius,
  stroke,
  dashed,
  dotted,
}: {
  cx: number;
  cy: number;
  radius: number;
  stroke: string;
  dashed?: boolean;
  dotted?: boolean;
}) {
  return (
    <circle
      cx={cx}
      cy={cy}
      r={Math.max(radius, 0.5)}
      fill="none"
      stroke={stroke}
      strokeWidth={dashed ? 1.25 : 0.85}
      strokeDasharray={dotted ? '1 6' : dashed ? '3 4' : '1.5 5'}
      opacity={dashed ? 0.85 : 0.55}
    />
  );
}

function TerrestrialBody({
  uid,
  x,
  y,
  radius,
  name,
  note,
  labelAngle = Math.PI / 2,
}: {
  uid: string;
  x: number;
  y: number;
  radius: number;
  name?: string;
  note?: string;
  /** The label is thrown outward along the orbit's radius, away from the star's glare. */
  labelAngle?: number;
}) {
  const clipId = `${uid}-land-${Math.round(x)}-${Math.round(y)}`;

  return (
    <g>
      <defs>
        <clipPath id={clipId}>
          <circle cx={x} cy={y} r={radius} />
        </clipPath>
      </defs>
      <circle cx={x} cy={y} r={radius + 3} fill={`url(#${uid}-atm)`} />
      <circle cx={x} cy={y} r={radius} fill={`url(#${uid}-world)`} />
      <g clipPath={`url(#${clipId})`}>
        <ellipse
          cx={x - radius * 0.15}
          cy={y - radius * 0.1}
          rx={radius * 0.55}
          ry={radius * 0.28}
          fill="#2f6b4a"
          opacity="0.9"
        />
        <ellipse
          cx={x + radius * 0.35}
          cy={y + radius * 0.2}
          rx={radius * 0.42}
          ry={radius * 0.22}
          fill="#3d7a52"
          opacity="0.85"
        />
        <ellipse
          cx={x + radius * 0.45}
          cy={y}
          rx={radius * 0.55}
          ry={radius}
          fill="#02080c"
          opacity="0.42"
        />
      </g>
      <ellipse
        cx={x - radius * 0.32}
        cy={y - radius * 0.38}
        rx={radius * 0.28}
        ry={radius * 0.14}
        fill="#e8f6ff"
        opacity="0.35"
      />
      {name && (
        <>
          <line
            x1={x + Math.cos(labelAngle) * (radius + 2)}
            y1={y + Math.sin(labelAngle) * (radius + 2)}
            x2={x + Math.cos(labelAngle) * (radius + 10)}
            y2={y + Math.sin(labelAngle) * (radius + 10)}
            stroke="#86efac"
            strokeWidth="0.8"
            opacity="0.8"
          />
          <text
            x={x + Math.cos(labelAngle) * (radius + 13)}
            y={y + Math.sin(labelAngle) * (radius + 13)}
            textAnchor={Math.cos(labelAngle) >= 0 ? 'start' : 'end'}
            fill="#86efac"
            fontSize="11"
          >
            {name}
          </text>
          {note && (
            <text
              x={x + Math.cos(labelAngle) * (radius + 13)}
              y={y + Math.sin(labelAngle) * (radius + 13) + 11}
              textAnchor={Math.cos(labelAngle) >= 0 ? 'start' : 'end'}
              fill="#4ade80"
              fontSize="9"
              opacity="0.85"
            >
              {note}
            </text>
          )}
        </>
      )}
    </g>
  );
}

function MoonSystem({
  uid,
  gx,
  gy,
  giantR,
  moonR,
  name,
  moons,
  appearance,
  labeled,
}: {
  uid: string;
  gx: number;
  gy: number;
  giantR: number;
  moonR: number;
  name: string;
  moons: ExportSystemMoon[];
  appearance?: ExportGiantAppearance;
  labeled: boolean;
}) {
  const family = moons.length > 0 ? moons : [{
    index: 1,
    orbitalDistanceEarthRadii: 12,
    massEarth: 1,
    radiusEarth: 1,
    dayLengthDays: 1,
    habitable: true,
  }];
  const farthest = Math.max(...family.map((moon) => moon.orbitalDistanceEarthRadii));
  const reach = labeled ? 26 : 12;
  const clipId = `${uid}-parent-${Math.round(gx)}-${Math.round(gy)}`;

  return (
    <g>
      <defs>
        <clipPath id={clipId}>
          <circle cx={gx} cy={gy} r={giantR} />
        </clipPath>
      </defs>
      {family.map((moon) => {
        const orbit = giantR + 3 + (moon.orbitalDistanceEarthRadii / farthest) * reach;
        return (
          <circle
            key={`orbit-${moon.index}`}
            cx={gx}
            cy={gy}
            r={orbit}
            fill="none"
            stroke="var(--outline-variant)"
            strokeWidth="0.6"
            strokeDasharray="2 3"
            opacity="0.45"
          />
        );
      })}
      {appearance?.ring && (
        <PlanetRingGlyph x={gx} y={gy} size={giantR} appearance={appearance} />
      )}
      <circle cx={gx} cy={gy} r={giantR} fill={`url(#${uid}-giant)`} />
      <g clipPath={`url(#${clipId})`}>
        <ellipse cx={gx} cy={gy - giantR * 0.2} rx={giantR} ry={giantR * 0.16} fill="#e8c078" opacity="0.45" />
        <ellipse
          cx={gx + giantR * 0.5}
          cy={gy}
          rx={giantR * 0.5}
          ry={giantR}
          fill="#1a0c04"
          opacity="0.38"
        />
      </g>
      {family.map((moon) => {
        const orbit = giantR + 3 + (moon.orbitalDistanceEarthRadii / farthest) * reach;
        const angle = -0.4 + moon.index * 0.7;
        const x = gx + Math.cos(angle) * orbit;
        const y = gy + Math.sin(angle) * orbit;
        if (moon.habitable) {
          return (
            <TerrestrialBody
              key={moon.index}
              uid={uid}
              x={x}
              y={y}
              radius={moonR}
              name={labeled ? name : undefined}
            />
          );
        }
        return <circle key={moon.index} cx={x} cy={y} r={labeled ? 2.4 : 1.6} fill="#94a3b8" />;
      })}
    </g>
  );
}

function CompanionBody({
  uid,
  body,
  cx,
  cy,
  radius,
  angle,
  labeled,
}: {
  uid: string;
  body: ExportCompanionPlanet;
  cx: number;
  cy: number;
  radius: number;
  angle: number;
  labeled: boolean;
}) {
  const { language } = useLanguage();
  const tr = (value: string) => cosmologyText(language, value);
  const x = cx + Math.cos(angle) * radius;
  const y = cy + Math.sin(angle) * radius;
  const size =
    body.role === 'ShepherdGiant'
      ? 10
      : body.role === 'OuterGasGiant'
        ? 8
        : body.role === 'OuterIceGiant'
          ? 7
          : 4;
  const fill =
    body.role === 'ShepherdGiant' || body.role === 'OuterGasGiant'
      ? `url(#${uid}-giant)`
      : body.role === 'OuterIceGiant'
        ? `url(#${uid}-ice)`
        : `url(#${uid}-rocky)`;
  const clipId = `${uid}-comp-${body.role}-${Math.round(x)}-${Math.round(y)}`;
  const ring = body.appearance?.ring;

  return (
    <g>
      <defs>
        <clipPath id={clipId}>
          <circle cx={x} cy={y} r={size} />
        </clipPath>
      </defs>
      {ring && <PlanetRingGlyph x={x} y={y} size={size} appearance={body.appearance!} />}
      <circle cx={x} cy={y} r={size} fill={fill} />
      {body.role !== 'InnerRocky' && (
        <g clipPath={`url(#${clipId})`}>
          <ellipse
            cx={x + size * 0.45}
            cy={y}
            rx={size * 0.5}
            ry={size}
            fill="#020617"
            opacity="0.35"
          />
        </g>
      )}
      {labeled && (
        <text x={x} y={y + size + 12} textAnchor="middle" fill="var(--ink-soft)" fontSize="9">
          {tr(shortCompanionLabel(body.role))}
        </text>
      )}
    </g>
  );
}

/**
 * A giant's rings, drawn in its equatorial plane: the obliquity decides how far open the ellipse
 * is, and the ascending node decides which way the ring line runs. A ring seen edge-on collapses
 * to a line, which is what it does from the ground too.
 */
function PlanetRingGlyph({
  x,
  y,
  size,
  appearance,
}: {
  x: number;
  y: number;
  size: number;
  appearance: ExportGiantAppearance;
}) {
  const ring = appearance.ring;
  if (!ring) return null;

  const tint = tintToCss(ring.tint);
  const roll = ((appearance.ascendingNodeDeg % 180) - 90) * (appearance.retrograde ? -1 : 1);
  const rx = size * ring.outerRadiusPlanetRadii;
  const ry = Math.max(0.6, rx * appearance.ringOpenness);
  const innerRx = size * ring.innerRadiusPlanetRadii;
  const width = Math.max(0.8, rx - innerRx);

  return (
    <g transform={`rotate(${roll.toFixed(1)} ${x} ${y})`}>
      <ellipse
        cx={x}
        cy={y}
        rx={(innerRx + rx) / 2}
        ry={Math.max(0.4, ((innerRx / rx) * ry + ry) / 2)}
        fill="none"
        stroke={tint}
        strokeWidth={width}
        opacity={0.25 + 0.6 * ring.opticalDepth}
      />
      {ring.divisionRadiusPlanetRadii > ring.innerRadiusPlanetRadii && (
        <ellipse
          cx={x}
          cy={y}
          rx={size * ring.divisionRadiusPlanetRadii}
          ry={Math.max(0.3, size * ring.divisionRadiusPlanetRadii * appearance.ringOpenness)}
          fill="none"
          stroke="var(--bg)"
          strokeWidth={Math.max(0.4, width * 0.22)}
          opacity={0.8}
        />
      )}
    </g>
  );
}

/** Linear 0-1 channels as the engine rolls them, into something CSS understands. */
function tintToCss(tint: ExportTint): string {
  const channel = (value: number) => Math.round(Math.max(0, Math.min(1, value)) * 255);
  return `rgb(${channel(tint.r)}, ${channel(tint.g)}, ${channel(tint.b)})`;
}

function shortCompanionLabel(role: CompanionRole): string {
  switch (role) {
    case 'InnerRocky':
      return 'Inner rocky';
    case 'ShepherdGiant':
      return 'Shepherd';
    case 'OuterIceGiant':
      return 'Ice giant';
    case 'OuterGasGiant':
      return 'Gas giant';
    case 'HostGiant':
      return 'Host giant';
    default:
      return role;
  }
}

function companionColor(role: CompanionRole): string {
  switch (role) {
    case 'InnerRocky':
      return '#a16207';
    case 'ShepherdGiant':
      return '#c48a3a';
    case 'OuterIceGiant':
      return '#38bdf8';
    case 'OuterGasGiant':
      return '#d8a45c';
    case 'HostGiant':
      return '#d4b483';
    default:
      return '#94a3b8';
  }
}

function starLook(starClass: StarSpectralClass, size: number) {
  switch (starClass) {
    case 'M':
      return { core: '#ffd0a8', mid: '#ff6b35', edge: '#8a1c0a', glow: '#ff5a1f', size };
    case 'K':
      return { core: '#fff1c8', mid: '#ff9f43', edge: '#b45309', glow: '#f59e0b', size };
    case 'G':
      return { core: '#fffce8', mid: '#ffd166', edge: '#ca8a04', glow: '#facc15', size };
    case 'F':
      return { core: '#ffffff', mid: '#fff3c4', edge: '#fde68a', glow: '#fef08a', size };
    default:
      return { core: '#fffce8', mid: '#ffd166', edge: '#ca8a04', glow: '#facc15', size };
  }
}

/** Exact translations for presentation strings; unknown source wording is retained. */
function cosmologyText(language: string, value: string): string {
  return translateIn(language, 'cosmology', value);
}
