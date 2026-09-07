import { VARIANT_MIX } from './narrate.ts';
import type { LifeContext, StandingPart } from './biography.ts';
import type {
  Affinity,
  Disposition,
  EntityId,
  Figure,
  HistoryEvent,
  Sex,
} from './types.ts';

export type EvidenceKind =
  | 'LongOccupation'
  | 'LongResidence'
  | 'Friendship'
  | 'Betrayal'
  | 'Mentorship'
  | 'Pilgrimage'
  | 'ReligiousOffice'
  | 'TradeJourney'
  | 'TradeSuccess'
  | 'TradeFailure'
  | 'HeldOffice'
  | 'RejectedOffice'
  | 'Revolt'
  | 'Feud'
  | 'Battle'
  | 'Killing'
  | 'Migration'
  | 'Discovery'
  | 'Study';

export type BiographyDial =
  | 'Aggression'
  | 'Expansionism'
  | 'Piety'
  | 'Tradition'
  | 'Mercantile'
  | 'Learning'
  | 'Centralism'
  | 'Independence';

export type BiographyTheme =
  | 'Rootedness'
  | 'EnduringLoyalty'
  | 'ReligiousDevotion'
  | 'ScholarlyLife'
  | 'TransmissionOfKnowledge'
  | 'CommercialSuccess'
  | 'CommercialFailure'
  | 'TerritorialAmbition'
  | 'FrontierLife'
  | 'ViolentConflict'
  | 'ConsolidationOfPower'
  | 'ResistanceToAuthority'
  | 'Isolation'
  | 'InstitutionalService'
  | 'ReligiousScholarship'
  | 'Betrayal';

export type BiographyOutcome = 'Positive' | 'Negative' | 'Mixed' | 'Neutral';

export type BiographyThemeGroup =
  | 'Identity'
  | 'Relationships'
  | 'Power'
  | 'Knowledge'
  | 'Faith'
  | 'Conflict'
  | 'Commerce'
  | 'Mobility';

export interface BiographyEvidence {
  kind: EvidenceKind;
  strength: number;
  startYear: number;
  endYear: number;
  relatedFigureId?: EntityId;
  placeId?: EntityId;
  tag?: string;
}

export interface BiographyInterpretation {
  theme: BiographyTheme;
  dials: Partial<Record<BiographyDial, number>>;
  score: number;
  evidence: BiographyEvidence[];
  outcome: BiographyOutcome;
}

export const THEME_LABELS: Record<BiographyTheme, string> = {
  Rootedness: 'Rooted',
  EnduringLoyalty: 'Loyal',
  ReligiousDevotion: 'Devout',
  ScholarlyLife: 'Scholarly',
  TransmissionOfKnowledge: 'Teacher',
  CommercialSuccess: 'Prosperous trade',
  CommercialFailure: 'Failed trade',
  TerritorialAmbition: 'Ambitious',
  FrontierLife: 'Frontier life',
  ViolentConflict: 'Quarrelsome',
  ConsolidationOfPower: 'Centralising',
  ResistanceToAuthority: 'Defiant',
  Isolation: 'Apart',
  InstitutionalService: 'Office-bound',
  ReligiousScholarship: 'Religious scholar',
  Betrayal: 'Broken trust',
};

const LONG_SPAN_YEARS = 20;
const FRIENDSHIP_MIN_YEARS = 10;

function clamp01(value: number): number {
  return Math.max(0, Math.min(1, value));
}

export function standingYear(figure: Figure, requestedYear: number): number {
  return figure.deathYear !== undefined && figure.deathYear <= requestedYear
    ? figure.deathYear
    : requestedYear;
}

function dialValue(disposition: Disposition | undefined, dial: BiographyDial): number {
  if (!disposition) return 0.5;
  switch (dial) {
    case 'Aggression':
      return disposition.aggression;
    case 'Expansionism':
      return disposition.expansionism;
    case 'Piety':
      return disposition.piety;
    case 'Tradition':
      return disposition.tradition;
    case 'Mercantile':
      return disposition.mercantile;
    case 'Learning':
      return disposition.learning;
    case 'Centralism':
      return disposition.centralism;
    case 'Independence':
      return disposition.independence ?? 0.5;
    default:
      return 0.5;
  }
}

function themeGroup(theme: BiographyTheme): BiographyThemeGroup {
  switch (theme) {
    case 'Rootedness':
    case 'Isolation':
      return 'Identity';
    case 'EnduringLoyalty':
    case 'Betrayal':
      return 'Relationships';
    case 'ConsolidationOfPower':
    case 'ResistanceToAuthority':
    case 'InstitutionalService':
    case 'TerritorialAmbition':
      return 'Power';
    case 'ScholarlyLife':
    case 'TransmissionOfKnowledge':
    case 'ReligiousScholarship':
      return 'Knowledge';
    case 'ReligiousDevotion':
      return 'Faith';
    case 'ViolentConflict':
      return 'Conflict';
    case 'CommercialSuccess':
    case 'CommercialFailure':
      return 'Commerce';
    case 'FrontierLife':
      return 'Mobility';
    default:
      return 'Identity';
  }
}

