/**
 * The claim record, read as a section rather than as a footnote to a life.
 *
 * A claim is exported on the figure who made it, and its travels are exported as a flat list of
 * dated transitions. Neither shape answers the questions the Knowledge section asks — what was
 * said about one thing, who came to hold it, what carried it there, and what became of it — so
 * this gathers both into one reading per loaded world.
 *
 * Three rules the whole module is written to keep, all of them from `DESIGN.md` → Core contracts
 * → Knowledge:
 *
 *   - **A verdict is not a belief.** `verdict` says how a claim stands to measurable reality at a
 *     named year. Nothing here derives holding, standing, or importance from it, and a refuted
 *     reading with two centuries of copies reads as exactly that.
 *   - **Doctrine is never graded.** A mythic reading is a held position, not a wrong answer. It
 *     is classified by what could settle it — nothing — and no score, error or ranking is
 *     computed for it anywhere.
 *   - **The truth stays on the cosmology page.** The world's real comet period is not read here.
 *     Error is not derived, stored, or shown; the sky's own verdict is the only judgement.
 */

import { artifactOf, figureOf, type World } from './store.ts';
import type {
  Artifact,
  Claim,
  ClaimStanding,
  ClaimStandingChange,
  ClaimTransition,
  EntityId,
  Figure,
} from './types.ts';

/**
 * A field of knowledge, named by what it is about rather than by who studies it.
 *
 * One member, because `ClaimSubjectKind` has one member. Rows appear as the engine gains
 * subjects; the page shows only the ones an export populates.
 */
export type KnowledgeDomain = 'Astronomy';

export const KNOWLEDGE_DOMAIN_LABELS: Record<KnowledgeDomain, string> = {
  Astronomy: 'Astronomy',
};

/**
 * What can settle a thing somebody held — the column of the overview matrix.
 *
 * Not observed / understood / practiced: "understood" cannot be shown without the engine
 * holding a correct answer to grade a civilization against, and that is the one thing this
 * section must never do.
 */
export type KnowledgeColumn = 'Observation' | 'Practice' | 'Doctrine';

export const KNOWLEDGE_COLUMNS: KnowledgeColumn[] = ['Observation', 'Practice', 'Doctrine'];

export const KNOWLEDGE_COLUMN_LABELS: Record<KnowledgeColumn, string> = {
  Observation: 'Observation',
  Practice: 'Practice',
  Doctrine: 'Doctrine',
};

export const KNOWLEDGE_COLUMN_NOTES: Record<KnowledgeColumn, string> = {
  Observation: 'What was written down, and what was derived from it. The sky can answer it.',
  Practice: 'What could actually be done. Answered by the object or the harvest, never by a proposition.',
  Doctrine: 'What was believed about why. Nothing settles it, and nothing here grades it.',
};

/**
 * Which column a register belongs in.
 *
 * A measured reading states a number the sky can meet or miss; a mythic one states what the
 * light is for. That is a difference in what could settle them, not in how right they are.
 */
export function columnOfRegister(claim: Claim): KnowledgeColumn {
  return claim.register === 'Measured' ? 'Observation' : 'Doctrine';
}

/** Every subject the engine can export, placed in the field it belongs to. */
export function domainOfClaim(claim: Claim): KnowledgeDomain {
  // `subject` is absent before schema 52, where every claim was a comet period.
  switch (claim.subject ?? 'CometPeriod') {
    case 'CometPeriod':
    default:
      return 'Astronomy';
  }
}

/** What a claim is about, said the way a reader would say it. */
export function subjectLabel(claim: Claim): string {
  switch (claim.subject ?? 'CometPeriod') {
    case 'CometPeriod':
    default:
      // Named the way the cosmology page names it, so a reader crossing between the two is
      // looking at the same object.
      return `Comet ${claim.cometIndex}`;
  }
}

