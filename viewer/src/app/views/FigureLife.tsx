import { useMemo, useRef, useState, type ReactNode } from 'react';
import {
  buildConstellation,
  historicalSignificance,
  knownFor,
  ripplesAfter,
  standingAt,
  standingSentence,
  type LifeArc,
  type LifeContext,
  type LifeMomentKind,
  type LifeStanding,
  type LifeVantage,
} from '../biography';
import { Badge, EntityLink, Panel } from '../components/common';
import {
  IconCrown,
  IconPeople,
  IconPerson,
  IconStar,
  IconSwords,
} from '../components/icons';
import type { World } from '../store';
import type { Figure, HistoryEvent } from '../types';

/**
 * The whole-life views of one person: the arc, the standing, the constellation, the reputation,
 * and what followed them.
 *
 * These sit apart from the rest of the entity pages because they share one idea: the engine
 * already knows all of this, and nobody can see it. A biography assembled from panels answers
 * "what is recorded about this person"; these answer "what was this life", which is the question
 * a reader actually arrived with. Every one of them is drawn from counts and dated records —
 * there is no generated prose anywhere in this file, and there should not be.
 */

const MOMENT_TONE: Record<LifeMomentKind, string> = {
  Birth: 'var(--ink-faint)',
  Trade: 'var(--tone-craft)',
  Rank: 'var(--tone-war)',
  Office: 'var(--tone-office)',
  Marriage: 'var(--tone-kin)',
  Children: 'var(--tone-kin)',
  Loss: 'var(--error)',
  Friendship: 'var(--tone-faith)',
  Wound: 'var(--tone-war)',
  Campaign: 'var(--tone-war)',
  Death: 'var(--ink-faint)',
};

/**
 * What each hue in the arc means, so the colour is a key rather than decoration.
 *
 * Grouped by what the turn was about rather than given a hue apiece: a marriage and a sixth
 * child are the same kind of fact about a life, and colouring them differently would say they
 * were not.
 */
const TONE_LEGEND: { tone: string; label: string }[] = [
  { tone: 'var(--tone-kin)', label: 'household' },
  { tone: 'var(--tone-office)', label: 'office' },
  { tone: 'var(--tone-war)', label: 'arms' },
  { tone: 'var(--tone-craft)', label: 'trade' },
  { tone: 'var(--tone-faith)', label: 'companionship' },
  { tone: 'var(--ink-faint)', label: 'birth and death' },
];

const SIGIL_TONES = [
  'var(--tone-kin)',
  'var(--tone-office)',
  'var(--tone-war)',
  'var(--tone-craft)',
  'var(--tone-faith)',
  'var(--tone-learning)',
];

/** Stable across pages and reloads: the same culture is the same colour wherever it appears. */
function toneFor(id: string | undefined): string {
  if (!id) return SIGIL_TONES[0];
  let hash = 0;
  for (let index = 0; index < id.length; index += 1) {
    hash = (hash * 31 + id.charCodeAt(index)) >>> 0;
  }
  return SIGIL_TONES[hash % SIGIL_TONES.length];
}

/**
 * A mark for a person, in place of a portrait we do not have and should not invent.
 *
 * Two things are true of it and neither is decoration: the colour is the culture's, so a page
 * of Swethil figures reads as one people, and the ring is the years the life covers set against
 * the years the record covers — a figure who spans a third of the history has a third of a ring.
 * A drawn face would be a claim about someone the simulation never described.
 */
