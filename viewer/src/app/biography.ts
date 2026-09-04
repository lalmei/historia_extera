import { OCCUPATION_LABELS } from './types.ts';
import type {
  Affinity,
  Campaign,
  Disposition,
  Dispute,
  EntityId,
  Figure,
  FigureBond,
  HistoryEvent,
  Journey,
  Plot,
  SalientMemory,
  Undertaking,
} from './types.ts';

export interface JourneyGroup {
  key: string;
  firstYear: number;
  lastYear: number;
  journeys: Journey[];
}

/** Groups only routine returns; a waylay or disappearance always keeps its own line. */
export function groupJourneys(journeys: Journey[], throughYear: number): JourneyGroup[] {
  const ordered = journeys
    .filter((journey) => journey.year <= throughYear)
    .sort(
      (a, b) =>
        a.year - b.year ||
        journeyKey(a).localeCompare(journeyKey(b)),
    );
  const byKey = new Map<string, JourneyGroup>();
  const result: JourneyGroup[] = [];

  for (const journey of ordered) {
    const routine = !journey.outcome || journey.outcome === 'Returned';
    const key = routine ? journeyKey(journey) : `${journeyKey(journey)}:${journey.year}:${result.length}`;
    let group = routine ? byKey.get(key) : undefined;
    if (!group) {
      group = { key, firstYear: journey.year, lastYear: journey.year, journeys: [] };
      result.push(group);
      if (routine) byKey.set(key, group);
    }

    group.journeys.push(journey);
    group.lastYear = journey.year;
  }

  return result.sort((a, b) => a.firstYear - b.firstYear || a.key.localeCompare(b.key));
}

export function visibleBondAt(bond: FigureBond, year: number): boolean {
  // A later mutation may have added a role or changed its reading. The export does not carry a
  // historical snapshot, so hiding that bond is the only honest alternative to leaking the future.
  return bond.sinceYear <= year && bond.lastChangedYear <= year;
}

export function visibleMemoryAt(memory: SalientMemory, year: number): boolean {
  return memory.year <= year && memory.lastReinforcedYear <= year;
}

export function undertakingAt(
  undertaking: Undertaking,
  year: number,
): Undertaking | undefined {
  if (undertaking.startYear > year) return undefined;

  const steps = undertaking.steps.filter((step) => step.year <= year);
  if (undertaking.endYear !== undefined && undertaking.endYear <= year) {
    return { ...undertaking, steps };
  }

  return {
    ...undertaking,
    state: 'Active',
    endYear: undefined,
    outcome: undefined,
    progress: Math.min(undertaking.requiredProgress, steps.length),
    steps,
  };
}

export function disputeAt(dispute: Dispute, year: number): Dispute | undefined {
  if (dispute.startYear > year) return undefined;

  const acts = dispute.acts.filter((act) => act.year <= year);
  if (dispute.endYear !== undefined && dispute.endYear <= year) return { ...dispute, acts };

  return {
    ...dispute,
    stage: acts.at(-1)?.stage ?? 'Grudge',
    outcome: 'Open',
    resolution: undefined,
    arbiterId: undefined,
    endYear: undefined,
    lastActionYear: acts.at(-1)?.year ?? dispute.startYear,
    acts,
  };
}

/**
 * A friendship as it stood in a given year.
 *
 * The same withholding `disputeAt` does, with one addition: `placeId` is dropped along with the
 * rest of an ending that has not happened yet. It is one mutable slot holding two different facts
 * — the town the two met in, until a betrayal overwrites it with the town the turn happened in —
 * so a record read before its ending must not carry it.
 */
export function affinityAt(affinity: Affinity, year: number): Affinity | undefined {
  if (affinity.startYear > year) return undefined;

  const acts = affinity.acts.filter((act) => act.year <= year);
  if (affinity.endYear !== undefined && affinity.endYear <= year) return { ...affinity, acts };

  return {
    ...affinity,
    stage: acts.at(-1)?.stage ?? 'Acquaintance',
    outcome: 'Open',
    resolution: undefined,
    betrayerId: undefined,
    placeId: undefined,
    endYear: undefined,
    lastActionYear: acts.at(-1)?.year ?? affinity.startYear,
    acts,
  };
}

export function plotVisibleFrom(plot: Plot, figureId: EntityId): number | undefined {
  if (plot.viewpoint === 'Target') return plot.publicYear;

  // Figure pages are retrospective, but the year slider is a contemporary cut through that
  // record. Until the conspiracy became public, even a participant's page must not turn private
  // knowledge into a public biographical fact. The unfiltered final-year page deliberately bypasses
  // this helper so it can still carry the complete retrospective truth promised by #133.
  const participantFrom =
    plot.viewpoint === 'Leader'
      ? plot.startYear
      : plot.members.find((member) => member.figureId === figureId && member.witting)?.joinedYear;
  const retrospectiveFrom = plot.publicYear;
  return participantFrom === undefined || retrospectiveFrom === undefined
    ? undefined
    : Math.max(participantFrom, retrospectiveFrom);
}

export function plotAt(plot: Plot, figureId: EntityId, year: number): Plot | undefined {
  const visibleFrom = plotVisibleFrom(plot, figureId);
  if (visibleFrom === undefined || visibleFrom > year) return undefined;

  const members = plot.members.filter((member) => member.joinedYear <= year);
  const acts = plot.acts.filter((act) => act.year <= year);
  if (plot.endYear !== undefined && plot.endYear <= year) return { ...plot, members, acts };

  return {
    ...plot,
    phase: acts.at(-1)?.phase ?? 'Gathering',
    outcome: 'Ongoing',
    resolution: undefined,
    betrayerId: undefined,
    endYear: undefined,
    publicYear: plot.publicYear !== undefined && plot.publicYear <= year ? plot.publicYear : undefined,
    progress: acts.filter((act) => act.detail.includes('moved a step')).length,
    members,
    acts,
  };
}

export type BiographyEpisodeKind = 'Undertaking' | 'Conflict' | 'Plot' | 'Campaign';

export interface BiographyEpisode {
  key: string;
  kind: BiographyEpisodeKind;
  startYear: number;
  endYear: number;
  score: number;
  primaryId?: EntityId;
  relatedIds: EntityId[];
  sourceEventIds: number[];
}