function affinityVisible(affinity: Affinity, year: number): Affinity | undefined {
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

function affinityOther(affinity: Affinity, _figureId: EntityId): EntityId {
  return affinity.otherId;
}

export function extractEvidence(
  figure: Figure,
  year: number,
  events: HistoryEvent[] = [],
): BiographyEvidence[] {
  const evidence: BiographyEvidence[] = [];

  const residences = figure.residences ?? [];
  if (residences.length > 0) {
    let longest: (typeof residences)[number] | undefined;
    let longestSpan = 0;

    for (let i = 0; i < residences.length; i++) {
      const residence = residences[i];
      if (residence.fromYear > year) continue;

      const endYear =
        i + 1 < residences.length
          ? Math.min(residences[i + 1].fromYear, year)
          : year;
      const span = endYear - residence.fromYear;
      if (span > longestSpan) {
        longestSpan = span;
        longest = residence;
      }

      if (
        (residence.reason === 'Flight' || residence.reason === 'Settled') &&
        residence.fromYear <= year
      ) {
        evidence.push({
          kind: 'Migration',
          strength: clamp01(span / 40),
          startYear: residence.fromYear,
          endYear,
          placeId: residence.settlementId,
          tag: residence.reason,
        });
      }
    }

    if (longest && longestSpan >= LONG_SPAN_YEARS) {
      let end = year;
      for (let i = 0; i < residences.length; i++) {
        if (residences[i].settlementId !== longest.settlementId) continue;
        if (residences[i].fromYear > year) break;
        end =
          i + 1 < residences.length
            ? Math.min(residences[i + 1].fromYear, year)
            : year;
      }
      evidence.push({
        kind: 'LongResidence',
        strength: clamp01(longestSpan / 40),
        startYear: longest.fromYear,
        endYear: end,
        placeId: longest.settlementId,
      });
    }
  }

  if (figure.occupation && figure.occupation !== 'None') {
    const startYear = occupationStartYear(figure, year, events);
    let span = year - startYear;
    if (span >= LONG_SPAN_YEARS) {
      evidence.push({
        kind: 'LongOccupation',
        strength: clamp01(span / 40),
        startYear,
        endYear: year,
        tag: figure.occupation,
      });
    }

    const priestOffice = figure.titles.find(
      (title) =>
        title.kind === 'HighPriest' &&
        title.fromYear <= year &&
        (title.toYear === undefined || title.toYear >= year),
    );
    if (figure.occupation === 'Clergy' || priestOffice) {
      evidence.push({
        kind: 'ReligiousOffice',
        strength: 0.75,
        startYear: priestOffice?.fromYear ?? startYear,
        endYear: year,
        placeId: priestOffice?.scopeId,
      });
    }
  }

  for (const affinity of figure.affinities ?? []) {
    const at = affinityVisible(affinity, year);
    if (!at) continue;
    const endYear = at.endYear ?? year;
    const span = endYear - at.startYear;
    const stage = at.acts.at(-1)?.stage ?? at.stage;
    if (stage === 'Friendship' && span >= FRIENDSHIP_MIN_YEARS) {
      evidence.push({
        kind: 'Friendship',
        strength: clamp01(span / 40),
        startYear: at.startYear,
        endYear,
        relatedFigureId: affinityOther(at, figure.id),
        placeId: at.placeId,
      });
    }
    if (at.outcome === 'Betrayed' && at.endYear !== undefined && at.endYear <= year) {
      evidence.push({
        kind: 'Betrayal',
        strength: 0.8,
        startYear: at.endYear,
        endYear: at.endYear,
        relatedFigureId: affinityOther(at, figure.id),
        placeId: at.placeId,
        tag: at.betrayerId === figure.id ? 'Turned' : 'Suffered',
      });
    }
  }

  for (const betrayal of figure.betrayals ?? []) {
    if (betrayal.year > year) continue;
    const other =
      betrayal.betrayerId === figure.id ? betrayal.betrayedId : betrayal.betrayerId;
    evidence.push({
      kind: 'Betrayal',
      strength: 0.85,
      startYear: betrayal.year,
      endYear: betrayal.year,
      relatedFigureId: other,
      placeId: betrayal.placeId,
      tag: betrayal.betrayerId === figure.id ? 'Turned' : 'Suffered',
    });
  }

  for (const mentorship of figure.mentorships ?? []) {
    if (mentorship.startYear > year) continue;
    const other =
      mentorship.mentorId === figure.id ? mentorship.apprenticeId : mentorship.mentorId;
    evidence.push({
      kind: 'Mentorship',
      strength: clamp01((year - mentorship.startYear + 10) / 40),
      startYear: mentorship.startYear,
      endYear: year,
      relatedFigureId: other,
      placeId: mentorship.locationId,
      tag: mentorship.careerFamily,
    });
  }

  for (const journey of figure.journeys ?? []) {
    if (journey.year > year) continue;
    const end = journey.returnYear !== undefined && journey.returnYear <= year
      ? journey.returnYear
      : year;
    if (journey.kind === 'Pilgrimage') {
      evidence.push({
        kind: 'Pilgrimage',
        strength: 0.7,
        startYear: journey.year,
        endYear: end,
        placeId: journey.toSettlementId,
      });
    }
    if (journey.kind === 'Trade') {
      evidence.push({
        kind: 'TradeJourney',
        strength: 0.55,
        startYear: journey.year,
        endYear: end,
        placeId: journey.toSettlementId,
      });
    }
  }

  for (const title of figure.titles ?? []) {
    if (title.fromYear > year) continue;
    if (title.toYear !== undefined && title.toYear < year) continue;
    evidence.push({
      kind: 'HeldOffice',
      strength: officeWeight(title.kind),
      startYear: title.fromYear,
      endYear: title.toYear !== undefined && title.toYear <= year ? title.toYear : year,
      placeId: title.scopeId,
      tag: title.kind,
    });
  }

  for (const dispute of figure.disputes ?? []) {
    if (dispute.startYear > year) continue;
    const endYear = dispute.endYear !== undefined && dispute.endYear <= year
      ? dispute.endYear
      : year;
    if (dispute.cause === 'PassedOverForOffice' || dispute.cause === 'SuccessionPassedOver') {
      evidence.push({
        kind: 'RejectedOffice',
        strength: 0.75,
        startYear: dispute.startYear,
        endYear,
        relatedFigureId: dispute.otherId,
        placeId: dispute.placeId,
      });
    }
    evidence.push({
      kind: 'Feud',
      strength: clamp01((endYear - dispute.startYear + 5) / 30),
      startYear: dispute.startYear,
      endYear,
      relatedFigureId: dispute.otherId,
      placeId: dispute.placeId,
    });
    if (dispute.outcome === 'Killed' && dispute.endYear !== undefined && dispute.endYear <= year) {
      evidence.push({
        kind: 'Killing',
        strength: 0.9,
        startYear: dispute.endYear,
        endYear: dispute.endYear,
        relatedFigureId: dispute.otherId,
        placeId: dispute.placeId,
      });
    }
  }

  for (const campaign of figure.campaigns ?? []) {
    if (campaign.year > year) continue;
    evidence.push({
      kind: 'Battle',
      strength: 0.65,
      startYear: campaign.year,
      endYear: campaign.year,
      placeId: campaign.battleId,
      tag: campaign.role,
    });
  }

  for (const undertaking of figure.undertakings ?? []) {
    if (undertaking.startYear > year) continue;
    if (undertaking.kind === 'TradeVenture') {
      if (
        undertaking.state === 'Succeeded' &&
        undertaking.endYear !== undefined &&
        undertaking.endYear <= year
      ) {
        evidence.push({
          kind: 'TradeSuccess',
          strength: 0.8,
          startYear: undertaking.startYear,
          endYear: undertaking.endYear,
          placeId: undertaking.destinationId,
        });
      }
      if (
        undertaking.state === 'Failed' &&
        undertaking.endYear !== undefined &&
        undertaking.endYear <= year
      ) {
        evidence.push({
          kind: 'TradeFailure',
          strength: 0.8,
          startYear: undertaking.startYear,
          endYear: undertaking.endYear,
          placeId: undertaking.destinationId,
        });
      }
    }
    if (
      undertaking.kind === 'Pilgrimage' &&
      undertaking.state === 'Succeeded' &&
      undertaking.endYear !== undefined &&
      undertaking.endYear <= year
    ) {
      evidence.push({
        kind: 'Pilgrimage',
        strength: 0.85,
        startYear: undertaking.startYear,
        endYear: undertaking.endYear,
        placeId: undertaking.destinationId,
      });
    }
  }

  for (const plot of figure.plots ?? []) {
    if (plot.startYear > year) continue;
    const witting =
      plot.leaderId === figure.id ||
      plot.members.some((member) => member.figureId === figure.id && member.witting);
    if (!witting) continue;
    const endYear = plot.endYear !== undefined && plot.endYear <= year ? plot.endYear : year;
    evidence.push({
      kind: 'Revolt',
      strength: 0.85,
      startYear: plot.startYear,
      endYear,
      relatedFigureId: plot.targetId,
      placeId: plot.placeId,
      tag: plot.objective,
    });
  }

  const scribe = figure.occupation === 'Scribe';
  const observations = (figure.observations ?? []).some((o) => o.year <= year);
  const claims = (figure.claims ?? []).length > 0;
  if (scribe || observations || claims) {
    evidence.push({
      kind: 'Study',
      strength: scribe ? 0.75 : 0.6,
      startYear: figure.birthYear + (scribe ? 18 : 0),
      endYear: year,
      tag: scribe ? 'Scribe' : observations ? 'Observation' : 'Claim',
    });
  }

  const discoveryYears = new Set<number>();
  for (const seen of figure.observations ?? []) {
    if (seen.year > year) continue;
    const first = seen.priorYear === undefined;
    const great = seen.grade === 'Great';
    if (!first && !great) continue;
    evidence.push({
      kind: 'Discovery',
      strength: great ? 0.85 : 0.7,
      startYear: seen.year,
      endYear: seen.year,
      placeId: seen.settlementId,
      tag: great ? 'Great' : 'First',
    });
    discoveryYears.add(seen.year);
  }
  for (const memory of figure.memories ?? []) {
    if (memory.kind !== 'Wonder' || memory.year > year) continue;
    if (discoveryYears.has(memory.year)) continue;
    evidence.push({
      kind: 'Discovery',
      strength: clamp01(memory.intensity),
      startYear: memory.year,
      endYear: memory.year,
      placeId: memory.locationId,
      tag: 'Wonder',
    });
  }

  return evidence;
}

function occupationStartYear(
  figure: Figure,
  year: number,
  events: HistoryEvent[],
): number {
  const taken = events
    .filter((event) => event.kind === 'OccupationTaken' && event.year <= year)
    .sort((a, b) => a.year - b.year);
  if (taken.length > 0) return taken[taken.length - 1]!.year;

  const remembered = (figure.memories ?? [])
    .filter((memory) => memory.sourceKind === 'OccupationTaken' && memory.year <= year)
    .sort((a, b) => a.year - b.year);
  if (remembered.length > 0) return remembered[remembered.length - 1]!.year;

  return figure.birthYear;
}

function officeWeight(kind: string): number {
  switch (kind) {
    case 'Ruler':
      return 1;
    case 'Regent':
      return 0.9;
    case 'HighPriest':
      return 0.85;
    case 'Marshal':
      return 0.8;
    case 'Governor':
      return 0.75;
    default:
      return 0.6;
  }
}

interface BiographyRule {
  dials: Partial<Record<BiographyDial, number>>;
  theme: BiographyTheme;
  required: EvidenceKind[];
  optional: EvidenceKind[];
  baseWeight: number;
  outcome?: (evidence: BiographyEvidence[]) => BiographyOutcome;
}

function commercialOutcome(evidence: BiographyEvidence[]): BiographyOutcome {
  const gain = evidence.some((e) => e.kind === 'TradeSuccess');
  const loss = evidence.some((e) => e.kind === 'TradeFailure');
  if (gain && !loss) return 'Positive';
  if (loss && !gain) return 'Negative';
  if (gain && loss) return 'Mixed';
  return 'Neutral';
}

const RULES: BiographyRule[] = [
  {
    dials: { Tradition: 0.6 },
    theme: 'Rootedness',
    required: ['LongResidence'],
    optional: ['LongOccupation', 'Friendship'],
    baseWeight: 1,
  },
  {
    dials: { Independence: 0.55 },
    theme: 'Rootedness',
    required: ['LongResidence', 'Migration'],
    optional: [],
    baseWeight: 0.95,
  },
  {
    dials: { Tradition: 0.65 },
    theme: 'EnduringLoyalty',
    required: ['Friendship'],
    optional: [],
    baseWeight: 0.8,
  },
  {
    dials: { Learning: 0.6 },
    theme: 'TransmissionOfKnowledge',
    required: ['Mentorship'],
    optional: ['LongOccupation', 'Study'],
    baseWeight: 1.1,
  },
  {
    dials: { Aggression: 0.55 },
    theme: 'ViolentConflict',
    required: ['Feud'],
    optional: ['Battle', 'Killing'],
    baseWeight: 1,
  },
  {
    dials: { Learning: 0.6 },
    theme: 'ScholarlyLife',
    required: ['Study'],
    optional: ['LongOccupation', 'Discovery'],
    baseWeight: 0.95,
  },
  {
    dials: { Learning: 0.55 },
    theme: 'ScholarlyLife',
    required: ['Discovery'],
    optional: ['Study'],
    baseWeight: 0.9,
  },
  {
    dials: { Piety: 0.6 },
    theme: 'ReligiousDevotion',
    required: ['ReligiousOffice'],
    optional: ['Pilgrimage'],
    baseWeight: 1,
  },
  {
    dials: { Mercantile: 0.55 },
    theme: 'CommercialSuccess',
    required: ['TradeSuccess'],
    optional: ['TradeJourney'],
    baseWeight: 1,
    outcome: commercialOutcome,
  },
  {
    dials: { Mercantile: 0.55 },
    theme: 'CommercialFailure',
    required: ['TradeFailure'],
    optional: ['TradeJourney'],
    baseWeight: 0.9,
    outcome: commercialOutcome,
  },
  {
    dials: { Centralism: 0.6 },
    theme: 'ConsolidationOfPower',
    required: ['HeldOffice'],
    optional: [],
    baseWeight: 1,
  },
  {
    dials: { Independence: 0.6 },
    theme: 'ResistanceToAuthority',
    required: ['RejectedOffice'],
    optional: ['Revolt'],
    baseWeight: 1,
  },
  {
    dials: { Centralism: 0.55 },
    theme: 'InstitutionalService',
    required: ['HeldOffice'],
    optional: ['LongOccupation'],
    baseWeight: 0.85,
  },
  {
    dials: { Independence: 0.55 },
    theme: 'FrontierLife',
    required: ['Migration'],
    optional: ['LongResidence'],
    baseWeight: 0.9,
  },
  {
    dials: { Expansionism: 0.6 },
    theme: 'TerritorialAmbition',
    required: ['Battle'],
    optional: ['HeldOffice'],
    baseWeight: 0.85,
  },
  {
    dials: { Independence: 0.65 },
    theme: 'Isolation',
    required: ['Migration'],
    optional: [],
    baseWeight: 0.75,
  },
  {
    dials: { Learning: 0.6, Piety: 0.6 },
    theme: 'ReligiousScholarship',
    required: ['ReligiousOffice', 'Study'],
    optional: ['Mentorship'],
    baseWeight: 1.25,
  },
  {
    dials: { Tradition: 0.55 },
    theme: 'Betrayal',
    required: ['Betrayal'],
    optional: ['Friendship'],
    baseWeight: 1,
    outcome: () => 'Negative',
  },
];

function matchingEvidence(rule: BiographyRule, evidence: BiographyEvidence[]): BiographyEvidence[] {
  const kinds = new Set<EvidenceKind>([...rule.required, ...rule.optional]);
  return evidence.filter((e) => kinds.has(e.kind));
}

function scoreRule(
  rule: BiographyRule,
  disposition: Disposition | undefined,
  evidence: BiographyEvidence[],
  year: number,
): number {
  let dialProduct = 1;
  for (const [dial, min] of Object.entries(rule.dials) as [BiographyDial, number][]) {
    const value = dialValue(disposition, dial);
    if (value < min) return 0;
    dialProduct *= value;
  }

  for (const required of rule.required) {
    if (!evidence.some((e) => e.kind === required)) return 0;
  }

  const matching = matchingEvidence(rule, evidence);
  const evidenceStrength =
    matching.length === 0 ? 0 : matching.reduce((sum, e) => sum + e.strength, 0) / matching.length;
  const avgSpan =
    matching.length === 0
      ? 1
      : matching.reduce((sum, e) => sum + Math.max(1, e.endYear - e.startYear), 0) / matching.length;
  const durationFactor = clamp01(0.6 + avgSpan / 50);
  const avgEnd =
    matching.length === 0 ? year : matching.reduce((sum, e) => sum + e.endYear, 0) / matching.length;
  const recencyFactor = clamp01(1 - Math.max(0, year - avgEnd) / 60);
  const peak = matching.length === 0 ? 1 : Math.max(...matching.map((e) => e.strength));
  const distinctiveness = clamp01(0.7 + peak * 0.3);

  return (
    rule.baseWeight *
    dialProduct *
    (0.5 + 0.5 * evidenceStrength) *
    durationFactor *
    recencyFactor *
    distinctiveness
  );
}

export function evaluateInterpretations(
  figure: Figure,
  year: number,
  events: HistoryEvent[] = [],
): BiographyInterpretation[] {
  const evidence = extractEvidence(figure, year, events);
  const candidates: BiographyInterpretation[] = [];

  for (const rule of RULES) {
    const score = scoreRule(rule, figure.disposition, evidence, year);
    if (score <= 0) continue;
    const matched = matchingEvidence(rule, evidence);
    candidates.push({
      theme: rule.theme,
      dials: rule.dials,
      score,
      evidence: matched,
      outcome: rule.outcome?.(matched) ?? 'Neutral',
    });
  }

  return candidates;
}

function tooSimilar(a: BiographyTheme, b: BiographyTheme): boolean {
  if (a === b) return true;
  if (themeGroup(a) === themeGroup(b)) return true;
  if (
    (a === 'Rootedness' && b === 'Isolation') ||
    (a === 'Isolation' && b === 'Rootedness')
  )
    return true;
  if (
    (a === 'CommercialSuccess' && b === 'CommercialFailure') ||
    (a === 'CommercialFailure' && b === 'CommercialSuccess')
  )
    return true;
  if (
    (a === 'ConsolidationOfPower' && b === 'InstitutionalService') ||
    (a === 'InstitutionalService' && b === 'ConsolidationOfPower')
  )
    return true;
  return false;
}

export function selectInterpretations(
  candidates: BiographyInterpretation[],
  maxCount = 3,
): BiographyInterpretation[] {
  if (maxCount <= 0 || candidates.length === 0) return [];
  const selected: BiographyInterpretation[] = [];
  const usedGroups = new Set<BiographyThemeGroup>();

  for (const candidate of [...candidates].sort((a, b) => b.score - a.score)) {
    const group = themeGroup(candidate.theme);
    if (usedGroups.has(group)) continue;
    if (selected.some((existing) => tooSimilar(existing.theme, candidate.theme))) continue;
    selected.push(candidate);
    usedGroups.add(group);
    if (selected.length >= maxCount) break;
  }

  return selected;
}

export function buildInterpretations(
  figure: Figure,
  year: number,
  maxCount = 3,
  events: HistoryEvent[] = [],
): BiographyInterpretation[] {
  return selectInterpretations(evaluateInterpretations(figure, year, events), maxCount);
}

export function primaryInterpretationTheme(
  figure: Figure,
  year: number,
  events: HistoryEvent[] = [],
): BiographyTheme | undefined {
  return buildInterpretations(figure, year, 1, events)[0]?.theme;
}

function figureDiscriminator(id: EntityId): number {
  const match = /^fig:(\d+)$/.exec(id);
  return match ? Number(match[1]) : 0;
}

function variantIndex(figureId: EntityId, theme: BiographyTheme, index: number): number {
  const disc = figureDiscriminator(figureId);
  const hash =
    (Math.imul(disc, VARIANT_MIX) >>> 0) +
    (Math.imul(theme.charCodeAt(0), 0x9e3779b9) >>> 0) +
    index;
  return hash % 3;
}

function firstEvidence(
  interpretation: BiographyInterpretation,
  kind: EvidenceKind,
): BiographyEvidence {
  const found = interpretation.evidence.find((e) => e.kind === kind);
  if (!found) throw new Error(`missing evidence ${kind}`);
  return found;
}

function pronouns(sex: Sex | undefined) {
  switch (sex) {
    case 'Female':
      return { subject: 'She', object: 'her', possessive: 'her', reflexive: 'herself', plural: false };
    case 'Male':
      return { subject: 'He', object: 'him', possessive: 'his', reflexive: 'himself', plural: false };
    default:
      return { subject: 'They', object: 'them', possessive: 'their', reflexive: 'themself', plural: true };
  }
}

export function renderInterpretationParts(
  figure: Figure,
  interpretation: BiographyInterpretation,
  ctx: LifeContext,
  variant: number,
): StandingPart[] {
  const name = figure.name;
  const { possessive, object, subject, reflexive } = pronouns(figure.sex);
  const placeName = (id?: EntityId) =>
    id ? ctx.nameOf(id) : 'an unrecorded place';

  const text = (value: string): StandingPart => ({ type: 'text', text: value });
  const entity = (id: EntityId): StandingPart => ({ type: 'entity', id });

  switch (interpretation.theme) {
    case 'Rootedness': {
      const residence = firstEvidence(interpretation, 'LongResidence');
      const place = placeName(residence.placeId);
      if (interpretation.dials.Independence !== undefined) {
        const lines = [
          `${name} made a home at ${place} after leaving the old seats behind.`,
          `${place} became ${possessive} ground, chosen rather than inherited.`,
          `Having gone out, ${name} put down roots at ${place} on ${possessive} own terms.`,
        ];
        return [text(lines[variant] ?? lines[0])];
      }
      if (residence.strength > 0.8) {
        const lines = [
          `${name} spent most of ${possessive} life rooted in ${place}.`,
          `${name} rarely strayed far from ${place}, where the record first found ${object}.`,
          `Familiar places and established ways remained important to ${name}, above all at ${place}.`,
        ];
        return [text(lines[variant] ?? lines[0])];
      }
      const lines = [
        `${name} remained closely tied to ${place} for much of ${possessive} life.`,
        `${name} kept returning to ${place} as though ${possessive} life had never truly left it.`,
        `The town at ${place} shaped ${name} more than any office ${possessive} years later brought.`,
      ];
      return [text(lines[variant] ?? lines[0])];
    }
    case 'EnduringLoyalty': {
      const friendship = firstEvidence(interpretation, 'Friendship');
      const years = friendship.endYear - friendship.startYear;
      const friendId = friendship.relatedFigureId!;
      const lines = [
        () => [
          text(`${name} and `),
          entity(friendId),
          text(` stood by one another for ${years} years, a tie the record never forgot.`),
        ],
        () => [
          text(`Through ${years} years, ${name} kept faith with `),
          entity(friendId),
          text(' when lesser ties cooled.'),
        ],
        () => [
          entity(friendId),
          text(
            ` remained at the centre of ${name}'s life for ${years} years — loyalty the chronicle could name.`,
          ),
        ],
      ];
      return lines[variant]?.() ?? lines[0]();
    }
    case 'TransmissionOfKnowledge': {
      const mentorship = firstEvidence(interpretation, 'Mentorship');
      const mentor = figure.mentorships?.some(
        (m) => m.mentorId === figure.id && m.apprenticeId === mentorship.relatedFigureId,
      );
      const otherId = mentorship.relatedFigureId!;
      if (mentor) {
        const lines = [
          () => [
            text(`${name} passed down what ${possessive} trade had taught ${object} to `),
            entity(otherId),
            text('.'),
          ],
          () => [text('Much of what '), entity(otherId), text(` knew came through ${name}'s teaching.`)],
          () => [
            text(`${name} shaped `),
            entity(otherId),
            text("'s path as a mentor the record still names."),
          ],
        ];
        return lines[variant]?.() ?? lines[0]();
      }
      const lines = [
        () => [
          text(`${name} learned the craft under `),
          entity(otherId),
          text(', and carried it forward.'),
        ],
        () => [
          text(`What ${name} became owed a great deal to `),
          entity(otherId),
          text("'s instruction."),
        ],
        () => [
          entity(otherId),
          text(`'s teaching left a lasting mark on ${name}'s life.`),
        ],
      ];
      return lines[variant]?.() ?? lines[0]();
    }
    case 'ViolentConflict': {
      const feud = firstEvidence(interpretation, 'Feud');
      const rivalId = feud.relatedFigureId!;
      if (interpretation.evidence.some((e) => e.kind === 'Killing')) {
        const lines = [
          () => [
            text(`${name}'s quarrel with `),
            entity(rivalId),
            text(' ended in blood the record could not soften.'),
          ],
          () => [
            text('Violence between '),
            entity(rivalId),
            text(` and ${name} closed a feud that had long been gathering.`),
          ],
          () => [
            text(`${name} and `),
            entity(rivalId),
            text(' settled their grievance at the edge of a blade.'),
          ],
        ];
        return lines[variant]?.() ?? lines[0]();
      }
      const lines = [
        () => [
          text(`${name} carried a long quarrel with `),
          entity(rivalId),
          text(' through the public life of the record.'),
        ],
        () => [
          text('A feud with '),
          entity(rivalId),
          text(` shadowed ${name}'s years more than any single battle.`),
        ],
        () => [
          text(`${name} and `),
          entity(rivalId),
          text(' remained locked in a dispute the chronicle followed for years.'),
        ],
      ];
      return lines[variant]?.() ?? lines[0]();
    }
    case 'CommercialSuccess': {
      const lines = [
        `Trade brought ${name} considerable prosperity.`,
        `${name}'s ventures on the road returned with gain the record could count.`,
        `Mercantile work rewarded ${name} more often than it failed ${object}.`,
      ];
      return [text(lines[variant] ?? lines[0])];
    }
    case 'CommercialFailure': {
      const lines = [
        `${name} repeatedly sought ${possessive} fortune through trade, though little of it endured.`,
        `The road took more from ${name}'s ventures than it ever gave back.`,
        `Trade occupied ${name} for years, but the record remembers more loss than gain.`,
      ];
      return [text(lines[variant] ?? lines[0])];
    }
    case 'ScholarlyLife': {
      if (interpretation.evidence.some((e) => e.kind === 'Discovery')) {
        const found = firstEvidence(interpretation, 'Discovery');
        const place = placeName(found.placeId);
        const lines = [
          `${name} wrote down a sighting at ${place} the record had not held before.`,
          `What ${name} saw in the sky stayed in the chronicle when others let it pass.`,
          `The first record of that apparition is in ${possessive} hand.`,
        ];
        return [text(lines[variant] ?? lines[0])];
      }
      const lines = [
        `${name} lived among books, observations, and the work of making sense of them.`,
        `Learning ran through ${name}'s life — not as ornament, but as habit.`,
        `The record remembers ${name} as one who studied, copied, and kept what others let pass.`,
      ];
      return [text(lines[variant] ?? lines[0])];
    }
    case 'ReligiousDevotion': {
      if (interpretation.evidence.some((e) => e.kind === 'Pilgrimage')) {
        const trip = firstEvidence(interpretation, 'Pilgrimage');
        const place = placeName(trip.placeId);
        const lines = [
          `Faith led ${name} to ${place} on pilgrimage, and the journey stayed in the record.`,
          `${name} sought the holy at ${place}, and the chronicle kept the road.`,
          `Pilgrimage to ${place} marked ${name}'s devotion in a way office alone could not.`,
        ];
        return [text(lines[variant] ?? lines[0])];
      }
      const lines = [
        `${name} served the faith in office, and the record treated that service as ${possessive} life's spine.`,
        `Religious duty shaped ${name}'s public years more than trade or arms.`,
        `${name} held the faith's work close through the offices ${possessive} life accumulated.`,
      ];
      return [text(lines[variant] ?? lines[0])];
    }
    case 'ReligiousScholarship': {
      const lines = [
        `${name} joined religious office to scholarly habit — doctrine learned and kept.`,
        `The record shows ${name} as both servant of the faith and student of its teaching.`,
        `${name} read, served, and copied within the same devout life.`,
      ];
      return [text(lines[variant] ?? lines[0])];
    }
    case 'ConsolidationOfPower': {
      const lines = [
        `${name} gathered power into ${possessive} own hands rather than leave it scattered.`,
        `Office after office, ${name} made authority personal.`,
        `The record shows ${name} bending institutions toward a single will.`,
      ];
      return [text(lines[variant] ?? lines[0])];
    }
    case 'InstitutionalService': {
      const lines = [
        `${name} served the institutions of the realm long and faithfully.`,
        `Public office defined ${name}'s mature years more than private ambition.`,
        `${name} held posts the chronicle could list and did not hurry to leave them.`,
      ];
      return [text(lines[variant] ?? lines[0])];
    }
    case 'ResistanceToAuthority': {
      if (interpretation.evidence.some((e) => e.kind === 'Revolt')) {
        const lines = [
          `${name} plotted against authority the record later exposed.`,
          `Conspiracy marked ${name}'s years when office alone could not satisfy ${object}.`,
          `${name} turned from subject to conspirator when power closed its doors.`,
        ];
        return [text(lines[variant] ?? lines[0])];
      }
      const lines = [
        `${name} resented the offices passed to others and did not hide it.`,
        `Being passed over left a wound in ${name}'s public life the record followed.`,
        `${name} chafed under authority that never quite admitted ${object}.`,
      ];
      return [text(lines[variant] ?? lines[0])];
    }
    case 'FrontierLife': {
      const migration = firstEvidence(interpretation, 'Migration');
      const place = placeName(migration.placeId);
      const lines = [
        `${name} made a life at the edge of the known world, settling at ${place}.`,
        `Migration brought ${name} to ${place}, and there ${possessive} story stayed.`,
        `${name} left the old seats behind and rooted ${reflexive} anew at ${place}.`,
      ];
      return [text(lines[variant] ?? lines[0])];
    }
    case 'TerritorialAmbition': {
      const lines = [
        `${name} pressed outward — in war, office, and the reach of ${possessive} ambitions.`,
        `The record remembers ${name} on campaign and in command more than at rest.`,
        `Territory and victory mattered to ${name} in a way peace alone never could.`,
      ];
      return [text(lines[variant] ?? lines[0])];
    }
    case 'Isolation': {
      const lines = [
        `${name} kept apart from the centres where others sought favour.`,
        `Distance and independence marked ${name}'s path more than courtly tie.`,
        `${name} lived at a remove from the seats that shaped most lives around ${object}.`,
      ];
      return [text(lines[variant] ?? lines[0])];
    }
    case 'Betrayal': {
      const betrayal = firstEvidence(interpretation, 'Betrayal');
      const otherId = betrayal.relatedFigureId!;
      const turned = interpretation.evidence.some((e) => e.kind === 'Betrayal' && e.tag === 'Turned');
      if (turned) {
        const lines = [
          () => [
            text(`${name} turned on `),
            entity(otherId),
            text(', and the record did not forget who broke the tie.'),
          ],
          () => [
            text('Faith with '),
            entity(otherId),
            text(` ended when ${name} broke it.`),
          ],
          () => [
            text(`${name} betrayed `),
            entity(otherId),
            text(' — a wrong the chronicle kept.'),
          ],
        ];
        return lines[variant]?.() ?? lines[0]();
      }
      const lines = [
        () => [
          entity(otherId),
          text(` turned on ${name}, and the record named the broken faith.`),
        ],
        () => [
          text(`${name} was betrayed by `),
          entity(otherId),
          text(', a wound the chronicle still carries.'),
        ],
        () => [
          text('The tie with '),
          entity(otherId),
          text(` ended in betrayal that shadowed ${name}'s later years.`),
        ],
      ];
      return lines[variant]?.() ?? lines[0]();
    }
    default: {
      const fallback = `${subject} lived in a way the record can name through ${THEME_LABELS[interpretation.theme].toLowerCase()}.`;
      return [text(fallback)];
    }
  }
}

export function interpretationSentenceParts(
  figure: Figure,
  year: number,
  ctx: LifeContext,
  maxCount = 3,
): StandingPart[] {
  const standing = standingYear(figure, year);
  const events = ctx.eventsFor(figure.id);
  const interpretations = buildInterpretations(figure, standing, maxCount, events);
  if (interpretations.length === 0) return [];

  const parts: StandingPart[] = [];
  interpretations.forEach((interpretation, index) => {
    const variant = variantIndex(figure.id, interpretation.theme, index);
    const rendered = renderInterpretationParts(figure, interpretation, ctx, variant);
    if (index === 0) {
      parts.push(...rendered);
    } else {
      parts.push({ type: 'text', text: ' ' });
      parts.push(...rendered);
    }
  });
  return parts;
}

/** Rounded semantic statement for parity tests against the C# engine. */
export function interpretationSignature(
  interpretation: BiographyInterpretation,
): string {
  const dials = Object.entries(interpretation.dials)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([dial, min]) => `${dial}:${min}`)
    .join(',');
  const evidence = interpretation.evidence
    .map((e) => e.kind)
    .sort()
    .join('+');
  return `${interpretation.theme}|${dials}|${interpretation.outcome}|${evidence}|${interpretation.score.toFixed(3)}`;
}

export function biographySignatures(
  figure: Figure,
  year: number,
  events: HistoryEvent[] = [],
): string[] {
  return buildInterpretations(figure, year, 3, events).map(interpretationSignature);
}
