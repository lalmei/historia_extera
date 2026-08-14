import type {
  Battle,
  Civilization,
  EntityId,
  Settlement,
  SettlementTier,
  War,
  WorldExport,
} from './types';

/**
 * The world as it stood in any given year, rebuilt from the chronicle.
 *
 * The export ships *final* state — a region's last owner, a settlement's last tier — because
 * carrying a snapshot per year would multiply a file that already runs to megabytes by the
 * number of years simulated. Everything before the end is instead recovered by replaying the
 * events, which the engine guarantees is possible: `TerritoryTests` asserts that the log alone
 * reproduces the final map, so any border move a system forgets to record fails there rather
 * than silently misdrawing here.
 *
 * <b>Spans, not snapshots.</b> Each entity gets a list of "from this year, this was true"
 * entries and answers are found by binary search. A snapshot per year is simpler to write and
 * costs ~1,000 regions × the length of the run in memory; spans cost one entry per actual
 * change — a few hundred for a whole world — and make scrubbing a slider cheap enough to
 * animate. It also answers the question the region page wants ("held by whom, since when")
 * with no extra machinery.
 */
export interface Timeline {
  startYear: number;
  endYear: number;

  /** Who held each region that had an owner in `year`. */
  ownersAt: (year: number) => Map<EntityId, EntityId>;

  /** Who held one region in `year`, and since when. */
  holdingAt: (regionId: EntityId, year: number) => Holding | undefined;

  /** Every time one region changed hands, oldest first. */
  historyOf: (regionId: EntityId) => Holding[];

  /** Settlements standing in `year`, at the tier and under the realm they had then. */
  settlementsAt: (year: number) => Standing[];

  /** What one settlement followed in `year`, if anything. */
  faithAt: (settlementId: EntityId, year: number) => EntityId | undefined;

  /** How many settlements followed one faith in `year`. */
  followingAt: (religionId: EntityId, year: number) => number;

  /** Realms founded and not yet ended in `year`. */
  realmsAt: (year: number) => Civilization[];

  /** How many regions one realm held in `year`. */
  extentAt: (civId: EntityId, year: number) => number;

  /** A realm's extent across the whole run, one entry per year from `startYear`. */
  extentOf: (civId: EntityId) => number[];

  /** Who sat the throne in `year`, if anyone did. */
  rulerAt: (civId: EntityId, year: number) => EntityId | undefined;

  /** The seat of government in `year`. */
  capitalAt: (civId: EntityId, year: number) => EntityId | undefined;

  /** Battles fought in `year`. */
  battlesIn: (year: number) => Battle[];

  /** Wars being fought in `year`. */
  warsIn: (year: number) => War[];
}

export interface Holding {
  owner: EntityId;
  since: number;
  /** The year it changed hands again, or undefined if it never did. */
  until?: number;
}

export interface Standing {
  settlement: Settlement;
  tier: SettlementTier;
  /** Whoever held the ground that year — not necessarily who holds it at the end. */
  civilizationId: EntityId;
  isCapital: boolean;
  /** What the place followed that year, if anything. */
  religionId?: EntityId;
}

/** One "from this year onwards" entry. `value` of null means nobody. */
interface Span<T> {
  from: number;
  value: T | null;
}