/** Selects a few completed, causal episodes; the full episode ledger remains separate. */
export function buildBiographyEpisodes(
  figure: Figure,
  events: HistoryEvent[],
  throughYear: number,
): BiographyEpisode[] {
  const candidates: BiographyEpisode[] = [];

  for (const undertaking of figure.undertakings ?? []) {
    if (undertaking.endYear === undefined || undertaking.endYear > throughYear) continue;
    const related = compactIds([
      undertaking.targetId,
      undertaking.destinationId,
      undertaking.sponsorId,
    ]);
    candidates.push({
      key: `undertaking:${undertaking.id}:${undertaking.startYear}`,
      kind: 'Undertaking',
      startYear: undertaking.startYear,
      endYear: undertaking.endYear,
      score: 70 + undertaking.steps.length + (undertaking.state === 'Succeeded' ? 8 : 0),
      primaryId: undertaking.destinationId ?? undertaking.targetId,
      relatedIds: related,
      sourceEventIds: evidence(events, figure.id, undertaking.startYear, undertaking.endYear, related),
    });
  }

  for (const dispute of figure.disputes ?? []) {
    if (dispute.endYear === undefined || dispute.endYear > throughYear) continue;
    const severity = dispute.outcome === 'Killed' ? 16 : dispute.outcome === 'Wounded' ? 10 : 3;
    candidates.push({
      key: `conflict:${dispute.id}:${dispute.otherId}:${dispute.startYear}`,
      kind: 'Conflict',
      startYear: dispute.startYear,
      endYear: dispute.endYear,
      score: 58 + severity + dispute.acts.length,
      primaryId: dispute.otherId,
      relatedIds: compactIds([dispute.otherId, dispute.sourceEntityId, dispute.arbiterId]),
      sourceEventIds: evidence(
        events,
        figure.id,
        dispute.startYear,
        dispute.endYear,
        compactIds([dispute.otherId, dispute.sourceEntityId]),
      ),
    });
  }

  for (const raw of figure.plots ?? []) {
    const plot = plotAt(raw, figure.id, throughYear);
    if (!plot || plot.endYear === undefined || plot.outcome === 'Abandoned') continue;
    const severity = plot.outcome === 'Succeeded' ? 18 : plot.outcome === 'Failed' ? 10 : 6;
    candidates.push({
      key: `plot:${plot.leaderId}:${plot.id}`,
      kind: 'Plot',
      startYear: plot.startYear,
      endYear: plot.endYear,
      score: 64 + severity + plot.members.length,
      primaryId: plot.viewpoint === 'Target' ? plot.leaderId : plot.targetId,
      relatedIds: compactIds([plot.leaderId, plot.targetId, plot.realmId, plot.betrayerId]),
      sourceEventIds: evidence(
        events,
        figure.id,
        plot.publicYear ?? plot.endYear,
        plot.endYear,
        compactIds([plot.leaderId, plot.targetId]),
      ),
    });
  }

  const seenCampaigns = new Set<string>();
  for (const campaign of figure.campaigns ?? []) {
    if (campaign.year > throughYear || !notableCampaign(campaign)) continue;
    const subject = campaign.battleId ?? campaign.warId;
    const key = `campaign:${subject}`;
    if (seenCampaigns.has(key)) continue;
    seenCampaigns.add(key);

    const severity =
      campaign.fate === 'Killed'
        ? 20
        : campaign.fate === 'Wounded'
          ? 12
          : campaign.role === 'Commanded'
            ? 8
            : 4;
    candidates.push({
      key,
      kind: 'Campaign',
      startYear: campaign.year,
      endYear: campaign.year,
      score: 52 + severity + campaign.renownGained,
      primaryId: subject,
      relatedIds: compactIds([campaign.warId, campaign.battleId, campaign.sideId]),
      sourceEventIds: evidence(events, figure.id, campaign.year, campaign.year, [subject]),
    });
  }

  const ranked = candidates.sort(
    (a, b) =>
      b.score - a.score ||
      b.endYear - a.endYear ||
      a.key.localeCompare(b.key),
  );
  const selected: BiographyEpisode[] = [];
  const kinds = new Set<BiographyEpisodeKind>();

  for (const candidate of ranked) {
    if (kinds.has(candidate.kind)) continue;
    selected.push(candidate);
    kinds.add(candidate.kind);
    if (selected.length === 3) return selected;
  }

  for (const candidate of ranked) {
    if (selected.includes(candidate)) continue;
    selected.push(candidate);
    if (selected.length === 3) break;
  }

  return selected;
}

function journeyKey(journey: Journey): string {
  return [
    journey.kind,
    journey.fromSettlementId,
    journey.toSettlementId,
    journey.viaId ?? '',
    journey.returnSettlementId ?? '',
  ].join(':');
}

function notableCampaign(campaign: Campaign): boolean {
  return (
    campaign.role === 'Commanded' ||
    campaign.fate === 'Wounded' ||
    campaign.fate === 'Killed' ||
    campaign.renownGained > 0 ||
    campaign.traumatized ||
    campaign.deserted
  );
}

function compactIds(ids: Array<EntityId | undefined>): EntityId[] {
  const result: EntityId[] = [];
  for (const id of ids) {
    if (id && !result.includes(id)) result.push(id);
  }
  return result;
}

function evidence(
  events: HistoryEvent[],
  figureId: EntityId,
  fromYear: number,
  toYear: number,
  related: EntityId[],
): number[] {
  return events
    .filter(
      (event) =>
        event.year >= fromYear &&
        event.year <= toYear &&
        references(event, figureId) &&
        (related.length === 0 || related.some((id) => references(event, id))),
    )
    .map((event) => event.id);
}

function references(event: HistoryEvent, id: EntityId): boolean {
  return (
    event.subject === id ||
    event.object === id ||
    event.location === id ||
    (event.extra?.includes(id) ?? false)
  );
}

// ---------------------------------------------------------------------------
// The life as a whole: arc, standing, constellation, reputation, aftermath.
//
// Everything below derives from what the engine already recorded — titles, ranks, bonds,
// memories, campaigns, journeys, affinities and the figure's own slice of the chronicle. None
// of it invents prose. A count the record can defend ("42 years under arms") carries further
// than a sentence a language model could have written about anyone.
// ---------------------------------------------------------------------------