export function FigureSigil({
  name,
  tone,
  fromYear,
  toYear,
  recordStart,
  recordEnd,
  size = 76,
}: {
  name: string;
  tone: string;
  fromYear: number;
  toYear: number;
  recordStart: number;
  recordEnd: number;
  size?: number;
}) {
  const span = Math.max(1, recordEnd - recordStart);
  const start = Math.min(1, Math.max(0, (fromYear - recordStart) / span));
  const end = Math.min(1, Math.max(0, (toYear - recordStart) / span));
  const radius = 34;
  const circumference = 2 * Math.PI * radius;
  const initial = name.trim().charAt(0).toUpperCase() || '?';

  return (
    <svg
      viewBox="0 0 88 88"
      width={size}
      height={size}
      role="img"
      aria-label={`${name}, ${fromYear} to ${toYear}`}
      className="shrink-0"
    >
      <rect
        x="1"
        y="1"
        width="86"
        height="86"
        rx="10"
        fill={`color-mix(in srgb, ${tone} 10%, var(--panel))`}
        stroke={`color-mix(in srgb, ${tone} 34%, transparent)`}
      />
      <circle
        cx="44"
        cy="44"
        r={radius}
        fill="none"
        stroke={`color-mix(in srgb, ${tone} 22%, transparent)`}
        strokeWidth="2"
      />
      <circle
        cx="44"
        cy="44"
        r={radius}
        fill="none"
        stroke={tone}
        strokeWidth="3"
        strokeLinecap="round"
        // Rotated so the ring starts at the top, where a reader expects a year zero.
        transform="rotate(-90 44 44)"
        strokeDasharray={`${Math.max(2, (end - start) * circumference)} ${circumference}`}
        strokeDashoffset={-start * circumference}
      />
      <text
        x="44"
        y="44"
        textAnchor="middle"
        dominantBaseline="central"
        fill={tone}
        fontSize="30"
        fontFamily="var(--font-mono)"
      >
        {initial}
      </text>
    </svg>
  );
}

/**
 * The top of a figure's page: who they were, in the year being read.
 *
 * It exists because the page used to open on a name and a date and then ask the reader to
 * assemble a person out of eleven panels. The five facts in the chip row are the same five the
 * standing readout carries, hoisted to where the eye lands first — and because they are read at
 * the selected year like everything else, the header is the fastest way to see that the year
 * control does something.
 */