export function buildTimeline(data: WorldExport): Timeline {
  const { meta, events } = data;

  const ownership = new Map<EntityId, Span<EntityId>[]>();
  const tiers = new Map<EntityId, Span<SettlementTier>[]>();
  const seats = new Map<EntityId, Span<EntityId>[]>();
  const reigns = new Map<EntityId, Span<EntityId>[]>();
  const faiths = new Map<EntityId, Span<EntityId>[]>();

  const append = <T>(spans: Map<EntityId, Span<T>[]>, key: EntityId, span: Span<T>) => {
    const existing = spans.get(key);
    if (existing) existing.push(span);
    else spans.set(key, [span]);
  };

  for (const event of events) {
    switch (event.kind) {
      case 'RegionClaimed':
      case 'RegionCeded':
        if (event.subject && event.object) {
          append(ownership, event.subject, { from: event.year, value: event.object });
        }
        break;

      case 'RegionReleased':
        if (event.subject) append(ownership, event.subject, { from: event.year, value: null });
        break;

      case 'SettlementPromoted':
      case 'SettlementDeclined': {
        const tier = event.subject && tierFrom(event.data?.tier);
        if (event.subject && tier) append(tiers, event.subject, { from: event.year, value: tier });
        break;
      }

      // A realm's first seat is named by its founding; later ones by their own event.
      case 'CivilizationFounded':
      case 'CapitalMoved':
        if (event.subject && event.location) {
          append(seats, event.subject, { from: event.year, value: event.location });
        }
        break;

      case 'RulerCrowned':
        if (event.object && event.subject) {
          append(reigns, event.object, { from: event.year, value: event.subject });
        }
        break;

      // Faith moves settlement by settlement, so it replays exactly like ownership does — and
      // for the same reason: the export carries only what each place believed at the end.
      case 'ReligionAdopted':
        if (event.subject && event.object) {
          append(faiths, event.subject, { from: event.year, value: event.object });
        }
        break;

      default:
        break;
    }
  }

  // Settlements start at the tier they were founded with, which no event announces — only
  // changes are recorded. Without this seed a settlement reads as a city from its founding
  // if it ever became one.
  for (const settlement of data.settlements) {
    append(tiers, settlement.id, { from: settlement.foundedYear, value: 'Hamlet' });
    tiers.get(settlement.id)!.sort((a, b) => a.from - b.from);
  }

  /**
   * Index of the rightmost span that had begun by `year`, or -1.
   *
   * Ties go to the later entry: several changes can land in one year — a province taken and
   * lost again in the same campaign season — and the last one written is the one that stood
   * at the year's end.
   */
  const indexAt = <T>(spans: Span<T>[] | undefined, year: number): number => {
    if (!spans || spans.length === 0 || year < spans[0].from) return -1;

    let low = 0;
    let high = spans.length - 1;
    while (low < high) {
      const middle = Math.ceil((low + high) / 2);
      if (spans[middle].from <= year) low = middle;
      else high = middle - 1;
    }

    return low;
  };

  const byYear = <T>(spans: Span<T>[] | undefined, year: number): Span<T> | undefined => {
    const index = indexAt(spans, year);
    return index < 0 ? undefined : spans![index];
  };

  const holdingAt = (regionId: EntityId, year: number): Holding | undefined => {
    const spans = ownership.get(regionId);
    const index = indexAt(spans, year);
    if (index < 0) return undefined;

    const span = spans![index];
    if (!span.value) return undefined;

    return { owner: span.value, since: span.from, until: spans![index + 1]?.from };
  };

  const ownersAt = (year: number): Map<EntityId, EntityId> => {
    const owners = new Map<EntityId, EntityId>();

    for (const [regionId, spans] of ownership) {
      const span = byYear(spans, year);
      if (span?.value) owners.set(regionId, span.value);
    }

    return owners;
  };

  const settlementsAt = (year: number): Standing[] => {
    const owners = ownersAt(year);
    const standing: Standing[] = [];

    for (const settlement of data.settlements) {
      if (settlement.foundedYear > year) continue;
      if (settlement.abandonedYear !== undefined && settlement.abandonedYear <= year) continue;

      // Whoever held the ground held the town: settlements change hands with their region, so
      // the region's owner is the honest answer for any year before the end. The settlement's
      // own field is the fallback for ground nobody claims — which is only ever a realm in the
      // year it is falling apart.
      const civilizationId = owners.get(settlement.regionId) ?? settlement.civilizationId;

      standing.push({
        settlement,
        tier: byYear(tiers.get(settlement.id), year)?.value ?? settlement.tier,
        civilizationId,
        isCapital: byYear(seats.get(civilizationId), year)?.value === settlement.id,
        religionId: byYear(faiths.get(settlement.id), year)?.value ?? undefined,
      });
    }

    return standing;
  };

  const realmsAt = (year: number): Civilization[] =>
    data.civilizations.filter(
      (civ) => civ.foundedYear <= year && (civ.endedYear === undefined || civ.endedYear >= year),
    );

  // Extent is asked for once per year to draw a curve, so it is accumulated in one pass over
  // the ownership spans rather than re-counting the map for every year of the run.
  const extents = new Map<EntityId, number[]>();
  const years = meta.endYear - meta.startYear + 1;

  for (const spans of ownership.values()) {
    for (let i = 0; i < spans.length; i++) {
      const span = spans[i];
      if (!span.value) continue;

      let series = extents.get(span.value);
      if (!series) {
        series = new Array<number>(years).fill(0);
        extents.set(span.value, series);
      }

      const from = Math.max(0, span.from - meta.startYear);
      const until = spans[i + 1] ? spans[i + 1].from - meta.startYear : years;
      for (let y = from; y < until; y++) series[y]++;
    }
  }

  const battles = new Map<number, Battle[]>();
  for (const battle of data.battles) {
    const fought = battles.get(battle.year);
    if (fought) fought.push(battle);
    else battles.set(battle.year, [battle]);
  }

  const figureDeaths = new Map<EntityId, number | undefined>(
    data.figures.map((figure) => [figure.id, figure.deathYear]),
  );

  const timeline: Timeline = {
    startYear: meta.startYear,
    endYear: meta.endYear,
    ownersAt,
    holdingAt,
    historyOf: (regionId) => {
      const spans = ownership.get(regionId) ?? [];
      const held: Holding[] = [];

      for (let i = 0; i < spans.length; i++) {
        if (spans[i].value) {
          held.push({ owner: spans[i].value!, since: spans[i].from, until: spans[i + 1]?.from });
        }
      }

      return held;
    },
    settlementsAt,
    faithAt: (settlementId, year) => byYear(faiths.get(settlementId), year)?.value ?? undefined,
    followingAt: (religionId, year) => {
      let following = 0;

      for (const settlement of data.settlements) {
        if (settlement.foundedYear > year) continue;
        if (settlement.abandonedYear !== undefined && settlement.abandonedYear <= year) continue;
        if (byYear(faiths.get(settlement.id), year)?.value === religionId) following++;
      }

      return following;
    },
    realmsAt,
    extentAt: (civId, year) => extents.get(civId)?.[year - meta.startYear] ?? 0,
    extentOf: (civId) => extents.get(civId) ?? new Array<number>(years).fill(0),
    rulerAt: (civId, year) => {
      const ruler = byYear(reigns.get(civId), year)?.value;
      if (!ruler) return undefined;

      // A reign ends with the person. Succession runs the same year, so the gap is usually
      // nothing — but showing a dead ruler on a scrubbed map is worse than showing none.
      const died = figureDeaths.get(ruler);
      if (died !== undefined && died < year) return undefined;

      const civ = data.civilizations.find((c) => c.id === civId);
      if (civ?.endedYear !== undefined && civ.endedYear < year) return undefined;

      return ruler;
    },
    capitalAt: (civId, year) => byYear(seats.get(civId), year)?.value ?? undefined,
    battlesIn: (year) => battles.get(year) ?? [],
    warsIn: (year) =>
      data.wars.filter((war) => war.startYear <= year && (war.endYear ?? Infinity) >= year),
  };

  warnIfReplayDisagrees(data, timeline);

  return timeline;
}

/**
 * Checks the replay against the state the export actually shipped.
 *
 * The engine's own tests are the real guard, but they only cover the seeds they run. A world
 * file produced by a newer engine than this viewer, or by a system that moves a border without
 * recording it, would otherwise draw a map that looks entirely plausible and is wrong.
 */
function warnIfReplayDisagrees(data: WorldExport, timeline: Timeline): void {
  const owners = timeline.ownersAt(data.meta.endYear);
  const wrong = data.regions.filter((region) => (owners.get(region.id) ?? undefined) !== region.owner);

  if (wrong.length > 0) {
    console.warn(
      `Replaying the chronicle disagrees with the exported map on ${wrong.length} of ` +
        `${data.regions.length} regions (first: ${wrong[0].id}). Territory shown for years ` +
        `before the end of the run may be wrong.`,
    );
  }
}

const TIERS: Record<string, SettlementTier> = {
  hamlet: 'Hamlet',
  village: 'Village',
  town: 'Town',
  city: 'City',
};

/** Promotion events carry the tier as the prose label the narration uses. */
function tierFrom(label: string | undefined): SettlementTier | undefined {
  return label ? TIERS[label.toLowerCase()] : undefined;
}