/** What these derivations need from the world, without importing the store into a pure module. */
export interface LifeContext {
  /** The last year the record covers. */
  endYear: number;
  figureOf: (id: EntityId) => Figure | undefined;
  eventsFor: (id: EntityId) => HistoryEvent[];
  nameOf: (id: EntityId) => string;
  /**
   * Which realm held one settlement in a year. Optional: an older export answers it, but a
   * caller that only has figures and events (a test, a compatibility pass) does not, and the
   * derivations that use it fall back to the figure's own realm rather than refusing to run.
   */
  realmAt?: (settlementId: EntityId, year: number) => EntityId | undefined;
}

export type LifeMomentKind =
  | 'Birth'
  | 'Trade'
  | 'Rank'
  | 'Office'
  | 'Marriage'
  | 'Children'
  | 'Loss'
  | 'Friendship'
  | 'Wound'
  | 'Campaign'
  | 'Death';

export interface LifeMoment {
  key: string;
  year: number;
  age: number;
  kind: LifeMomentKind;
  /** The word that stands in the arc: "Soldiery", "Marriage", "6 children". */
  label: string;
  /** The same moment at length, for the title attribute. */
  detail: string;
  aboutId?: EntityId;
  /** Ranking weight; only used to decide what survives the cap. */
  score: number;
}

export interface LifeArc {
  firstYear: number;
  lastYear: number;
  moments: LifeMoment[];
  /**
   * Years in which anything was recorded, and how heavy each was against the heaviest — a
   * notable event counting for three routine ones.
   */
  density: { year: number; weight: number }[];
  busiestYear?: number;
}

const OFFICE_STANDING: Record<string, number> = {
  Ruler: 6,
  Regent: 5,
  HighPriest: 4,
  Marshal: 4,
  Governor: 3,
  Consort: 2,
};

const RANK_STANDING: Record<string, number> = {
  None: 0,
  Recruit: 1,
  Soldier: 2,
  FileLeader: 3,
  Captain: 4,
  Commander: 5,
};

/**
 * One life reduced to the handful of turns that made it that life.
 *
 * <b>Deliberately retrospective, unlike every panel below it on the page.</b> It is the page's
 * scrubber as well as its summary, and a scrubber that hides where it can scrub to is not one.
 * The year cut still governs the panels; this says so in its caption rather than pretending the
 * death it is drawing has not happened yet.
 *
 * Repetition is collapsed rather than listed: eleven children are one moment, six promotions are
 * the rung they ended on. A life told as forty dots is a timeline, and the reader already has one.
 */
