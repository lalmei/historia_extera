import type {
  CompanionRole,
  ExportCompanionPlanet,
  ExportComet,
  ExportCosmology,
  ExportGalaxy,
  ExportSystemMoon,
  ExportWorld,
  GalaxyMorphology,
  StarSpectralClass,
  WorldKind,
} from '../types';
import { COMPANION_ROLE_LABELS } from '../types';
import { PageTitle, Panel } from './common';
import type { World } from '../store';
import { NightSky } from './NightSky';

export function CosmologyPage({ world }: { world: World }) {
  const { designation, name, kind, cosmology } = world.export.world;
  const beginning = world.export.events.find((event) => event.kind === 'WorldCreated');
  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={designation || name}
        title="Cosmology"
        meta={
          cosmology ? (
            <span className="text-[var(--ink-faint)]">
              {cosmology.galaxy
                ? `${morphologyLabel(cosmology.galaxy.morphology)} · `
                : ''}
              {cosmology.starClass}-type · {kind === 'Moon' ? 'habitable moon' : 'habitable planet'}
            </span>
          ) : undefined
        }
      />
      <CosmicTimeline
        data={beginning?.data}
        startYear={world.export.meta.startYear}
        metallicityFeH={cosmology?.galaxy?.location.metallicityFeH}
      />
      <CosmologyPanel world={world.export.world} seed={world.export.meta.seed} />
    </div>
  );
}

function CosmicTimeline({
  data,
  startYear,
  metallicityFeH,
}: {
  data?: Record<string, string>;
  startYear: number;
  metallicityFeH?: number;
}) {
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

  const iron = metallicityFeH == null
    ? 'metal-rich gas'
    : `gas at [Fe/H] ${metallicityFeH >= 0 ? '+' : ''}${metallicityFeH.toFixed(2)}`;

  return (
    <Panel title="Cosmic timeline">
      <p className="mb-4 text-sm text-[var(--ink-soft)]">
        Lookback dates are measured from Year {startYear}. The Chronicle begins there; it does not
        use civic years for events billions of years earlier.
      </p>
      <ol className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        <CosmicMoment
          when={`${data.universeAgeGyr} Gyr ago`}
          title="The universe began"
          detail="Hydrogen and helium came first; the iron and rock needed by this world did not yet exist."
        />
        <CosmicMoment
          when={`${data.galaxyAgeGyr} Gyr ago`}
          title="The host galaxy began assembling"
          detail="Its first stars formed from nearly pristine gas while the galaxy was still growing."
        />
        <CosmicMoment
          when={`${data.stellarEnrichmentGyr} Gyr of enrichment`}
          title="Earlier stellar generations lived and died"
          detail={`Massive-star explosions and delayed Type Ia supernovae enriched ${iron}, supplying the iron and other heavy elements used by the later system.`}
        />
        <CosmicMoment
          when={`${data.starAgeGyr} Gyr ago`}
          title="The host star and its disk formed"
          detail="A metal-enriched molecular cloud collapsed into the star and a surrounding protoplanetary disk."
        />
        <CosmicMoment
          when={`${data.worldAgeGyr} Gyr ago`}
          title="The history world assembled"
          detail={`Its last large accretion followed the star by about ${data.worldFormationDelayMyr} million years.`}
        />
        <CosmicMoment
          when={`Year ${startYear}`}
          title="Recorded history began"
          detail="This is the Chronicle's first year, not the physical creation date of the galaxy, system, or world."
        />
        <CosmicMoment
          when={`In ${data.starRemainingGyr} Gyr`}
          title={`The host star becomes a ${data.starNextStage}`}
          detail={data.stellarFuture}
          future
        />
      </ol>
    </Panel>
  );
}

function CosmicMoment({
  when,
  title,
  detail,
  future = false,
}: {
  when: string;
  title: string;
  detail: string;
  future?: boolean;
}) {
  return (
    <li
      className={`rounded-md border p-3 ${
        future
          ? 'border-[color-mix(in_srgb,var(--primary)_45%,var(--rule))] bg-[color-mix(in_srgb,var(--primary)_7%,var(--panel))]'
          : 'border-[var(--rule)] bg-[var(--input)]'
      }`}
    >
      <div className="he-label mb-1">{when}</div>
      <div className="text-sm font-semibold text-[var(--ink)]">{title}</div>
      <p className="mt-1 text-xs leading-relaxed text-[var(--ink-soft)]">{detail}</p>
    </li>
  );
}