export function FigureHero({
  world,
  figure,
  standing,
  eyebrow,
  cultureId,
  recordStart,
  recordEnd,
  significance,
  place,
}: {
  world: World;
  figure: Figure;
  standing: LifeStanding;
  eyebrow: string;
  cultureId?: string;
  recordStart: number;
  recordEnd: number;
  significance?: string;
  place?: string;
}) {
  const tone = toneFor(cultureId ?? figure.cultureId);
  const chips: { key: string; icon: ReactNode; fact: ReactNode; note: string; tone: string }[] = [
    {
      key: 'position',
      icon: figure.titles.length > 0 ? <IconCrown /> : <IconSwords />,
      fact: standing.position ?? 'No position on record',
      note: standing.position ? (place ?? 'place unrecorded') : 'nothing the record names',
      tone: figure.titles.length > 0 ? 'var(--tone-office)' : 'var(--tone-craft)',
    },
    {
      key: 'household',
      icon: <IconPeople />,
      fact: sentenceCase(standing.household),
      note:
        standing.childCount === 0
          ? 'no children'
          : `${standing.childCount} ${standing.childCount === 1 ? 'child' : 'children'}`,
      tone: 'var(--tone-kin)',
    },
    {
      key: 'closest',
      icon: <IconPerson />,
      fact: standing.closest ? <EntityLink world={world} id={standing.closest.id} /> : 'Nobody',
      note: standing.closest ? standing.closest.reading : 'stands close on record',
      tone: 'var(--tone-faith)',
    },
    {
      key: 'disposition',
      icon: <IconStar />,
      fact: standing.dominantDisposition ?? 'Even',
      note: standing.dominantDisposition ? 'runs strongest' : 'nothing runs strongly',
      tone: 'var(--tone-learning)',
    },
  ];

  return (
    <header className="mb-6 overflow-hidden rounded-lg border border-[var(--rule)] bg-[var(--panel)]">
      <div
        className="flex flex-wrap items-start gap-5 border-b border-[var(--rule)] p-5"
        style={{
          background: `linear-gradient(120deg, color-mix(in srgb, ${tone} 9%, transparent), transparent 60%)`,
        }}
      >
        <FigureSigil
          name={figure.name}
          tone={tone}
          fromYear={figure.birthYear}
          toYear={figure.deathYear ?? recordEnd}
          recordStart={recordStart}
          recordEnd={recordEnd}
        />
        <div className="min-w-[14rem] flex-1">
          <div className="he-label" style={{ color: `color-mix(in srgb, ${tone} 70%, var(--ink-faint))` }}>
            {eyebrow}
          </div>
          <h1 className="he-headline mt-1.5">{figure.name}</h1>
          <div className="mt-2.5 flex flex-wrap items-center gap-2 text-sm">
            <Badge tone={standing.alive ? 'accent' : 'muted'}>
              {standing.alive ? `Living in ${standing.year}` : 'Deceased'}
            </Badge>
            {significance && significance !== 'Ordinary' && <Badge tone="neutral">{significance}</Badge>}
            <span className="he-data text-[var(--ink-faint)]">
              {figure.deathYear === undefined
                ? `b. ${figure.birthYear}`
                : `${figure.birthYear}–${figure.deathYear}`}
              {' · aged '}
              {standing.age}
            </span>
          </div>
        </div>
      </div>

      <dl className="grid grid-cols-2 gap-px bg-[var(--rule)] sm:grid-cols-4">
        {chips.map((chip) => (
          <div key={chip.key} className="flex items-start gap-2.5 bg-[var(--panel)] px-4 py-3">
            <span aria-hidden style={{ color: chip.tone }} className="mt-0.5 text-base">
              {chip.icon}
            </span>
            <div className="min-w-0">
              <dt className="truncate text-sm text-[var(--ink)]">{chip.fact}</dt>
              <dd className="truncate text-xs text-[var(--ink-faint)]">{chip.note}</dd>
            </div>
          </div>
        ))}
      </dl>
    </header>
  );
}