export function buildLifeArc(
  figure: Figure,
  events: HistoryEvent[],
  ctx: LifeContext,
  limit = 9,
): LifeArc {
  const lastYear = figure.deathYear ?? ctx.endYear;
  const age = (year: number) => year - figure.birthYear;
  const moments: LifeMoment[] = [];
  const add = (moment: Omit<LifeMoment, 'age'>) => {
    if (moment.year < figure.birthYear || moment.year > lastYear) return;
    moments.push({ ...moment, age: age(moment.year) });
  };

  add({
    key: 'birth',
    year: figure.birthYear,
    kind: 'Birth',
    label: 'Born',
    detail: `Born in ${figure.birthYear}`,
    aboutId: figure.birthSettlementId,
    score: 1000,
  });

  const tookUpTrade = events.find((event) => event.kind === 'OccupationTaken');
  if (tookUpTrade) {
    const trade = tradeAt(figure, events, tookUpTrade.year);
    if (trade) {
      add({
        key: 'trade',
        year: tookUpTrade.year,
        kind: 'Trade',
        label: trade,
        detail: `Took to ${(tookUpTrade.data?.occupation ?? trade).toLowerCase()} in ${tookUpTrade.year}`,
        score: 60,
      });
    }
  }

  // The first office is the entry into public life and the highest is the height of it; the
  // dozen renewals of a term between them are the same fact recorded repeatedly.
  const titles = [...figure.titles].sort((a, b) => a.fromYear - b.fromYear);
  const firstTitle = titles[0];
  const highestTitle = highestOffice(figure);
  if (firstTitle) {
    add({
      key: `office:${firstTitle.fromYear}`,
      year: firstTitle.fromYear,
      kind: 'Office',
      label: firstTitle.title,
      detail: `Made ${firstTitle.title} in ${firstTitle.fromYear}`,
      aboutId: firstTitle.scopeId ?? firstTitle.civilizationId,
      score: 75 + (OFFICE_STANDING[firstTitle.kind] ?? 0),
    });
  }
  if (highestTitle && highestTitle.fromYear !== firstTitle?.fromYear) {
    add({
      key: `office:${highestTitle.fromYear}`,
      year: highestTitle.fromYear,
      kind: 'Office',
      label: highestTitle.title,
      detail: `Raised to ${highestTitle.title} in ${highestTitle.fromYear}`,
      aboutId: highestTitle.scopeId ?? highestTitle.civilizationId,
      score: 85 + (OFFICE_STANDING[highestTitle.kind] ?? 0),
    });
  }

  const topRank = [...figure.service].sort(
    (a, b) => (RANK_STANDING[a.rank] ?? 0) - (RANK_STANDING[b.rank] ?? 0) || a.year - b.year,
  ).at(-1);
  if (topRank && (RANK_STANDING[topRank.rank] ?? 0) >= 3) {
    add({
      key: `rank:${topRank.year}`,
      year: topRank.year,
      kind: 'Rank',
      label: topRank.title,
      detail: `Raised to ${topRank.title} in ${topRank.year}${topRank.claim ? `, ${topRank.claim}` : ''}`,
      aboutId: topRank.civilizationId,
      score: 68 + (RANK_STANDING[topRank.rank] ?? 0),
    });
  }

  const marriages = figure.bonds
    .filter((bond) => bond.kinds.includes('Spouse'))
    .sort((a, b) => a.sinceYear - b.sinceYear);
  for (const [index, marriage] of marriages.slice(0, 2).entries()) {
    add({
      key: `marriage:${marriage.otherId}`,
      year: marriage.sinceYear,
      kind: 'Marriage',
      label: index === 0 ? 'Marriage' : 'Remarriage',
      detail: `Married ${ctx.nameOf(marriage.otherId)} in ${marriage.sinceYear}`,
      aboutId: marriage.otherId,
      score: 80 - index * 4,
    });
  }

  const children = figure.childIds
    .map((id) => ctx.figureOf(id))
    .filter((child): child is Figure => child !== undefined)
    .sort((a, b) => a.birthYear - b.birthYear);
  if (children.length > 0) {
    add({
      key: 'children',
      year: children[0].birthYear,
      kind: 'Children',
      // Dated to the first child, and labelled as the first of them: a chip reading "7 children"
      // that moves the year to a house with one in it is a small lie about the same fact.
      label: children.length === 1 ? 'A child' : `First of ${children.length}`,
      detail:
        children.length === 1
          ? `${children[0].name} born in ${children[0].birthYear}`
          : `${children.length} children, from ${children[0].birthYear} to ${children.at(-1)!.birthYear}`,
      aboutId: children[0].id,
      score: 78,
    });
  }

  const worstLoss = figure.memories
    .filter((memory) => memory.kind === 'Bereavement')
    .sort((a, b) => b.intensity - a.intensity || a.year - b.year)[0];
  if (worstLoss) {
    add({
      key: `loss:${worstLoss.year}:${worstLoss.aboutId ?? ''}`,
      year: worstLoss.year,
      kind: 'Loss',
      label: 'Loss',
      detail: worstLoss.aboutId
        ? `Lost ${ctx.nameOf(worstLoss.aboutId)} in ${worstLoss.year}`
        : `Bereaved in ${worstLoss.year}`,
      aboutId: worstLoss.aboutId,
      score: 72,
    });
  }

  const longestFriendship = (figure.affinities ?? [])
    .map((affinity) => ({
      affinity,
      years: (affinity.endYear ?? lastYear) - affinity.startYear,
    }))
    .sort((a, b) => b.years - a.years || a.affinity.startYear - b.affinity.startYear)[0];
  if (longestFriendship) {
    const { affinity, years } = longestFriendship;
    add({
      key: `friendship:${affinity.id}`,
      year: affinity.startYear,
      kind: 'Friendship',
      label: 'Friendship',
      detail: `${ctx.nameOf(affinity.otherId)}, from ${affinity.startYear}${years > 0 ? ` — ${counted(years, 'year')}` : ''}`,
      aboutId: affinity.otherId,
      score: 66,
    });
  }

  const worstWound = [...figure.injuries].sort(
    (a, b) => Number(b.permanent) - Number(a.permanent) || b.year - a.year,
  )[0];
  if (worstWound) {
    add({
      key: `wound:${worstWound.year}`,
      year: worstWound.year,
      kind: 'Wound',
      label: worstWound.permanent ? 'Maimed' : 'Wounded',
      detail: `${worstWound.detail} in ${worstWound.year}`,
      aboutId: worstWound.causeId,
      score: 64 + (worstWound.permanent ? 6 : 0),
    });
  }

  const notableCampaign = [...figure.campaigns]
    .filter((campaign) => campaign.role === 'Commanded' || campaign.renownGained > 0)
    .sort((a, b) => b.renownGained - a.renownGained || a.year - b.year)[0];
  if (notableCampaign) {
    const subject = notableCampaign.battleId ?? notableCampaign.warId;
    add({
      key: `campaign:${subject}`,
      year: notableCampaign.year,
      kind: 'Campaign',
      label: notableCampaign.role === 'Commanded' ? 'Command' : 'In the field',
      detail: `${ctx.nameOf(subject)}, ${notableCampaign.year}`,
      aboutId: subject,
      score: 62 + notableCampaign.renownGained,
    });
  }

  if (figure.deathYear !== undefined) {
    add({
      key: 'death',
      year: figure.deathYear,
      kind: 'Death',
      label: 'Died',
      detail: `Died in ${figure.deathYear}, aged ${figure.deathYear - figure.birthYear}`,
      score: 1000,
    });
  }

  // Weighted by what the chronicle itself calls significance, not by raw count: three births and
  // a move is a quiet year, and a year with a siege in it should not have to compete with one.
  const counts = new Map<number, number>();
  for (const event of events) {
    if (event.year < figure.birthYear || event.year > lastYear) continue;
    const weight = event.significance === 'Notable' ? 3 : 1;
    counts.set(event.year, (counts.get(event.year) ?? 0) + weight);
  }
  const busiest = [...counts.entries()].sort((a, b) => b[1] - a[1] || a[0] - b[0])[0];
  const density = [...counts.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([year, count]) => ({ year, weight: busiest ? count / busiest[1] : 0 }));

  // Keep the strongest moments, then put them back in the order they happened: the arc is read
  // left to right as a life, not as a ranking.
  const kept = moments
    .sort((a, b) => b.score - a.score || a.year - b.year)
    .slice(0, limit)
    .sort((a, b) => a.year - b.year || b.score - a.score);

  return {
    firstYear: figure.birthYear,
    lastYear,
    moments: kept,
    density,
    busiestYear: busiest?.[0],
  };
}

export interface LifeVantage {
  key: 'Youth' | 'Peak' | 'Death' | 'Latest';
  label: string;
  year: number;
  hint: string;
}

/**
 * Three or four years worth comparing, so the slider reads as an instrument rather than a filter.
 *
 * Youth is the year they entered adult life, height is the year of their highest standing — the
 * office if they held one, otherwise the year the record has most to say about them — and death
 * is death. A reader who drags between them is doing the comparison the temporal model exists for.
 */
export function lifeVantages(figure: Figure, arc: LifeArc, ctx: LifeContext): LifeVantage[] {
  const vantages: LifeVantage[] = [];
  const entryYear = arc.moments.find(
    (moment) => moment.kind === 'Trade' || moment.kind === 'Rank' || moment.kind === 'Office',
  )?.year;
  // A child who died at two has no youth to stand at, and offering one would date it to their
  // death — which is how the same three buttons end up saying the same year three times.
  const youth = entryYear ?? (figure.birthYear + 16 <= arc.lastYear ? figure.birthYear + 16 : undefined);
  if (youth !== undefined && youth > figure.birthYear) {
    vantages.push({
      key: 'Youth',
      label: 'At youth',
      year: youth,
      hint: entryYear ? 'The year adult life began' : 'Sixteen years old',
    });
  }

  const peak = highestOffice(figure)?.fromYear ?? arc.busiestYear;
  if (
    peak !== undefined &&
    peak > figure.birthYear &&
    !vantages.some((vantage) => vantage.year === peak)
  ) {
    vantages.push({
      key: 'Peak',
      label: 'At height',
      year: peak,
      hint: highestOffice(figure)
        ? `The year they first became ${highestOffice(figure)!.title}`
        : 'The year the record has most to say about them',
    });
  }

  if (figure.deathYear !== undefined) {
    vantages.push({
      key: 'Death',
      label: 'At death',
      year: figure.deathYear,
      hint: `Aged ${figure.deathYear - figure.birthYear}`,
    });
  } else {
    vantages.push({
      key: 'Latest',
      label: 'Last year',
      year: ctx.endYear,
      hint: 'Still living where the record ends',
    });
  }

  // Death outranks the two vantages before it where they land on the same year: standing "at
  // youth" in the year somebody died is not a comparison.
  return vantages.filter(
    (vantage, index, all) => all.findLastIndex((other) => other.year === vantage.year) === index,
  );
}