/** One realm's unbroken possession of one claim, folded out of its transitions. */
export interface Holding {
  realmId?: EntityId;
  fromYear: number;
  /** Absent while the realm still held it at the end of the record. */
  toYear?: number;
  acquired: ClaimTransition;
  /** The transition that ended it, where it ended. */
  lost?: ClaimTransition;
  /**
   * What the realm made of it, where the span ended.
   *
   * `Received` on an export older than schema 55, which is what every holding began as and the
   * only thing such a file can honestly be read as saying.
   */
  standing: ClaimStanding;
  /** How that changed while the span lasted, earliest first. Empty before schema 55. */
  standings: ClaimStandingChange[];
}

/** One claim, with everything the record says about where it went. */
export interface ClaimRecord {
  /** Addresses the claim across the world, the way a transition and a tome do. */
  key: string;
  claimantId: EntityId;
  claimant?: Figure;
  claim: Claim;
  domain: KnowledgeDomain;
  column: KnowledgeColumn;
  /** Dated changes in who held it, earliest first. Empty before schema 53. */
  transitions: ClaimTransition[];
  /** Possession spans per realm, in the order they began. */
  holdings: Holding[];
  /** Realms still holding it at the end of the record. */
  heldNow: EntityId[];
  /** Every realm that ever held it, in the order they first did. */
  everHeld: EntityId[];
  /** Written works that set this reading down. */
  texts: Artifact[];
  /** Whether a holding of it ended — the loss the record can account for. */
  wasLost: boolean;
}

/** One cell of the overview matrix: a domain and what settles it. */
export interface KnowledgeCell {
  domain: KnowledgeDomain;
  column: KnowledgeColumn;
  claims: ClaimRecord[];
  /** Sightings underlying this cell. Only the observation column has any. */
  observations: number;
  /** Realms that ever held one of these readings. */
  realms: number;
}

export interface KnowledgeIndex {
  claims: ClaimRecord[];
  byKey: ReadonlyMap<string, ClaimRecord>;
  /** Rows the export actually populates, in domain order. */
  domains: KnowledgeDomain[];
  /** Columns the export actually populates. A column with no record is not a column. */
  columns: KnowledgeColumn[];
  cells: KnowledgeCell[];
  cellAt: (domain: KnowledgeDomain, column: KnowledgeColumn) => KnowledgeCell | undefined;
  /** Sightings written down anywhere in the world. */
  observations: number;
  /** Whether this export carries the dated holdings at all — schema 53 and later. */
  hasTransitions: boolean;
  /** Whether it says what realms made of them — schema 55 and later. */
  hasStandings: boolean;
  /** Realms that ever held any reading. */
  realmsHolding: EntityId[];
  /** Written works carrying at least one reading. */
  texts: Artifact[];
}

/** Addresses one claim across the world, matching how a transition and a tome name one. */
export function claimKey(claimantId: EntityId, claimId: number): string {
  return `${claimantId}/${claimId}`;
}

/** Splits a key back into the pair that made it, for a route. */
export function parseClaimKey(key: string): { claimantId: EntityId; claimId: number } | undefined {
  const cut = key.lastIndexOf('/');
  if (cut <= 0) return undefined;
  const claimId = Number(key.slice(cut + 1));
  if (!Number.isInteger(claimId)) return undefined;
  return { claimantId: key.slice(0, cut), claimId };
}

const cache = new WeakMap<World, KnowledgeIndex>();

/**
 * One reading per loaded world, shared by the section's pages.
 *
 * Built in a single pass over figures, transitions and artifacts rather than per claim: a
 * millennium world holds tens of thousands of figures and a claim page must cost a lookup.
 */