/**
 * Orbital habitability map and diagnostics for the world's host star system.
 *
 * Two scales on purpose: a shepherd beyond the snow line is several times farther
 * out than the habitable zone, so one diagram cannot show both without crushing
 * the inner system. The zone view is fitted to liquid-water orbits; the system
 * view is fitted to the outermost planet.
 */
export function CosmologyPanel({ world, seed }: { world: ExportWorld; seed?: number }) {
  const c = world.cosmology;
  if (!c) {
    return (
      <Panel title="Cosmology">
        <p className="text-sm text-[var(--ink-soft)]">
          This export predates stellar-system physics — regenerate the world to see habitable-zone
          diagnostics.
        </p>
      </Panel>
    );
  }

  const surfaceC = c.surfaceTempK - 273.15;
  const eqC = c.equilibriumTempK - 273.15;
  const moons = c.moons ?? [];
  const moonOrbitR =
    c.moonOrbitalDistanceEarthRadii ?? moons.find((moon) => moon.habitable)?.orbitalDistanceEarthRadii;

  return (
    <div className="space-y-5">
      <p className="mb-4 text-sm text-[var(--ink-soft)]">
        {world.kind === 'Moon'
          ? 'A tidally locked habitable moon orbiting a gas giant inside the star\'s liquid-water zone. An outer shepherd giant beyond the snow line keeps leftover rock from raining inward.'
          : 'A standalone planet inside the host star\'s liquid-water habitable zone. A shepherd giant beyond the snow line clears leftover planetesimals so the world is not late-bombarded for gigayears.'}
        {c.galaxy
          ? ` The system sits in a ${morphologyLabel(c.galaxy.morphology)} galaxy, ${c.galaxy.location.galactocentricRadiusKpc.toFixed(1)} kpc from the centre.`
          : ''}
      </p>

      {c.galaxy && (
        <div className="grid gap-4 lg:grid-cols-2">
          <div>
            <div className="he-label mb-2">Host galaxy, face-on</div>
            <GalaxyView galaxy={c.galaxy} mode="face" />
            <p className="mt-2 text-xs text-[var(--ink-faint)]">
              Gold ring is the habitable annulus; the mark is this world
            </p>
          </div>
          <div>
            <div className="he-label mb-2">Host galaxy, edge-on</div>
            <GalaxyView galaxy={c.galaxy} mode="edge" />
            <p className="mt-2 text-xs text-[var(--ink-faint)]">
              Height against galactocentric radius · same mark
            </p>
          </div>
        </div>
      )}

      {c.galaxy && (
        <NightSky galaxy={c.galaxy} seed={seed ?? 0} />
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        <div>
          <div className="he-label mb-2">Habitable zone</div>
          <SystemView cosmology={c} kind={world.kind} name={world.name} mode="zone" />
          <p className="mt-2 text-xs text-[var(--ink-faint)]">
            Fitted to the liquid-water belt around this {starLabel(c.starClass)} (
            {c.starMassSolar.toFixed(2)} M☉)
          </p>
        </div>
        <div>
          <div className="he-label mb-2">Full system</div>
          <SystemView cosmology={c} kind={world.kind} name={world.name} mode="system" />
          <p className="mt-2 text-xs text-[var(--ink-faint)]">
            Fitted to the outermost planet · map icons are markers, not radii
          </p>
        </div>
      </div>

      <SizeStrip cosmology={c} kind={world.kind} name={world.name} />
      <MapLegend cosmology={c} kind={world.kind} name={world.name} />

      <div className="mt-6 grid gap-6 lg:grid-cols-2">
        <div>
          <div className="he-label mb-2">Calculated diagnostics</div>
          <dl className="space-y-2 text-sm">
            <Diag
              label="Habitable zone"
              value={`${c.habitableZoneInnerAu.toFixed(2)}–${c.habitableZoneOuterAu.toFixed(2)} AU`}
            />
            {c.galaxy && (
              <>
                <Diag
                  label="Host galaxy"
                  value={`${morphologyLabel(c.galaxy.morphology)}${c.galaxy.spiralArmCount > 0 ? `, ${c.galaxy.spiralArmCount} arms` : ''}`}
                />
                <Diag
                  label="Galactocentric site"
                  value={`R ${c.galaxy.location.galactocentricRadiusKpc.toFixed(1)} kpc · z ${c.galaxy.location.heightPc.toFixed(0)} pc`}
                />
                <Diag
                  label="[Fe/H]"
                  value={`${c.galaxy.location.metallicityFeH >= 0 ? '+' : ''}${c.galaxy.location.metallicityFeH.toFixed(2)} · iron ${c.galaxy.canHostIronCore ? 'yes' : 'no'} · ores ${c.galaxy.canHostOres ? 'yes' : 'no'}`}
                />
                <Diag
                  label="Spiral arm"
                  value={
                    c.galaxy.morphology === 'Elliptical'
                      ? 'none'
                      : c.galaxy.location.inSpiralArm
                        ? 'inside an arm'
                        : 'interarm'
                  }
                />
              </>
            )}
            <Diag
              label={world.kind === 'Moon' ? 'Parent distance to star' : 'Distance to star'}
              value={`${c.orbitalDistanceAu.toFixed(3)} AU`}
            />
            <Diag
              label={world.kind === 'Moon' ? 'Parent year' : 'Year length'}
              value={`${Math.round(c.orbitalPeriodDays)} Earth days`}
            />
            {world.kind === 'Moon' && moonOrbitR != null && (
              <Diag label="Distance to parent" value={formatMoonOrbit(moonOrbitR)} />
            )}
            {world.kind === 'Moon' && c.rocheLimitEarthRadii != null && (
              <Diag label="Roche limit" value={`${c.rocheLimitEarthRadii.toFixed(1)} R⊕`} />
            )}
            {world.kind === 'Moon' && c.moonDayLengthDays != null && (
              <Diag
                label="Tidal day length"
                value={`${c.moonDayLengthDays.toFixed(1)} Earth days`}
              />
            )}
            {world.kind === 'Moon' && c.parentGiantMassEarth != null && (
              <Diag
                label="Parent giant mass"
                value={`${c.parentGiantMassEarth.toFixed(0)} M⊕`}
              />
            )}
            {moons
              .filter((moon) => !moon.habitable)
              .map((moon) => (
                <Diag
                  key={`moon-${moon.index}`}
                  label={`Moon ${moon.index} of parent`}
                  value={`${formatMoonOrbit(moon.orbitalDistanceEarthRadii)} · ${moon.massEarth.toFixed(2)} M⊕ · ${moon.dayLengthDays.toFixed(1)} d`}
                />
              ))}
            {(c.companions ?? []).map((body) => (
              <Diag
                key={`${body.role}-${body.semiMajorAxisAu}`}
                label={COMPANION_ROLE_LABELS[body.role] ?? body.role}
                value={`${body.semiMajorAxisAu.toFixed(2)} AU · ${Math.round(body.orbitalPeriodDays)} d · ${body.massEarth.toFixed(body.role === 'InnerRocky' ? 2 : 0)} M⊕`}
              />
            ))}
            {(c.comets ?? []).map((comet) => (
              <Diag
                key={`comet-${comet.index}`}
                label={`Comet ${comet.index}`}
                value={`q ${comet.perihelionAu.toFixed(2)} AU · Q ${comet.aphelionAu.toFixed(1)} AU · ${comet.nucleusRadiusKm.toFixed(1)} km · ${formatMassEarth(comet.massEarth)}`}
              />
            ))}
            {c.snowLineAu != null && (
              <Diag label="Snow line" value={`${c.snowLineAu.toFixed(2)} AU`} />
            )}
            <Diag label="Star lifespan" value={`${c.starLifespanGyr.toFixed(1)} Gyr`} />
            <Diag label="Luminosity" value={`${c.luminositySolar.toFixed(3)} L☉`} />
            <Diag
              label="World mass / radius"
              value={`${c.worldMassEarth.toFixed(2)} M⊕ · ${c.worldRadiusEarth.toFixed(2)} R⊕`}
            />
            <Diag label="Surface gravity" value={`${c.surfaceGravityG.toFixed(2)} g`} />
            <Diag label="Escape velocity" value={`${c.escapeVelocityKmS.toFixed(1)} km/s`} />
            <Diag
              label="Equilibrium temp (no air)"
              value={`${eqC.toFixed(0)} °C (${c.equilibriumTempK.toFixed(0)} K)`}
            />
            <Diag
              label="Surface temp"
              value={`${surfaceC.toFixed(0)} °C (${c.surfaceTempK.toFixed(0)} K)`}
            />
          </dl>
        </div>

        <div className="rounded-md border border-[var(--rule)] bg-[var(--input)] p-3">
          <div className="he-label mb-2">Consistency engine</div>
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
                {check.passed ? '✓' : '✗'} {check.detail}
              </li>
            ))}
          </ul>
          {c.isHabitable && (
            <p className="mt-2 text-xs font-semibold text-[var(--primary)]">
              All checks passed — liquid surface water is physically plausible.
            </p>
          )}
        </div>
      </div>
    </div>
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

function formatCometPeriod(days: number): string {
  const years = days / 365.25;
  if (years >= 100) return `${Math.round(years).toLocaleString()} yr`;
  if (years >= 2) return `${years.toFixed(1)} yr`;
  return `${Math.round(days)} d`;
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
      className="h-64 w-full rounded-md border border-[var(--rule)] bg-[var(--canvas)]"
      role="img"
      aria-label="Face-on host galaxy"
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
      className="h-64 w-full rounded-md border border-[var(--rule)] bg-[var(--canvas)]"
      role="img"
      aria-label="Edge-on host galaxy"
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
}: {
  cosmology: ExportCosmology;
  kind: WorldKind;
  name: string;
  mode: 'zone' | 'system';
}) {
  const companions = c.companions ?? [];
  const comets = c.comets ?? [];
  const width = 480;
  const height = 300;
  const cx = width / 2;
  const cy = height / 2;
  const farthestPlanet = companions.reduce(
    (max, body) => Math.max(max, body.semiMajorAxisAu),
    Math.max(c.habitableZoneOuterAu, c.orbitalDistanceAu, c.snowLineAu ?? 0),
  );
  const farthest = comets.reduce(
    (max, comet) => Math.max(max, Math.min(comet.aphelionAu, farthestPlanet * 2.4), comet.perihelionAu),
    farthestPlanet,
  );
  const maxAu = mode === 'zone' ? c.habitableZoneOuterAu * 1.45 : farthest * 1.12;
  const maxR = Math.min(cx, cy) - 28;
  const r = (au: number) => (au / maxAu) * maxR;
  const uid = `${mode}-${c.starClass}-${Math.round(c.orbitalDistanceAu * 1000)}`;
  const star = starLook(c.starClass, mode === 'zone' ? 9 : 6);
  const isMoon = kind === 'Moon';
  const compact = mode === 'system';
  const worldR = compact ? 4 : 6;
  const giantR = isMoon ? (compact ? 6 : 10) : 0;
  const moons = c.moons ?? [];
  const angle = mode === 'zone' ? -0.55 : -0.35;
  const orbitR = r(c.orbitalDistanceAu);
  const worldX = cx + Math.cos(angle) * orbitR;
  const worldY = cy + Math.sin(angle) * orbitR;
  const visibleCompanions = companions.filter((body) => body.semiMajorAxisAu <= maxAu * 0.98);

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      className="h-64 w-full rounded-md border border-[var(--rule)] bg-[var(--canvas)]"
      role="img"
      aria-label={
        mode === 'zone'
          ? `Habitable zone of ${name}`
          : `Full system around ${starLabel(c.starClass)}`
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
          angle={1.2 + index * 1.4}
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
          labeled={mode === 'zone'}
        />
      ) : (
        <TerrestrialBody
          uid={uid}
          x={worldX}
          y={worldY}
          radius={worldR}
          name={mode === 'zone' ? name : undefined}
        />
      )}
    </svg>
  );
}