export interface LifeStanding {
  year: number;
  age: number;
  alive: boolean;
  /** Office, else rank, else trade. */
  position?: string;
  /** "married", "widowed", "unmarried". */
  household: string;
  childCount: number;
  closest?: { id: EntityId; reading: string };
  dominantDisposition?: string;
  activeMemories: number;
}

/**
 * Who this person was in one year, in five lines.
 *
 * The panels underneath already carry all of it, spread across four sections and a ledger. The
 * point of saying it again in one block is that a reader dragging the year needs something small
 * enough to watch change.
 */
export function standingAt(figure: Figure, requestedYear: number, ctx: LifeContext): LifeStanding {
  const dead = figure.deathYear !== undefined && figure.deathYear <= requestedYear;
  // Every lookup below reads the year they were last alive in. A page left on the record's final
  // year would otherwise report a woman six centuries dead as holding no office and knowing
  // nobody, which is true and useless: what a reader wants from a dead person is who they were
  // when they stopped.
  const year = dead ? figure.deathYear! : requestedYear;
  const rank = figure.service.filter((step) => step.year <= year).at(-1);
  const trade = tradeAt(figure, ctx.eventsFor(figure.id), year);
  const office = activeOffice(figure, year);

  const spouses = figure.bonds
    .filter((bond) => bond.kinds.includes('Spouse') && bond.sinceYear <= year)
    .map((bond) => ctx.figureOf(bond.otherId));
  const living = spouses.filter(
    (spouse) => spouse && (spouse.deathYear === undefined || spouse.deathYear > year),
  );
  const household =
    spouses.length === 0 ? 'unmarried' : living.length > 0 ? 'married' : 'widowed';

  const childCount = figure.childIds
    .map((id) => ctx.figureOf(id))
    .filter((child) => child !== undefined && child.birthYear <= year).length;

  const closest = figure.bonds
    .filter((bond) => visibleBondAt(bond, year))
    .sort((a, b) => relationshipImportance(b) - relationshipImportance(a))[0];

  return {
    year: requestedYear,
    age: year - figure.birthYear,
    alive: !dead,
    position: office?.title ?? rank?.title ?? trade,
    household,
    childCount,
    closest: closest ? { id: closest.otherId, reading: relationshipReading(closest) } : undefined,
    dominantDisposition: dominantDisposition(figure),
    activeMemories: figure.memories.filter((memory) => visibleMemoryAt(memory, year)).length,
  };
}

export type ConstellationTie = 'kin' | 'friend' | 'rival';

export interface ConstellationNode {
  id: EntityId;
  tie: ConstellationTie;
  /** 0–1, for line weight. */
  strength: number;
  roles: string[];
  reading: string;
}

/**
 * The five to eight people who mattered in one year, as a shape rather than a list.
 *
 * <b>The point is what disappears.</b> A list of relationships read at two different years looks
 * much the same; the same relationships drawn as lines from one person show a household emptying
 * as the people in it die, which is the fact the social model is actually producing.
 */
export function buildConstellation(
  figure: Figure,
  year: number,
  ctx: LifeContext,
  limit = 8,
): ConstellationNode[] {
  const strongest = figure.bonds
    .filter((bond) => visibleBondAt(bond, year))
    .filter((bond) => {
      const other = ctx.figureOf(bond.otherId);
      // Somebody not yet born is not a relationship, and somebody already dead is not one either:
      // their line going out is the thing this drawing is for.
      if (!other) return false;
      return other.birthYear <= year && (other.deathYear === undefined || other.deathYear > year);
    })
    .sort((a, b) => relationshipImportance(b) - relationshipImportance(a))
    .slice(0, limit);

  const peak = strongest.reduce(
    (most, bond) => Math.max(most, relationshipImportance(bond)),
    0.0001,
  );

  return strongest.map((bond) => ({
    id: bond.otherId,
    tie: tieOf(bond),
    strength: Math.min(1, relationshipImportance(bond) / peak),
    roles: bond.kinds,
    reading: relationshipReading(bond),
  }));
}

/**
 * How many rulers took a throne over the figure while they were alive under it.
 *
 * <b>Where they lived, not where they ended.</b> `figure.civilizationId` is the realm they held
 * at death; counting a whole life against it charges a woman who married across a border, or
 * whose town changed hands under her, with accessions in a chronicle she was never living in.
 * The residences say where she was in each year and the timeline says who held that ground, so
 * the count walks the two together and only counts a crowning in a realm she was under that year.
 *
 * Where the record cannot answer that — no residences, or a caller without a timeline — it falls
 * back to the old reading against the ending realm, which is right for the majority who never
 * moved and no worse than what it replaces for those who did.
 */
function accessionsLivedUnder(figure: Figure, throughYear: number, ctx: LifeContext): number {
  const lastYear = Math.min(throughYear, figure.deathYear ?? throughYear);
  const realmAt = ctx.realmAt;
  const residences = figure.residences ?? [];

  const crownings = (realmId: EntityId, holds: (year: number) => boolean) =>
    ctx
      .eventsFor(realmId)
      .filter(
        (event) =>
          event.kind === 'RulerCrowned' &&
          event.year > figure.birthYear &&
          event.year <= lastYear &&
          holds(event.year),
      ).length;

  if (!realmAt || residences.length === 0) {
    return crownings(figure.civilizationId, () => true);
  }

  // A lifetime is at most a couple of hundred years, and both lookups are a binary search, so
  // the honest thing is also cheap: ask where they were and who held it, year by year.
  const yearsUnder = new Map<EntityId, Set<number>>();
  for (let year = figure.birthYear + 1; year <= lastYear; year++) {
    const residence = residences.filter((lived) => lived.fromYear <= year).at(-1);
    if (!residence) continue;

    const realmId = realmAt(residence.settlementId, year);
    if (!realmId) continue;

    const years = yearsUnder.get(realmId);
    if (years) years.add(year);
    else yearsUnder.set(realmId, new Set([year]));
  }

  if (yearsUnder.size === 0) return crownings(figure.civilizationId, () => true);

  let accessions = 0;
  for (const [realmId, years] of yearsUnder) {
    accessions += crownings(realmId, (year) => years.has(year));
  }

  return accessions;
}