export function readKnowledge(world: World): KnowledgeIndex {
  const existing = cache.get(world);
  if (existing) return existing;

  const byKey = new Map<string, ClaimRecord>();
  const claims: ClaimRecord[] = [];
  let observations = 0;

  for (const figure of world.export.figures) {
    observations += figure.observations?.length ?? 0;

    for (const claim of figure.claims ?? []) {
      const record: ClaimRecord = {
        key: claimKey(figure.id, claim.id),
        claimantId: figure.id,
        claimant: figure,
        claim,
        domain: domainOfClaim(claim),
        column: columnOfRegister(claim),
        transitions: [],
        holdings: [],
        heldNow: [],
        everHeld: [],
        texts: [],
        wasLost: false,
      };
      byKey.set(record.key, record);
      claims.push(record);
    }
  }

  for (const transition of world.export.claimTransitions ?? []) {
    byKey.get(claimKey(transition.claimantId, transition.claimId))?.transitions.push(transition);
  }

  const standingsByKey = new Map<string, ClaimStandingChange[]>();
  for (const change of world.export.claimStandings ?? []) {
    const key = claimKey(change.claimantId, change.claimId);
    const kept = standingsByKey.get(key);
    if (kept) kept.push(change);
    else standingsByKey.set(key, [change]);
  }

  for (const artifact of world.export.artifacts) {
    for (const ref of artifact.tomeContents?.carries ?? []) {
      byKey.get(claimKey(ref.claimantId, ref.claimId))?.texts.push(artifact);
    }
  }

  const texts: Artifact[] = [];
  const realmsHolding: EntityId[] = [];
  const seenRealm = new Set<EntityId>();
  const seenText = new Set<EntityId>();

  for (const record of claims) {
    // Sorting per claim rather than sorting the whole export: the transitions of one claim are a
    // handful, and the export's order is already the order they happened in for all but ties.
    record.transitions.sort((a, b) => a.year - b.year || a.kind.localeCompare(b.kind));
    foldHoldings(record, standingsByKey.get(record.key) ?? []);

    for (const realmId of record.everHeld) {
      if (seenRealm.has(realmId)) continue;
      seenRealm.add(realmId);
      realmsHolding.push(realmId);
    }
    for (const text of record.texts) {
      if (seenText.has(text.id)) continue;
      seenText.add(text.id);
      texts.push(text);
    }
  }

  claims.sort((a, b) => a.claim.year - b.claim.year || a.key.localeCompare(b.key));

  const cells: KnowledgeCell[] = [];
  for (const domain of Object.keys(KNOWLEDGE_DOMAIN_LABELS) as KnowledgeDomain[]) {
    for (const column of KNOWLEDGE_COLUMNS) {
      const inCell = claims.filter(
        (record) => record.domain === domain && record.column === column,
      );
      if (inCell.length === 0) continue;

      const realms = new Set<EntityId>();
      for (const record of inCell) for (const realmId of record.everHeld) realms.add(realmId);

      cells.push({
        domain,
        column,
        claims: inCell,
        // Sightings belong to the column the sky can answer. Attaching them to doctrine would
        // suggest a mythic reading rests on measurement it never claimed to.
        observations: column === 'Observation' ? observations : 0,
        realms: realms.size,
      });
    }
  }

  const index: KnowledgeIndex = {
    claims,
    byKey,
    domains: distinctInOrder(cells.map((cell) => cell.domain)),
    columns: KNOWLEDGE_COLUMNS.filter((column) => cells.some((cell) => cell.column === column)),
    cells,
    cellAt: (domain, column) =>
      cells.find((cell) => cell.domain === domain && cell.column === column),
    observations,
    hasTransitions: (world.export.claimTransitions ?? []).length > 0,
    hasStandings: (world.export.claimStandings ?? []).length > 0,
    realmsHolding,
    texts,
  };

  cache.set(world, index);
  return index;
}

/**
 * Turns a claim's transitions into possession spans.
 *
 * Acquisitions and losses arrive as separate rows because the state at a year folds out of
 * them, the way territory folds out of transfers. A realm can acquire, lose and re-acquire the
 * same reading — the last is rediscovery, and it must read as a second span rather than as an
 * amendment to the first.
 */
