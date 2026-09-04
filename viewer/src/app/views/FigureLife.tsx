import { useMemo, useRef } from 'react';
import {
  buildConstellation,
  historicalSignificance,
  knownFor,
  ripplesAfter,
  standingAt,
  type LifeArc,
  type LifeContext,
  type LifeMomentKind,
  type LifeVantage,
} from '../biography';
import { EntityLink } from '../components/common';
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
  Trade: 'var(--secondary)',
  Rank: 'var(--tertiary)',
  Office: 'var(--primary)',
  Marriage: 'var(--secondary)',
  Children: 'var(--secondary)',
  Loss: 'var(--error)',
  Friendship: 'var(--primary)',
  Wound: 'var(--error)',
  Campaign: 'var(--tertiary)',
  Death: 'var(--ink-faint)',
};

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
  selectedYear,
  onSelectYear,
}: {
  arc: LifeArc;
  selectedYear: number;
  onSelectYear: (year: number) => void;
}) {
  const trackRef = useRef<HTMLDivElement>(null);
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
              title={`${year}`}
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
            {selectedYear}
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
              className={`rounded border px-2 py-0.5 text-xs transition-colors ${
                moment.year === selectedYear
                  ? 'border-[var(--primary)] text-[var(--primary)]'
                  : 'border-[var(--rule)] text-[var(--ink-soft)] hover:border-[var(--primary)] hover:text-[var(--primary)]'
              }`}
            >
              {moment.label}
              <span className="ml-1.5 text-[var(--ink-faint)]">{moment.age}</span>
            </button>
          </li>
        ))}
      </ol>

      <p className="mt-3 text-xs leading-relaxed text-[var(--ink-faint)]">
        The whole life, retrospectively — the panels below show only what was known in the year
        selected. Bar height is how much the chronicle recorded in that year, counting what it
        marked notable for three; the number beside each turn is the age it happened at. Click
        anywhere on the strip to move the year.
        {arc.busiestYear !== undefined && (
          <>
            {' '}
            Their busiest year was <strong className="text-[var(--ink-soft)]">{arc.busiestYear}</strong>.
          </>
        )}
      </p>
    </div>
  );
}

/**
 * Who they were in the selected year, in five lines that all change as it moves.
 *
 * The slider was conceptually the most interesting thing on this page and read as a filter,
 * because nothing next to it visibly changed except which rows were hidden. This is the readout
 * that makes it an instrument: age, position, household, the person closest to them, the
 * inclination that ran strongest, and how much they were still carrying.
 */
export function StandingReadout({
  world,
  figure,
  year,
  ctx,
  vantages,
  onSelectYear,
}: {
  world: World;
  figure: Figure;
  year: number;
  ctx: LifeContext;
  vantages: LifeVantage[];
  onSelectYear: (year: number) => void;
}) {
  const standing = useMemo(() => standingAt(figure, year, ctx), [figure, year, ctx]);

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

      <p className="mt-1.5 text-sm">
        {[
          standing.position ?? 'no recorded position',
          standing.household,
          standing.childCount === 1 ? '1 child' : `${standing.childCount} children`,
        ].join(' · ')}
      </p>

      <dl className="mt-3 space-y-1 text-sm">
        <div className="flex gap-2">
          <dt className="w-44 shrink-0 text-[var(--ink-faint)]">Closest relationship</dt>
          <dd className="min-w-0">
            {standing.closest ? (
              <>
                <EntityLink world={world} id={standing.closest.id} />
                <span className="ml-2 text-xs text-[var(--ink-faint)]">
                  {standing.closest.reading}
                </span>
              </>
            ) : (
              <span className="text-[var(--ink-faint)]">none yet visible</span>
            )}
          </dd>
        </div>
        <div className="flex gap-2">
          <dt className="w-44 shrink-0 text-[var(--ink-faint)]">Dominant disposition</dt>
          <dd className="min-w-0">
            {standing.dominantDisposition ?? (
              <span className="text-[var(--ink-faint)]">nothing runs strongly</span>
            )}
          </dd>
        </div>
        <div className="flex gap-2">
          <dt className="w-44 shrink-0 text-[var(--ink-faint)]">Still carried</dt>
          <dd className="min-w-0">
            {standing.activeMemories === 0
              ? 'nothing formative yet'
              : `${standing.activeMemories} formative ${standing.activeMemories === 1 ? 'memory' : 'memories'}`}
          </dd>
        </div>
      </dl>

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