export interface KnownForLine {
  key: string;
  /** Text before an entity link, the entity, then the rest. Most lines are text alone. */
  before: string;
  aboutId?: EntityId;
  after: string;
}

/**
 * What the record can say about a life without writing a sentence about it.
 *
 * Every line is a count taken from the simulation, which is why they are worth reading: "outlived
 * four close relatives" is a fact the engine produced and nobody wrote. Counted through the
 * selected year like everything else on the page, so a life reads shorter earlier in it.
 */
export function knownFor(
  figure: Figure,
  events: HistoryEvent[],
  throughYear: number,
  ctx: LifeContext,
  limit = 5,
): KnownForLine[] {
  const lines: { line: KnownForLine; score: number }[] = [];
  const push = (score: number, key: string, before: string, aboutId?: EntityId, after = '') =>
    lines.push({ score, line: { key, before, aboutId, after } });

  const service = [
    ...figure.service.map((step) => step.year),
    ...figure.campaigns.map((campaign) => campaign.year),
  ].filter((year) => year <= throughYear);
  if (service.length > 0) {
    const years = Math.max(...service) - Math.min(...service);
    push(
      90,
      'service',
      years >= 2
        ? `${counted(years, 'year')} under arms`
        : `Took to arms in ${Math.min(...service)}`,
    );
  }

  const offices = figure.titles.filter((title) => title.fromYear <= throughYear);
  if (offices.length > 0) {
    const distinct = new Set(offices.map((title) => title.title));
    push(
      88,
      'offices',
      distinct.size === 1
        ? `${offices[0].title} of `
        : `Held ${counted(distinct.size, 'office')}, latterly in `,
      offices.at(-1)!.civilizationId,
    );
  }

  const accessions = accessionsLivedUnder(figure, throughYear, ctx);
  if (accessions >= 2) {
    push(70, 'rulers', `Lived under ${counted(accessions, 'ruler')}`);
  }

  const children = figure.childIds
    .map((id) => ctx.figureOf(id))
    .filter((child): child is Figure => child !== undefined && child.birthYear <= throughYear);
  if (children.length > 0) {
    push(84, 'children', `Raised ${counted(children.length, 'child', 'children')}`);
  }

  const closeKin = [
    figure.motherId,
    figure.fatherId,
    ...figure.childIds,
    ...figure.spouseIds,
  ]
    .map((id) => (id ? ctx.figureOf(id) : undefined))
    .filter(
      (kin): kin is Figure =>
        kin !== undefined &&
        kin.deathYear !== undefined &&
        kin.deathYear <= Math.min(throughYear, figure.deathYear ?? throughYear),
    );
  if (closeKin.length > 0) {
    push(80, 'outlived', `Outlived ${counted(closeKin.length, 'close relative')}`);
  }

  const friendship = (figure.affinities ?? [])
    .filter((affinity) => affinity.startYear <= throughYear)
    .map((affinity) => ({
      affinity,
      years: Math.min(affinity.endYear ?? throughYear, throughYear) - affinity.startYear,
    }))
    .sort((a, b) => b.years - a.years)[0];
  if (friendship && friendship.years >= 1) {
    push(
      76,
      'friendship',
      'Friendship with ',
      friendship.affinity.otherId,
      ` lasted ${counted(friendship.years, 'year')}`,
    );
  }

  const battles = figure.campaigns.filter(
    (campaign) => campaign.year <= throughYear && campaign.battleId !== undefined,
  );
  if (battles.length > 0) {
    push(72, 'battles', `Stood in ${counted(battles.length, 'engagement')}`);
  }

  const wounds = figure.injuries.filter((injury) => injury.year <= throughYear);
  if (wounds.length > 0) {
    push(
      68,
      'wounds',
      `Wounded ${counted(wounds.length, 'time')}${wounds.some((wound) => wound.permanent) ? ', one of them for good' : ''}`,
    );
  }

  const journeys = figure.journeys.filter((journey) => journey.year <= throughYear);
  if (journeys.length >= 3) {
    const places = new Set(journeys.map((journey) => journey.toSettlementId));
    push(60, 'journeys', `Travelled to ${counted(places.size, 'town')}`);
  }

  const claims = figure.claims.filter((claim) => claim.year <= throughYear);
  if (claims.length > 0) {
    const borneOut = claims.filter(
      (claim) => claim.verdict === 'Confirmed' && (claim.settledYear ?? throughYear) <= throughYear,
    ).length;
    push(
      64,
      'sky',
      borneOut > 0
        ? `Read the sky, and was borne out ${counted(borneOut, 'time')}`
        : `Set down ${counted(claims.length, 'reading')} of the sky`,
    );
  }

  const mentions = events.filter(
    (event) => event.year <= throughYear && event.subject !== figure.id,
  ).length;
  if (mentions >= 12) {
    push(56, 'mentions', `Named in ${counted(mentions, "other person's record", "other people's records")}`);
  }

  return lines
    .sort((a, b) => b.score - a.score)
    .slice(0, limit)
    .map((entry) => entry.line);
}

export interface Significance {
  /** 0–1. Most people sit near the bottom, which is what makes the top worth finding. */
  score: number;
  band: 'Ordinary' | 'Recorded' | 'Notable' | 'Consequential' | 'Influential';
  /** What put them there, largest contribution first. */
  reasons: string[];
}

const SIGNIFICANCE_BANDS: [floor: number, band: Significance['band']][] = [
  [0.82, 'Influential'],
  [0.6, 'Consequential'],
  [0.38, 'Notable'],
  [0.18, 'Recorded'],
  [0, 'Ordinary'],
];

/**
 * How far one life reaches into the rest of the record.
 *
 * Not a rating of the person: a measure of how much of the history runs through them. Offices,
 * command, artefacts, conspiracies and being written into other people's records all count, and
 * the total is squashed through a saturating curve so that the ordinary case — a townsman with a
 * trade, a marriage and four children — stays ordinary. Finding somebody at the top of this scale
 * should mean something, which it cannot if half the population reads as influential.
 */