function foldHoldings(record: ClaimRecord, standings: ClaimStandingChange[]): void {
  const open = new Map<string, Holding>();
  const everHeld: EntityId[] = [];
  const seen = new Set<EntityId>();

  for (const transition of record.transitions) {
    const key = transition.realmId ?? '';

    if (transition.kind === 'Acquired') {
      if (open.has(key)) continue;
      const holding: Holding = {
        realmId: transition.realmId,
        fromYear: transition.year,
        acquired: transition,
        // Arrival is not adoption: a span begins as a thing the realm has, and becomes whatever
        // the standing changes inside it say it became.
        standing: 'Received',
        standings: [],
      };
      open.set(key, holding);
      record.holdings.push(holding);
      if (transition.realmId && !seen.has(transition.realmId)) {
        seen.add(transition.realmId);
        everHeld.push(transition.realmId);
      }
      continue;
    }

    const holding = open.get(key);
    // A loss with no open span is a claim the record cannot account for. Dropping it silently
    // would hide exactly the gap the transitions exist to make visible, so it becomes a span
    // that ends the year it started, which reads as the anomaly it is.
    if (!holding) {
      const orphan: Holding = {
        realmId: transition.realmId,
        fromYear: transition.year,
        toYear: transition.year,
        acquired: transition,
        lost: transition,
        standing: 'Received',
        standings: [],
      };
      record.holdings.push(orphan);
      record.wasLost = true;
      continue;
    }

    holding.toYear = transition.year;
    holding.lost = transition;
    open.delete(key);
    record.wasLost = true;
  }

  record.heldNow = record.holdings
    .filter((holding) => holding.toYear === undefined && holding.realmId !== undefined)
    .map((holding) => holding.realmId!);
  record.everHeld = everHeld;

  placeStandings(record, standings);
}

/**
 * Files each standing change under the span of possession it happened inside.
 *
 * A realm can lose a reading and come by it again centuries later, and the second span starts at
 * `Received` like any other arrival — so a change is placed by year rather than by realm alone,
 * and one that falls in no span is dropped rather than attached to the nearest. That case is a
 * standing for a reading the realm was not holding, which the engine does not write.
 */
function placeStandings(record: ClaimRecord, standings: ClaimStandingChange[]): void {
  if (standings.length === 0) return;

  for (const change of [...standings].sort((a, b) => a.year - b.year)) {
    for (const holding of record.holdings) {
      if (holding.realmId !== change.realmId) continue;
      if (change.year < holding.fromYear) continue;
      if (holding.toYear !== undefined && change.year > holding.toYear) continue;

      holding.standings.push(change);
      holding.standing = change.to;
      break;
    }
  }
}

function distinctInOrder<T>(values: T[]): T[] {
  const seen = new Set<T>();
  const kept: T[] = [];
  for (const value of values) {
    if (seen.has(value)) continue;
    seen.add(value);
    kept.push(value);
  }
  return kept;
}

/** The claim a route names, or nothing when the id belongs to no claim in this world. */
export function claimRecordAt(world: World, key: string): ClaimRecord | undefined {
  return readKnowledge(world).byKey.get(key);
}

/**
 * Every claim ever made about the same thing, so two realms disagreeing is visible as such.
 *
 * Addressed by subject rather than by comet, which is what `ClaimSubjectKind` was added for:
 * the day a second subject exists, the gathering already works.
 */
export function claimsAboutTheSame(index: KnowledgeIndex, record: ClaimRecord): ClaimRecord[] {
  const subject = record.claim.subject ?? 'CometPeriod';
  const subjectIndex = record.claim.subjectIndex ?? record.claim.cometIndex;

  return index.claims.filter(
    (other) =>
      (other.claim.subject ?? 'CometPeriod') === subject &&
      (other.claim.subjectIndex ?? other.claim.cometIndex) === subjectIndex,
  );
}

/** The written works one figure wrote that carry a reading, for the provenance walk. */
export function textsWrittenBy(world: World, figureId: EntityId): Artifact[] {
  return world.export.artifacts.filter(
    (artifact) =>
      artifact.creatorId === figureId && (artifact.tomeContents?.carries?.length ?? 0) > 0,
  );
}

/** Resolves the carrier a transition names, whoever it turned out to be. */
export function carrierOf(
  world: World,
  transition: ClaimTransition,
): { label: string; id?: EntityId } {
  if (!transition.carrierId) {
    return { label: transition.carrier === 'Text' ? 'a written work' : 'their claimant' };
  }

  if (transition.carrier === 'Text') {
    const text = artifactOf(world, transition.carrierId);
    return { label: text?.name ?? world.nameOf(transition.carrierId), id: transition.carrierId };
  }

  const figure = figureOf(world, transition.carrierId);
  return { label: figure?.name ?? world.nameOf(transition.carrierId), id: transition.carrierId };
}
