import type {
  Campaign,
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