export function historicalSignificance(
  figure: Figure,
  events: HistoryEvent[],
  throughYear: number,
  ctx: LifeContext,
  artifactCount = 0,
): Significance {
  const contributions: { weight: number; reason: string }[] = [];
  const contribute = (weight: number, reason: string) => {
    if (weight > 0) contributions.push({ weight, reason });
  };

  const offices = figure.titles.filter((title) => title.fromYear <= throughYear);
  const officeWeight = offices.reduce(
    (sum, title) => sum + (OFFICE_STANDING[title.kind] ?? 1) * 1.6,
    0,
  );
  contribute(officeWeight, offices.length === 1 ? `${offices[0].title}` : `${offices.length} offices held`);

  const commands = figure.campaigns.filter(
    (campaign) => campaign.year <= throughYear && campaign.role === 'Commanded',
  );
  contribute(commands.length * 2.4, `${counted(commands.length, 'command')} in the field`);

  const battles = figure.campaigns.filter(
    (campaign) => campaign.year <= throughYear && campaign.battleId !== undefined,
  );
  contribute(battles.length * 0.9, `${counted(battles.length, 'engagement')}`);

  const topRank = figure.service
    .filter((step) => step.year <= throughYear)
    .reduce((best, step) => Math.max(best, RANK_STANDING[step.rank] ?? 0), 0);
  contribute(topRank * 0.8, topRank >= 4 ? 'raised high in the army' : 'served in the army');

  contribute(artifactCount * 2.2, `${counted(artifactCount, 'treasure')} held`);

  const plots = figure.plots.filter((plot) => plot.startYear <= throughYear);
  contribute(plots.length * 2.6, `${counted(plots.length, 'conspiracy', 'conspiracies')}`);

  const undertakings = figure.undertakings.filter(
    (undertaking) => undertaking.startYear <= throughYear,
  );
  contribute(undertakings.length * 1.2, `${counted(undertakings.length, 'undertaking')}`);

  const rulersKnown = figure.bonds.filter(
    (bond) =>
      visibleBondAt(bond, throughYear) &&
      (ctx.figureOf(bond.otherId)?.titles ?? []).some((title) => title.kind === 'Ruler'),
  );
  contribute(rulersKnown.length * 1.5, `known to ${counted(rulersKnown.length, 'ruler')}`);

  const descendants = figure.childIds
    .map((id) => ctx.figureOf(id))
    .filter((child): child is Figure => child !== undefined && child.birthYear <= throughYear);
  contribute(descendants.length * 0.4, `${counted(descendants.length, 'child', 'children')}`);

  const journeys = figure.journeys.filter((journey) => journey.year <= throughYear);
  contribute(journeys.length * 0.25, `${counted(journeys.length, 'journey')}`);

  const claims = figure.claims.filter((claim) => claim.year <= throughYear);
  contribute(claims.length * 1.4, `${counted(claims.length, 'reading')} of the sky`);

  // Being written into somebody else's year is the one measure here that no single system
  // produces: it is the history noticing them.
  const mentions = events.filter(
    (event) => event.year <= throughYear && event.subject !== figure.id,
  ).length;
  contribute(mentions * 0.14, `named in ${counted(mentions, 'other record')}`);

  const total = contributions.reduce((sum, entry) => sum + entry.weight, 0);
  // Saturating rather than linear: the difference between a king and a general should be small,
  // and the difference between a farmer and a general large.
  // Divisor tuned against every saved world rather than by taste: at 30, between 45% and 82% of
  // a world's people read Ordinary and at most 1.3% read Influential, across seventeen runs from
  // schema 21 to 50. A scale that calls a quarter of the population influential says nothing.
  const score = 1 - Math.exp(-total / 30);
  const band = SIGNIFICANCE_BANDS.find(([floor]) => score >= floor)![1];

  return {
    score,
    band,
    reasons: contributions
      .sort((a, b) => b.weight - a.weight)
      .slice(0, 4)
      .map((entry) => entry.reason),
  };
}

export interface Ripple {
  key: string;
  before: string;
  aboutId?: EntityId;
  after: string;
}

/**
 * What went on because they had been there.
 *
 * The reason generated history is worth reading is that it has consequences, and consequences are
 * exactly what a page about one person hides: their record stops at their death, and the part
 * that mattered is on other people's pages. This walks two generations down and reports what the
 * chronicle did with them.
 */
export function ripplesAfter(figure: Figure, ctx: LifeContext, limit = 5): Ripple[] {
  if (figure.deathYear === undefined) return [];
  const death = figure.deathYear;
  const ripples: { ripple: Ripple; score: number }[] = [];
  const push = (score: number, key: string, before: string, aboutId?: EntityId, after = '') =>
    ripples.push({ score, ripple: { key, before, aboutId, after } });

  const children = figure.childIds
    .map((id) => ctx.figureOf(id))
    .filter((child): child is Figure => child !== undefined);
  const survivors = children.filter(
    (child) => child.deathYear === undefined || child.deathYear > death,
  );

  for (const child of survivors
    .filter((child) => child.childIds.length > 0)
    .sort((a, b) => b.childIds.length - a.childIds.length)
    .slice(0, 2)) {
    push(
      90 + child.childIds.length,
      `line:${child.id}`,
      '',
      child.id,
      ` continued the line — ${counted(child.childIds.length, 'child', 'children')}`,
    );
  }

  const outlivedBy = survivors.filter((child) => child.childIds.length === 0);
  if (outlivedBy.length > 0 && survivors.length > 0) {
    push(
      70,
      'survived',
      `Outlived by ${counted(survivors.length, 'child', 'children')}`,
      undefined,
      survivors.some((child) => child.deathYear === undefined)
        ? ', of whom the record loses none'
        : '',
    );
  }

  // One friendship, not every friendship: two people outliving them by a year each is a list,
  // and the longest survival is the one that says something.
  const carried = (figure.affinities ?? [])
    .filter((affinity) => affinity.endYear === undefined || affinity.endYear >= death)
    .map((affinity) => ({ affinity, other: ctx.figureOf(affinity.otherId) }))
    .filter((entry) => entry.other !== undefined && (entry.other.deathYear ?? ctx.endYear) > death)
    .sort(
      (a, b) => (b.other!.deathYear ?? ctx.endYear) - (a.other!.deathYear ?? ctx.endYear),
    )[0];
  if (carried) {
    const carriedTo = carried.other!.deathYear;
    push(
      80,
      `friendship:${carried.affinity.id}`,
      '',
      carried.affinity.otherId,
      carriedTo === undefined
        ? ' carried their friendship past the end of the record'
        : ` carried their friendship ${counted(carriedTo - death, 'year')} longer, to their own death`,
    );
  }

  // Two generations is far enough to show a line continuing and near enough that the count is
  // still about this person rather than about the realm.
  const descendants = new Set<EntityId>();
  for (const child of children) {
    descendants.add(child.id);
    for (const grandchild of child.childIds) descendants.add(grandchild);
  }
  const laterEvents = [...descendants]
    .flatMap((id) => ctx.eventsFor(id))
    .filter((event) => event.year > death && event.significance === 'Notable');
  const laterIds = new Set(laterEvents.map((event) => event.id));
  if (laterIds.size > 0) {
    push(
      86,
      'descendants',
      `Descendants stood in ${counted(laterIds.size, 'later recorded event')}`,
    );
  }

  // Naming the successor rather than saying the seat "passed on": the whole point of a ripple is
  // that it lands on somebody, and the realm's own chronicle knows exactly who.
  const seat = figure.titles.find((title) => title.kind === 'Ruler');
  if (seat) {
    const successor = ctx
      .eventsFor(seat.civilizationId)
      .filter((event) => event.kind === 'RulerCrowned' && event.year >= death)
      .sort((a, b) => a.year - b.year)[0];
    if (successor?.subject && successor.subject !== figure.id) {
      push(
        84,
        'successor',
        '',
        successor.subject,
        ` took the seat in ${ctx.nameOf(seat.civilizationId)}, in ${successor.year}`,
      );
    }
  }

  return ripples
    .sort((a, b) => b.score - a.score)
    .slice(0, limit)
    .map((entry) => entry.ripple);
}

