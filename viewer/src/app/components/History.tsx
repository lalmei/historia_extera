import { useLanguage } from '../i18n/LanguageProvider';
import { translate, translateIn, translationsIn } from '../i18n/translations.ts';
import { useMemo, useState } from 'react';
import type { World } from '../store';
import type { EntityId, Series } from '../types';
import { Panel } from './common';

/** One entity's series, in the engine's order, grouped as the engine grouped them. */
export function groupedSeries(world: World, id: EntityId): Map<string, Series[]> {
  const groups = new Map<string, Series[]>();

  for (const series of world.seriesFor(id)) {
    const existing = groups.get(series.group);
    if (existing) existing.push(series);
    else groups.set(series.group, [series]);
  }

  return groups;
}

/**
 * A panel per group of series an entity carries.
 *
 * `skip` is for pages that place a group themselves — a realm shows its fortunes beside the
 * dials they ended on — and everything not skipped still renders, so a group the engine adds
 * later cannot go missing just because this page was written before it existed.
 */
export function HistoryPanels({
  world,
  id,
  skip = [],
}: {
  world: World;
  id: EntityId;
  skip?: string[];
}) {
  const { language } = useLanguage();
  const groups = groupedSeries(world, id);

  return (
    <>
      {[...groups.entries()]
        .filter(([group]) => !skip.includes(group))
        .map(([group, series]) => (
          <Panel key={group} title={panelTitle(group, series, language)}>
            <SeriesGrid world={world} series={series} />
          </Panel>
        ))}
    </>
  );
}

/**
 * A title for one group's panel.
 *
 * An ungrouped measure names itself — "Population, year by year" beats "Over time" on a page
 * where it is the only chart — and a group the viewer has no title for falls back to its own
 * key rather than going unlabelled.
 */
function panelTitle(group: string, series: Series[], language: string = 'en'): string {
  const suffix = translate(language, 'history.yearByYear');
  if (group === '' && series.length === 1) {
    return `${humaniseMetric(series[0].metric, language)}${suffix}`;
  }

  const titles = translationsIn(language, 'groupTitles');
  const known = Object.prototype.hasOwnProperty.call(titles, group) ? titles[group] : undefined;
  return known ?? `${humaniseMetric(group, language)}${suffix}`;
}

/** The charts themselves, one per measure, on a shared scale within each unit. */
export function SeriesGrid({ world, series }: { world: World; series: Series[] }) {
  if (series.length === 0) return null;

  // Counts get their own ceiling per measure; dials share [0, 1] across the whole group, which
  // is the only way "this realm was more weary than aggrieved" is legible at a glance.
  return (
    <div className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
      {series.map((one) => (
        <SeriesChart key={one.metric} world={world} series={one} />
      ))}
    </div>
  );
}

export function SeriesChart({ world, series }: { world: World; series: Series }) {
  const { language } = useLanguage();
  const fr = language === 'fr';
  const { startYear, endYear } = world.export.meta;
  const [hovered, setHovered] = useState<number | undefined>(undefined);

  const summary = useMemo(() => summarise(series), [series]);

  const width = 300;
  const height = 52;
  const top = 5;

  const span = Math.max(1, endYear - startYear);
  const ceiling = series.unit === 'Fraction' ? 1 : Math.max(1, summary.peak);

  const x = (index: number) => ((series.fromYear + index - startYear) / span) * width;
  const y = (value: number) => height - (value / ceiling) * (height - top);

  const line = series.values
    .map((value, index) => `${index === 0 ? 'M' : 'L'}${x(index)} ${y(value)}`)
    .join('');

  const label = humaniseMetric(series.metric, language);
  const reading = hovered !== undefined ? series.values[hovered] : undefined;

  return (
    <figure>
      <figcaption className="mb-1 flex items-baseline justify-between gap-3 text-xs">
        <span className="text-[var(--ink-soft)]">{label}</span>
        <span className="tabular-nums text-[var(--ink-faint)]">
          {reading !== undefined ? (
            <>
              {series.fromYear + hovered!} · {format(reading, series.unit, language)}
            </>
          ) : (
            <>
              {format(summary.last, series.unit, language)}{translateIn(language, 'inline', " at the end · peak ")}
              {format(summary.peak, series.unit, language)} {translateIn(language, 'inline', "in")} {series.fromYear + summary.peakIndex}
            </>
          )}
        </span>
      </figcaption>

      <svg
        viewBox={`0 0 ${width} ${height}`}
        className="h-14 w-full touch-none"
        preserveAspectRatio="none"
        role="img"
        aria-label={fr
          ? `${label}, de ${series.fromYear} à ${series.fromYear + series.values.length - 1} : valeur finale ${format(summary.last, series.unit, language)}, maximum de ${format(summary.peak, series.unit, language)} en ${series.fromYear + summary.peakIndex}`
          : `${label} from ${series.fromYear} to ${series.fromYear + series.values.length - 1}: ` +
            `ended at ${format(summary.last, series.unit, language)}, peaked at ` +
            `${format(summary.peak, series.unit, language)} in ${series.fromYear + summary.peakIndex}`
        }
        onPointerMove={(event) => {
          const box = event.currentTarget.getBoundingClientRect();
          const fraction = (event.clientX - box.left) / Math.max(1, box.width);
          const year = startYear + fraction * span;
          const index = Math.round(year - series.fromYear);
          setHovered(index >= 0 && index < series.values.length ? index : undefined);
        }}
        onPointerLeave={() => setHovered(undefined)}
      >
        <path
          d={`${line}L${x(series.values.length - 1)} ${height}L${x(0)} ${height}z`}
          fill="var(--accent)"
          fillOpacity={0.18}
        />
        <path
          d={line}
          fill="none"
          stroke="var(--accent)"
          strokeWidth={1.5}
          vectorEffect="non-scaling-stroke"
        />

        {hovered !== undefined && (
          <line
            x1={x(hovered)}
            x2={x(hovered)}
            y1={0}
            y2={height}
            stroke="var(--ink-faint)"
            strokeWidth={1}
            vectorEffect="non-scaling-stroke"
          />
        )}
      </svg>
    </figure>
  );
}

function summarise(series: Series): { last: number; peak: number; peakIndex: number } {
  let peak = series.values[0] ?? 0;
  let peakIndex = 0;

  for (let i = 1; i < series.values.length; i++) {
    if (series.values[i] > peak) {
      peak = series.values[i];
      peakIndex = i;
    }
  }

  return { last: series.values[series.values.length - 1] ?? 0, peak, peakIndex };
}

function format(value: number, unit: string, language: string = 'en'): string {
  if (language === 'fr') {
    return unit === 'Count'
      ? Math.round(value).toLocaleString('fr-FR')
      : value.toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }
  return unit === 'Count' ? Math.round(value).toLocaleString() : value.toFixed(2);
}

/** `population` → `Population`, and an unrecognised key still reads as words. */
function humaniseMetric(metric: string, language: string = 'en'): string {
  const labels = translationsIn(language, 'metricLabels');
  const known = Object.prototype.hasOwnProperty.call(labels, metric) ? labels[metric] : undefined;
  if (known) return known;

  const spaced = metric.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/[-_]/g, ' ');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}