function MapLegend({
  cosmology: c,
  kind,
  name,
}: {
  cosmology: ExportCosmology;
  kind: WorldKind;
  name: string;
}) {
  const items = [
    { color: '#ef4444', label: `Inner HZ ${c.habitableZoneInnerAu.toFixed(2)} AU` },
    { color: '#60a5fa', label: `Outer HZ ${c.habitableZoneOuterAu.toFixed(2)} AU` },
    ...(kind === 'Moon'
      ? [
          {
            color: '#d4b483',
            label: `Parent giant · ${c.orbitalDistanceAu.toFixed(2)} AU from star`,
          },
          ...(c.moons ?? []).map((moon) => ({
            color: moon.habitable ? '#86efac' : '#94a3b8',
            label: `${moon.habitable ? name : `Moon ${moon.index}`} · ${formatMoonOrbit(moon.orbitalDistanceEarthRadii)} from parent`,
          })),
        ]
      : [
          {
            color: '#86efac',
            label: `${name} · ${c.orbitalDistanceAu.toFixed(2)} AU from star`,
          },
        ]),
    ...(c.snowLineAu != null
      ? [{ color: '#94a3b8', label: `Snow line ${c.snowLineAu.toFixed(2)} AU` }]
      : []),
    ...(c.companions ?? []).map((body) => ({
      color: companionColor(body.role),
      label: `${COMPANION_ROLE_LABELS[body.role] ?? body.role} · ${body.semiMajorAxisAu.toFixed(2)} AU from star`,
    })),
    ...(c.comets ?? []).map((comet) => ({
      color: '#cbd5e1',
      label: `Comet ${comet.index} · q ${comet.perihelionAu.toFixed(2)} AU · ${formatCometPeriod(comet.orbitalPeriodDays)}`,
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

function SizeStrip({
  cosmology: c,
  kind,
  name,
}: {
  cosmology: ExportCosmology;
  kind: WorldKind;
  name: string;
}) {
  const starEarth = (c.starRadiusSolar ?? 1) * 109.2;
  const starMassEarth = (c.starMassSolar ?? 1) * EARTH_MASSES_PER_SOLAR;
  const parentEarth =
    kind === 'Moon' && c.parentGiantMassEarth != null
      ? 2 * Math.sqrt(Math.sqrt(c.parentGiantMassEarth))
      : undefined;
  const moons = c.moons ?? [];
  const habitableMoon = moons.find((moon) => moon.habitable);
  const comets = c.comets ?? [];
  const bodies = [
    {
      label: starLabel(c.starClass),
      radiusDetail: `${(c.starRadiusSolar ?? 1).toFixed(2)} R☉`,
      massDetail: formatMassEarth(starMassEarth, c.starMassSolar),
      radius: starEarth,
      mass: starMassEarth,
      color: '#fbbf24',
    },
    ...(c.companions ?? [])
      .filter((body) => body.role === 'InnerRocky')
      .map((body) => ({
        label: 'Inner rocky',
        radiusDetail: `${body.radiusEarth.toFixed(2)} R⊕`,
        massDetail: formatMassEarth(body.massEarth),
        radius: body.radiusEarth,
        mass: body.massEarth,
        color: companionColor(body.role),
      })),
    ...(parentEarth != null && c.parentGiantMassEarth != null
      ? [{
          label: 'Parent giant',
          radiusDetail: `${parentEarth.toFixed(1)} R⊕`,
          massDetail: formatMassEarth(c.parentGiantMassEarth),
          radius: parentEarth,
          mass: c.parentGiantMassEarth,
          color: '#c48a3a',
        }]
      : []),
    ...moons.map((moon) => ({
      label: moon.habitable ? name : `Moon ${moon.index}`,
      radiusDetail: `${moon.radiusEarth.toFixed(2)} R⊕`,
      massDetail: formatMassEarth(moon.massEarth),
      radius: moon.radiusEarth,
      mass: moon.massEarth,
      color: moon.habitable ? '#4ade80' : '#94a3b8',
    })),
    ...(kind === 'Planet' || !habitableMoon
      ? [{
          label: name,
          radiusDetail: `${c.worldRadiusEarth.toFixed(2)} R⊕`,
          massDetail: formatMassEarth(c.worldMassEarth),
          radius: c.worldRadiusEarth,
          mass: c.worldMassEarth,
          color: '#4ade80',
        }]
      : []),
    ...(c.companions ?? [])
      .filter((body) => body.role !== 'InnerRocky')
      .map((body) => ({
        label: COMPANION_ROLE_LABELS[body.role] ?? body.role,
        radiusDetail: `${body.radiusEarth.toFixed(1)} R⊕`,
        massDetail: formatMassEarth(body.massEarth),
        radius: body.radiusEarth,
        mass: body.massEarth,
        color: companionColor(body.role),
      })),
    ...comets.map((comet) => ({
      label: `Comet ${comet.index}`,
      radiusDetail: `${comet.nucleusRadiusKm.toFixed(1)} km`,
      massDetail: formatMassEarth(comet.massEarth),
      radius: comet.nucleusRadiusKm / EARTH_KM,
      mass: comet.massEarth,
      color: '#cbd5e1',
    })),
  ];
  const minR = Math.min(...bodies.map((b) => b.radius));
  const maxR = Math.max(...bodies.map((b) => b.radius));
  const minM = Math.min(...bodies.map((b) => b.mass));
  const maxM = Math.max(...bodies.map((b) => b.mass));
  const widthOf = (value: number, lo: number, hi: number) => {
    if (hi <= lo) return 28;
    const t = (Math.log10(value) - Math.log10(lo)) / (Math.log10(hi) - Math.log10(lo));
    return 12 + t * 56;
  };

  return (
    <div className="mt-4 rounded-md border border-[var(--rule)] bg-[var(--input)] px-3 py-2.5">
      <div className="he-label mb-2">True radii</div>
      <div className="flex flex-wrap items-end gap-6">
        {bodies.map((body) => {
          const size = widthOf(body.radius, minR, maxR);
          return (
            <div key={`${body.label}-${body.radiusDetail}`} className="flex flex-col items-center gap-1.5">
              <span
                className="rounded-full"
                style={{ width: size, height: size, background: body.color }}
              />
              <span className="text-[11px] text-[var(--ink)]">{body.label}</span>
              <span className="he-data text-[11px] text-[var(--ink-faint)]">{body.radiusDetail}</span>
              <span className="he-data text-[11px] text-[var(--ink-faint)]">{body.massDetail}</span>
            </div>
          );
        })}
      </div>
      <div className="he-label mb-2 mt-4">Relative masses</div>
      <div className="flex flex-wrap items-end gap-6">
        {bodies.map((body) => {
          const size = widthOf(body.mass, minM, maxM);
          return (
            <div key={`mass-${body.label}-${body.massDetail}`} className="flex flex-col items-center gap-1.5">
              <span
                className="rounded-full"
                style={{ width: size, height: size, background: body.color }}
              />
              <span className="text-[11px] text-[var(--ink)]">{body.label}</span>
              <span className="he-data text-[11px] text-[var(--ink-faint)]">{body.massDetail}</span>
            </div>
          );
        })}
      </div>
      <p className="mt-2 text-[11px] text-[var(--ink-faint)]">
        Both rows are log-scaled so the star and a comet nucleus can share a strip. The orbital
        maps above use fixed marker sizes — a star drawn at the world&apos;s pixel scale would
        cover the whole diagram.
      </p>
    </div>
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
}: {
  uid: string;
  x: number;
  y: number;
  radius: number;
  name?: string;
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
        <text x={x} y={y + radius + 14} textAnchor="middle" fill="#86efac" fontSize="10">
          {name}
        </text>
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
  labeled,
}: {
  uid: string;
  gx: number;
  gy: number;
  giantR: number;
  moonR: number;
  name: string;
  moons: ExportSystemMoon[];
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
  const x = cx + Math.cos(angle) * radius;
  const y = cy + Math.sin(angle) * radius;
  const size =
    body.role === 'ShepherdGiant' ? 10 : body.role === 'OuterIceGiant' ? 7 : 4;
  const fill =
    body.role === 'ShepherdGiant'
      ? `url(#${uid}-giant)`
      : body.role === 'OuterIceGiant'
        ? `url(#${uid}-ice)`
        : `url(#${uid}-rocky)`;
  const clipId = `${uid}-comp-${body.role}-${Math.round(x)}-${Math.round(y)}`;

  return (
    <g>
      <defs>
        <clipPath id={clipId}>
          <circle cx={x} cy={y} r={size} />
        </clipPath>
      </defs>
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
          {shortCompanionLabel(body.role)}
        </text>
      )}
    </g>
  );
}

function shortCompanionLabel(role: CompanionRole): string {
  switch (role) {
    case 'InnerRocky':
      return 'Inner rocky';
    case 'ShepherdGiant':
      return 'Shepherd';
    case 'OuterIceGiant':
      return 'Ice giant';
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