/**
 * How much weight to give a relationship, for ordering and for line weight.
 *
 * Shared by the constellation and by every panel that has to pick three people out of forty:
 * a page that ranks the same bonds two different ways is a page that contradicts itself.
 */
export function relationshipImportance(bond: FigureBond): number {
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

/**
 * Which of the three kinds of line to draw.
 *
 * Kin outranks a grievance on purpose: a son a man is afraid of is still his son, and drawing
 * that as a rivalry loses the fact the drawing exists to show. What the bond feels like is in
 * its reading beside the name; what it *is* is the line.
 */
function tieOf(bond: FigureBond): ConstellationTie {
  if (bond.kinds.some((kind) => ['Rival', 'Enemy', 'Betrayer'].includes(kind))) return 'rival';
  if (
    bond.kinds.some((kind) =>
      ['Kin', 'Spouse', 'Parent', 'Child', 'Sibling', 'Guardian', 'Ward'].includes(kind),
    )
  ) {
    return 'kin';
  }
  if (bond.grievance >= 0.5 || bond.fear >= 0.5) return 'rival';
  return 'friend';
}

/** How a bond reads in a few words: the strongest feeling in it, or the last thing that moved it. */
export function relationshipReading(bond: FigureBond): string {
  if (bond.grievance >= 0.65) return 'a bitter grievance';
  if (bond.fear >= 0.55) return 'feared';
  if (bond.trust <= -0.45) return 'deeply distrusted';
  if (bond.obligation >= 0.55) return 'bound by duty';
  if (bond.trust >= 0.55) return 'deeply trusted';
  if (bond.affection >= 0.45) return 'held dear';
  if (bond.affection <= -0.35) return 'disliked';
  return bond.lastCause.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
}

const DISPOSITION_LABELS: [key: keyof Disposition, label: string][] = [
  ['aggression', 'Aggression'],
  ['expansionism', 'Expansionism'],
  ['piety', 'Piety'],
  ['tradition', 'Tradition'],
  ['mercantile', 'Mercantile'],
  ['learning', 'Learning'],
  ['centralism', 'Centralism'],
];

function dominantDisposition(figure: Figure): string | undefined {
  const disposition = figure.disposition;
  if (!disposition) return undefined;
  const strongest = DISPOSITION_LABELS.map(([key, label]) => ({
    label,
    value: disposition[key] ?? 0,
  })).sort((a, b) => b.value - a.value)[0];
  return strongest && strongest.value >= 0.5 ? strongest.label : undefined;
}

/**
 * The trade they were following in a year.
 *
 * The chronicle writes an occupation as prose ("took to a craft") while the figure carries the
 * engine's own name for it ("Guild"). Where the year is past their last change of trade the two
 * describe the same thing, and the rest of the page says "Guild" — so this prefers the label and
 * falls back to the sentence only where an earlier trade is being read.
 */
function tradeAt(figure: Figure, events: HistoryEvent[], year: number): string | undefined {
  const taken = events.filter((event) => event.kind === 'OccupationTaken');
  const current = taken.filter((event) => event.year <= year).at(-1);
  if (!current) return undefined;
  if (current === taken.at(-1) && figure.occupation && figure.occupation !== 'None') {
    return OCCUPATION_LABELS[figure.occupation];
  }
  return current.data?.occupation ? sentenceCase(current.data.occupation) : undefined;
}

/**
 * The highest office they ever held, dated to the first time they held it.
 *
 * A consul elected six times reached the height of his life at the first election, not the last;
 * ordering by year alone would put the height of every repeated office at its final renewal.
 */
function highestOffice(figure: Figure) {
  return [...figure.titles].sort(
    (a, b) =>
      (OFFICE_STANDING[a.kind] ?? 0) - (OFFICE_STANDING[b.kind] ?? 0) || b.fromYear - a.fromYear,
  ).at(-1);
}

/** The office held in a year, highest first — a governor who is also consort is a governor. */
function activeOffice(figure: Figure, year: number) {
  return figure.titles
    .filter((title) => title.fromYear <= year && (title.toYear === undefined || title.toYear >= year))
    .sort((a, b) => (OFFICE_STANDING[a.kind] ?? 0) - (OFFICE_STANDING[b.kind] ?? 0))
    .at(-1);
}

/** The engine writes some values as prose ("a craft"); a label wants them capitalised. */
function sentenceCase(text: string): string {
  return text.length === 0 ? text : text[0].toUpperCase() + text.slice(1);
}

/** "3 children", "1 child" — the plural is irregular often enough to be worth passing in. */
function counted(count: number, singular: string, plural = `${singular}s`): string {
  return `${count.toLocaleString()} ${count === 1 ? singular : plural}`;
}