function sentenceCase(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

/**
 * A life as one strip: the years that had something in them, and the turns that made it that life.
 *
 * <b>Retrospective on purpose, unlike every panel below it.</b> It is the page's scrubber as well
 * as its summary, and a scrubber that hides where it can scrub to is useless. The caption says so
 * rather than pretending the death it draws has not happened yet; the panels underneath keep the
 * contemporary discipline.
 *
 * The density behind the moments is the part that could not be written by hand: a year with nine
 * recorded events stands taller than a year with one, so a crowded decade is visible before a
 * single label is read.
 */
export function LifeArcStrip({
  arc,
  birthYear,
  selectedYear,
  scale,
  onSelectYear,
}: {
  arc: LifeArc;
  birthYear: number;
  selectedYear: number;
  /** Whether the strip counts in years of the world or years of the life. Same data either way. */
  scale: 'age' | 'year';
  onSelectYear: (year: number) => void;
}) {
  const trackRef = useRef<HTMLDivElement>(null);
  const reading = (year: number) => (scale === 'age' ? year - birthYear : year);
  const span = Math.max(1, arc.lastYear - arc.firstYear);
  const at = (year: number) => ((year - arc.firstYear) / span) * 100;

  // Clicking the track itself, not only a dot: the strip is the year control, and a reader who
  // wants "somewhere in his forties" has no dot to aim at.
  const pickYear = (clientX: number) => {
    const box = trackRef.current?.getBoundingClientRect();
    if (!box || box.width === 0) return;
    const fraction = Math.min(1, Math.max(0, (clientX - box.left) / box.width));
    onSelectYear(Math.round(arc.firstYear + fraction * span));
  };

  return (
    <div>
      <div
        ref={trackRef}
        onClick={(event) => pickYear(event.clientX)}
        className="relative h-20 cursor-pointer select-none"
      >
        <div className="absolute inset-x-0 bottom-6 h-14">
          {arc.density.map(({ year, weight }) => (
            <span
              key={year}
              title={`${reading(year)}`}
              className="absolute bottom-0 bg-[var(--accent-soft)]"
              style={{
                left: `${at(year)}%`,
                // Capped: a two-year life would otherwise draw one year as half the strip.
                width: `max(2px, ${Math.min(6, 100 / span)}%)`,
                height: `${8 + weight * 92}%`,
              }}
            />
          ))}
        </div>

        <div className="absolute inset-x-0 bottom-6 h-px bg-[var(--rule)]" />

        <div
          className="absolute bottom-4 w-px bg-[var(--primary)]"
          style={{ left: `${at(Math.min(Math.max(selectedYear, arc.firstYear), arc.lastYear))}%`, height: '3.75rem' }}
        >
          <span className="absolute -bottom-5 -translate-x-1/2 whitespace-nowrap text-[0.7rem] text-[var(--primary)]">
            {reading(selectedYear)}
          </span>
        </div>

        {arc.moments.map((moment) => (
          <button
            key={moment.key}
            type="button"
            title={moment.detail}
            aria-label={`${moment.label} — ${moment.detail}`}
            onClick={(event) => {
              event.stopPropagation();
              onSelectYear(moment.year);
            }}
            className="absolute bottom-6 h-2.5 w-2.5 -translate-x-1/2 translate-y-1/2 rounded-full border border-[var(--panel)] transition-transform hover:scale-150"
            style={{ left: `${at(moment.year)}%`, background: MOMENT_TONE[moment.kind] }}
          />
        ))}
      </div>

      <ol className="mt-4 flex flex-wrap items-center gap-x-1 gap-y-2 text-sm">
        {arc.moments.map((moment, index) => (
          <li key={moment.key} className="flex items-center gap-1">
            {index > 0 && <span className="text-[var(--ink-faint)]">→</span>}
            <button
              type="button"
              title={moment.detail}
              onClick={() => onSelectYear(moment.year)}
              className="rounded border px-2 py-0.5 text-xs transition-colors"
              style={
                moment.year === selectedYear
                  ? {
                      borderColor: MOMENT_TONE[moment.kind],
                      color: MOMENT_TONE[moment.kind],
                      background: `color-mix(in srgb, ${MOMENT_TONE[moment.kind]} 12%, transparent)`,
                    }
                  : { borderColor: 'var(--rule)', color: 'var(--ink-soft)' }
              }
            >
              <span
                aria-hidden
                className="mr-1.5 inline-block h-1.5 w-1.5 rounded-full align-middle"
                style={{ background: MOMENT_TONE[moment.kind] }}
              />
              {moment.label}
              <span className="ml-1.5 text-[var(--ink-faint)]">
                {scale === 'age' ? moment.age : moment.year}
              </span>
            </button>
          </li>
        ))}
      </ol>

      <p className="mt-3 text-xs leading-relaxed text-[var(--ink-faint)]">
        The whole life, retrospectively — the panels below show only what was known in the year
        selected. Bar height is how much the chronicle recorded in that year, counting what it
        marked notable for three; the number beside each turn is the {scale === 'age' ? 'age' : 'year'} it
        happened at, and its colour is what the turn was about. Click anywhere on the strip to move
        the year.
        {arc.busiestYear !== undefined && (
          <>
            {' '}
            Their busiest year was{' '}
            <strong className="text-[var(--ink-soft)]">
              {scale === 'age' ? `age ${reading(arc.busiestYear)}` : arc.busiestYear}
            </strong>
            .
          </>
        )}
      </p>

      <ul className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-[0.7rem] text-[var(--ink-faint)]">
        {TONE_LEGEND.map((entry) => (
          <li key={entry.label} className="flex items-center gap-1.5">
            <span
              aria-hidden
              className="inline-block h-1.5 w-1.5 rounded-full"
              style={{ background: entry.tone }}
            />
            {entry.label}
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * The arc with its own panel and its own scale control.
 *
 * <b>Age or year is not a formatting preference.</b> "Married at 33" is a fact about a person and
 * "married in 412" is a fact about a world, and a reader arrives wanting one or the other — the
 * same strip answers both, so it should not have to be redrawn to do it.
 */
export function LifeArcPanel({
  arc,
  birthYear,
  selectedYear,
  onSelectYear,
}: {
  arc: LifeArc;
  birthYear: number;
  selectedYear: number;
  onSelectYear: (year: number) => void;
}) {
  const [scale, setScale] = useState<'age' | 'year'>('age');

  return (
    <Panel
      title="Life arc"
      actions={
        <div
          role="group"
          aria-label="Life arc scale"
          className="flex overflow-hidden rounded border border-[var(--rule)]"
        >
          {(['age', 'year'] as const).map((option) => (
            <button
              key={option}
              type="button"
              aria-pressed={scale === option}
              onClick={() => setScale(option)}
              className={`px-2.5 py-1 text-xs capitalize transition-colors ${
                scale === option
                  ? 'bg-[var(--primary)] text-[var(--on-primary)]'
                  : 'text-[var(--ink-soft)] hover:text-[var(--primary)]'
              }`}
            >
              {option}
            </button>
          ))}
        </div>
      }
    >
      <LifeArcStrip
        arc={arc}
        birthYear={birthYear}
        selectedYear={selectedYear}
        scale={scale}
        onSelectYear={onSelectYear}
      />
    </Panel>
  );
}

/**
 * Who they were in the selected year, in a sentence that rewrites itself as it moves.
 *
 * The slider was conceptually the most interesting thing on this page and read as a filter,
 * because nothing next to it visibly changed except which rows were hidden. This is the readout
 * that makes it an instrument: age, position, household, the person closest to them, the
 * inclination that ran strongest, and how much they were still carrying.
 *
 * <b>It was a definition list before, and the header above the page now carries the same five
 * facts as chips.</b> Saying them a third time in a column of labels was not more rigorous, only
 * longer — the sentence is the form that shows the year moving, because a reader watching one
 * clause change reads it, and a reader watching a table cell change does not.
 */
export function StandingReadout({
  world,
  figure,
  year,
  ctx,
  vantages,
  place,
  onSelectYear,
}: {
  world: World;
  figure: Figure;
  year: number;
  ctx: LifeContext;
  vantages: LifeVantage[];
  /** Where the position was held, when the page could work one out. */
  place?: string;
  onSelectYear: (year: number) => void;
}) {
  const standing = useMemo(() => standingAt(figure, year, ctx), [figure, year, ctx]);
  const sentence = useMemo(
    () => standingSentence(figure, standing, ctx, place),
    [figure, standing, ctx, place],
  );

  return (
    <div>
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <span className="he-data text-lg text-[var(--ink)]">
          Age {standing.age} · Year {standing.year}
        </span>
        {!standing.alive && (
          <span className="text-xs text-[var(--ink-faint)]">
            after their death in {figure.deathYear}
          </span>
        )}
      </div>

      <p className="mt-2 text-[0.95rem] leading-relaxed text-[var(--ink-soft)]">
        {sentence.map((part, index) =>
          part.type === 'text' ? (
            <span key={index}>{part.text}</span>
          ) : (
            <EntityLink key={index} world={world} id={part.id} />
          ),
        )}
      </p>

      {vantages.length > 1 && (
        <div className="mt-4 flex flex-wrap gap-2 border-t border-[var(--rule)] pt-3">
          {vantages.map((vantage) => (
            <button
              key={vantage.key}
              type="button"
              title={vantage.hint}
              onClick={() => onSelectYear(vantage.year)}
              className={`rounded border px-2 py-1 text-xs transition-colors ${
                vantage.year === year
                  ? 'border-[var(--primary)] text-[var(--primary)]'
                  : 'border-[var(--rule)] text-[var(--ink-soft)] hover:border-[var(--primary)] hover:text-[var(--primary)]'
              }`}
            >
              {vantage.label}
              <span className="ml-1.5 text-[var(--ink-faint)]">
                {vantage.year - figure.birthYear}
              </span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

const TIE_STROKE = {
  kin: { stroke: 'var(--primary)', dash: undefined as string | undefined, label: 'kin' },
  friend: { stroke: 'var(--tertiary)', dash: '4 3', label: 'friend' },
  rival: { stroke: 'var(--error)', dash: '1 3', label: 'rival' },
};

/**
 * The people around one person in one year, drawn rather than listed.
 *
 * <b>Deliberately not a genealogy.</b> Eight lines at most, weighted by how much the bond carries
 * and styled by what kind of bond it is. What makes it worth drawing is dragging the year: a
 * household that had six lines at forty has two at seventy, and the social model becomes something
 * you watch instead of something you reconstruct from the ledger.
 */
export function RelationshipConstellation({
  world,
  figure,
  year,
  ctx,
}: {
  world: World;
  figure: Figure;
  year: number;
  ctx: LifeContext;
}) {
  const nodes = useMemo(() => buildConstellation(figure, year, ctx), [figure, year, ctx]);

  if (nodes.length === 0) {
    return (
      <p className="text-sm text-[var(--ink-faint)]">
        Nobody living stood in a recorded relationship with them in {year}.
      </p>
    );
  }

  const size = 260;
  const centre = size / 2;
  const radius = size / 2 - 26;
  const placed = nodes.map((node, index) => {
    // Start at the top and go round: the strongest bond is first, so the eye lands on it.
    const angle = (index / nodes.length) * Math.PI * 2 - Math.PI / 2;
    return {
      ...node,
      x: centre + Math.cos(angle) * radius * (0.62 + 0.38 * node.strength),
      y: centre + Math.sin(angle) * radius * (0.62 + 0.38 * node.strength),
    };
  });

  return (
    <div className="flex flex-wrap items-start gap-5">
      <svg
        viewBox={`0 0 ${size} ${size}`}
        role="img"
        aria-label={`${nodes.length} relationships around ${figure.name} in ${year}`}
        className="h-[260px] w-[260px] shrink-0"
      >
        {placed.map((node) => (
          <line
            key={`line:${node.id}`}
            x1={centre}
            y1={centre}
            x2={node.x}
            y2={node.y}
            stroke={TIE_STROKE[node.tie].stroke}
            strokeDasharray={TIE_STROKE[node.tie].dash}
            strokeWidth={0.8 + node.strength * 3}
            strokeOpacity={0.35 + node.strength * 0.5}
          />
        ))}
        {placed.map((node) => (
          <circle
            key={`dot:${node.id}`}
            cx={node.x}
            cy={node.y}
            r={3 + node.strength * 2.5}
            fill={TIE_STROKE[node.tie].stroke}
          />
        ))}
        <circle cx={centre} cy={centre} r={6} fill="var(--ink)" />
      </svg>

      <div className="min-w-[12rem] flex-1">
        <ul className="space-y-1.5 text-sm">
          {placed.map((node) => (
            <li key={node.id} className="flex items-baseline gap-2">
              <span
                aria-hidden
                className="mt-1 inline-block h-2 w-2 shrink-0 rounded-full"
                style={{ background: TIE_STROKE[node.tie].stroke }}
              />
              <span className="min-w-0">
                <EntityLink world={world} id={node.id} />
                <span className="ml-2 text-xs text-[var(--ink-faint)]">
                  {node.roles.slice(0, 2).join(', ').toLowerCase()} · {node.reading}
                </span>
              </span>
            </li>
          ))}
        </ul>
        <p className="mt-3 text-xs leading-relaxed text-[var(--ink-faint)]">
          Line weight is how much the bond carries; solid is kin, dashed a companion, dotted a
          rivalry. Only people living in {year} are drawn — move the year and watch the lines go
          out.
        </p>
      </div>
    </div>
  );
}

/**
 * What the record can say about a life without writing a sentence about it.
 *
 * Counts, not prose. "Outlived four close relatives" is a fact the simulation produced and nobody
 * composed, which is exactly why it lands harder than any generated paragraph would.
 */
export function KnownForPanel({
  world,
  figure,
  events,
  throughYear,
  ctx,
  artifactCount,
}: {
  world: World;
  figure: Figure;
  events: HistoryEvent[];
  throughYear: number;
  ctx: LifeContext;
  artifactCount: number;
}) {
  const lines = useMemo(
    () => knownFor(figure, events, throughYear, ctx),
    [figure, events, throughYear, ctx],
  );
  const significance = useMemo(
    () => historicalSignificance(figure, events, throughYear, ctx, artifactCount),
    [figure, events, throughYear, ctx, artifactCount],
  );

  return (
    <div>
      {lines.length === 0 ? (
        <p className="text-sm text-[var(--ink-faint)]">
          Nothing beyond a birth is recorded of them by {throughYear}.
        </p>
      ) : (
        <ul className="space-y-1 text-sm">
          {lines.map((line) => (
            <li key={line.key}>
              {line.before}
              {line.aboutId && <EntityLink world={world} id={line.aboutId} />}
              {line.after}
            </li>
          ))}
        </ul>
      )}

      <div className="mt-4 border-t border-[var(--rule)] pt-3">
        <div className="flex items-baseline justify-between gap-3">
          <span className="he-label">Historical significance</span>
          <span className="text-xs text-[var(--ink-soft)]">{significance.band}</span>
        </div>
        <div
          className="relative mt-2 h-1 rounded bg-[var(--rule)]"
          role="meter"
          aria-valuemin={0}
          aria-valuemax={1}
          aria-valuenow={Number(significance.score.toFixed(2))}
          aria-label="Historical significance"
        >
          <span
            className="absolute top-1/2 h-2.5 w-2.5 -translate-x-1/2 -translate-y-1/2 rounded-full bg-[var(--primary)]"
            style={{ left: `${significance.score * 100}%` }}
          />
        </div>
        <div className="mt-1 flex justify-between text-[0.7rem] text-[var(--ink-faint)]">
          <span>Ordinary</span>
          <span>Influential</span>
        </div>
        <p className="mt-2 text-xs leading-relaxed text-[var(--ink-faint)]">
          How much of the recorded history runs through this person, not how good a person they
          were. Most lives sit near the left, which is what makes the right worth finding.
          {significance.reasons.length > 0 && <> Counted here: {significance.reasons.join(', ')}.</>}
        </p>
      </div>
    </div>
  );
}

/**
 * What continued because they existed.
 *
 * A page about one person stops when they do, and the part that mattered is on other people's
 * pages. Generated history is only interesting where it has consequences, so this walks two
 * generations down and says what the chronicle did with them.
 */
export function RipplesPanel({
  world,
  figure,
  ctx,
}: {
  world: World;
  figure: Figure;
  ctx: LifeContext;
}) {
  const ripples = useMemo(() => ripplesAfter(figure, ctx), [figure, ctx]);

  if (ripples.length === 0) {
    return (
      <p className="text-sm text-[var(--ink-faint)]">
        The record follows nothing past their death.
      </p>
    );
  }

  return (
    <ul className="space-y-1.5 text-sm">
      {ripples.map((ripple) => (
        <li key={ripple.key}>
          {ripple.before}
          {ripple.aboutId && <EntityLink world={world} id={ripple.aboutId} />}
          {ripple.after}
        </li>
      ))}
    </ul>
  );
}
