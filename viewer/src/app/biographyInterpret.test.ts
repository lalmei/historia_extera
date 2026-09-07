import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  biographySignatures,
  buildInterpretations,
  extractEvidence,
  selectInterpretations,
  standingYear,
} from './biographyInterpret.ts';
import { standingAt, standingSentence, type LifeContext } from './biography.ts';
import type { Affinity, Disposition, Figure, HistoryEvent, Occupation } from './types.ts';

const DISPOSITION: Disposition = {
  aggression: 0.5,
  expansionism: 0.5,
  piety: 0.5,
  tradition: 0.5,
  mercantile: 0.5,
  learning: 0.5,
  centralism: 0.5,
  independence: 0.5,
};

function person(overrides: Partial<Figure> = {}): Figure {
  return {
    id: 'fig:1',
    name: 'Kullerwa',
    sex: 'Female',
    civilizationId: 'civ:1',
    cultureId: 'cul:1',
    birthYear: 10,
    residences: [],
    origin: 'Born',
    occupation: 'None',
    craft: 'None',
    disposition: DISPOSITION,
    titles: [],
    service: [],
    campaigns: [],
    journeys: [],
    bonds: [],
    memories: [],
    feelings: { grief: 0, fear: 0, anger: 0, pride: 0, loyalty: 0 },
    injuries: [],
    undertakings: [],
    disputes: [],
    affinities: [],
    betrayals: [],
    plots: [],
    guardianships: [],
    mentorships: [],
    observations: [],
    claims: [],
    childIds: [],
    spouseIds: [],
    ...overrides,
  };
}

function context(figures: Figure[], events: HistoryEvent[] = []): LifeContext {
  const byId = new Map(figures.map((figure) => [figure.id, figure]));
  return {
    endYear: 200,
    figureOf: (id) => byId.get(id),
    eventsFor: (id) => events.filter((event) => event.subject === id),
    nameOf: (id) => byId.get(id)?.name ?? (id === 'set:9' ? 'Ilen' : id === 'set:1' ? 'Ashfen' : id),
  };
}

function spoken(figure: Figure, year: number, ctx: LifeContext, place?: string): string {
  return standingSentence(figure, standingAt(figure, year, ctx), ctx, place)
    .map((part) => (part.type === 'text' ? part.text : ctx.nameOf(part.id)))
    .join('');
}

interface SharedFixtureFile {
  cases: SharedFixtureCase[];
}

interface SharedFixtureCase {
  name: string;
  year: number;
  expected: string[];
  figure: {
    id: number;
    name: string;
    sex: Figure['sex'];
    birthYear: number;
    occupation: Occupation;
    disposition: Disposition;
    residences?: { settlementId: number; fromYear: number; reason: Figure['residences'][number]['reason'] }[];
    affinities?: { id: number; otherId: number; startYear: number; stage: Affinity['stage']; origin: Affinity['origin']; placeId: number }[];
    observations?: { cometIndex: number; year: number; grade: 'Faint' | 'Notable' | 'Great'; priorYear?: number }[];
  };
}

function hydrateFixture(spec: SharedFixtureCase['figure']): Figure {
  return person({
    id: `fig:${spec.id}`,
    name: spec.name,
    sex: spec.sex,
    birthYear: spec.birthYear,
    occupation: spec.occupation,
    disposition: spec.disposition,
    residences: (spec.residences ?? []).map((residence) => ({
      settlementId: `set:${residence.settlementId}`,
      fromYear: residence.fromYear,
      reason: residence.reason,
    })),
    affinities: (spec.affinities ?? []).map((affinity) => ({
      id: affinity.id,
      otherId: `fig:${affinity.otherId}`,
      sought: true,
      startYear: affinity.startYear,
      origin: affinity.origin,
      sourceKind: 'FigureBorn',
      sourceEntityId: `fig:${spec.id}`,
      placeId: `set:${affinity.placeId}`,
      stage: affinity.stage,
      outcome: 'Open' as const,
      lastActionYear: affinity.startYear,
      acts: [
        {
          year: affinity.startYear,
          sourceKind: 'FigureBorn',
          stage: affinity.stage,
          actorId: `fig:${spec.id}`,
          detail: 'friendship',
        },
      ],
    })),
    observations: (spec.observations ?? []).map((seen) => ({
      cometIndex: seen.cometIndex,
      year: seen.year,
      grade: seen.grade,
      priorYear: seen.priorYear,
      settlementId: 'set:1',
    })),
  });
}

test('long residence and tradition produce rootedness', () => {
  const figure = person({
    disposition: { ...DISPOSITION, tradition: 0.82 },
    residences: [{ settlementId: 'set:1', fromYear: 10, reason: 'Birth' }],
  });
  const evidence = extractEvidence(figure, 60);
  assert.ok(evidence.some((e) => e.kind === 'LongResidence'));
  const themes = buildInterpretations(figure, 60).map((i) => i.theme);
  assert.ok(themes.includes('Rootedness'));
});

test('friendship after the scrubber year does not fire early', () => {
  const friend = person({ id: 'fig:2', name: 'Ragny', birthYear: 5 });
  const affinity: Affinity = {
    id: 1,
    otherId: 'fig:2',
    sought: true,
    startYear: 45,
    endYear: undefined,
    origin: 'SharedResidence',
    sourceKind: 'FigureBorn',
    sourceEntityId: 'fig:1',
    placeId: 'set:1',
    stage: 'Friendship',
    outcome: 'Open',
    lastActionYear: 45,
    acts: [{ year: 45, sourceKind: 'FigureBorn', stage: 'Friendship', actorId: 'fig:1', detail: 'friendship' }],
  };
  const figure = person({
    disposition: { ...DISPOSITION, tradition: 0.7 },
    affinities: [affinity],
  });

  const early = buildInterpretations(figure, 40);
  assert.equal(early.length, 0);

  const later = buildInterpretations(figure, 60);
  assert.ok(later.some((i) => i.theme === 'EnduringLoyalty'));
});

test('selector suppresses themes from the same group', () => {
  const selected = selectInterpretations(
    [
      { theme: 'Rootedness', dials: {}, score: 0.9, evidence: [], outcome: 'Neutral' },
      { theme: 'Isolation', dials: {}, score: 0.85, evidence: [], outcome: 'Neutral' },
      { theme: 'EnduringLoyalty', dials: {}, score: 0.8, evidence: [], outcome: 'Neutral' },
    ],
    3,
  );
  assert.equal(selected.length, 2);
  assert.ok(!selected.some((i) => i.theme === 'Isolation'));
});

test('standing sentence uses interpretation prose instead of raw dial labels', () => {
  const spouse = person({ id: 'fig:9', name: 'Ainikka', birthYear: 8 });
  const child = person({ id: 'fig:2', birthYear: 35 });
  const figure = person({
    childIds: ['fig:2'],
    disposition: { ...DISPOSITION, tradition: 0.9 },
    residences: [{ settlementId: 'set:9', fromYear: 10, reason: 'Birth' }],
    bonds: [
      {
        otherId: 'fig:9',
        kinds: ['Spouse'],
        sinceYear: 30,
        lastChangedYear: 30,
        lastCause: 'Marriage',
        originEventKind: 'Marriage',
        lastEventKind: 'Marriage',
        affection: 0.5,
        trust: 0.8,
        obligation: 0,
        fear: 0,
        grievance: 0,
      },
    ],
    titles: [{ kind: 'Ruler', title: 'Consul', civilizationId: 'civ:1', fromYear: 50 }],
  });
  const ctx = context([figure, spouse, child]);

  const said = standingSentence(figure, standingAt(figure, 64, ctx), ctx, 'Orvinowotsi')
    .map((part) => (part.type === 'text' ? part.text : ctx.nameOf(part.id)))
    .join('');

  assert.match(said, /At age 54, Kullerwa was a consul in Orvinowotsi\./);
  assert.doesNotMatch(said, /tradition ran strongest/);
  assert.match(said, /Familiar places|rooted in|closely tied to|remained closely/i);
});

test('parity fixtures match expected semantic signatures', () => {
  const file = JSON.parse(
    readFileSync(
      join(dirname(fileURLToPath(import.meta.url)), '../../../testdata/biography-signatures.json'),
      'utf8',
    ),
  ) as SharedFixtureFile;

  for (const fixture of file.cases) {
    assert.deepEqual(
      biographySignatures(hydrateFixture(fixture.figure), fixture.year),
      fixture.expected,
      fixture.name,
    );
  }
});

test('independence and a settled town use chosen-home rootedness prose', () => {
  const figure = person({
    name: 'Eira',
    disposition: { ...DISPOSITION, tradition: 0.4, independence: 0.72 },
    residences: [{ settlementId: 'set:3', fromYear: 20, reason: 'Settled' }],
  });
  const rooted = buildInterpretations(figure, 60).find((i) => i.theme === 'Rootedness');
  assert.ok(rooted);
  assert.equal(rooted.dials.Independence, 0.55);
  const said = spoken(figure, 60, context([figure]));
  assert.match(said, /made a home|chosen rather than inherited|own terms/);
  assert.doesNotMatch(said, /established ways/);
});

test('a first apparition is discovery evidence and scholarly prose', () => {
  const figure = person({
    name: 'Alda',
    disposition: { ...DISPOSITION, learning: 0.75 },
    observations: [{ cometIndex: 0, year: 30, grade: 'Notable', settlementId: 'set:1' }],
  });
  assert.ok(extractEvidence(figure, 50).some((e) => e.kind === 'Discovery'));
  assert.ok(
    buildInterpretations(figure, 50).some(
      (i) => i.theme === 'ScholarlyLife' && i.evidence.some((e) => e.kind === 'Discovery'),
    ),
  );
  assert.match(spoken(figure, 50, context([figure])), /sighting|sky|apparition/);
});

test('standing sentence can carry three interpretations', () => {
  const friend = person({ id: 'fig:2', name: 'Ragny', birthYear: 8 });
  const rival = person({ id: 'fig:3', name: 'Bera', birthYear: 8 });
  const figure = person({
    disposition: { ...DISPOSITION, tradition: 0.9, aggression: 0.8 },
    residences: [{ settlementId: 'set:1', fromYear: 10, reason: 'Birth' }],
    affinities: [
      {
        id: 1,
        otherId: 'fig:2',
        sought: true,
        startYear: 17,
        origin: 'SharedResidence',
        sourceKind: 'FigureBorn',
        sourceEntityId: 'fig:1',
        placeId: 'set:1',
        stage: 'Friendship',
        outcome: 'Open',
        lastActionYear: 17,
        acts: [
          { year: 17, sourceKind: 'FigureBorn', stage: 'Friendship', actorId: 'fig:1', detail: 'friendship' },
        ],
      },
    ],
    disputes: [
      {
        id: 1,
        otherId: 'fig:3',
        opened: true,
        cause: 'PassedOverForOffice',
        sourceKind: 'OfficeRevoked',
        stage: 'Grudge',
        outcome: 'Open',
        startYear: 30,
        lastActionYear: 30,
        acts: [],
      },
    ],
  });
  const themes = buildInterpretations(figure, 60, 3).map((i) => i.theme);
  assert.equal(themes.length, 3);
  const said = spoken(figure, 60, context([figure, friend, rival]));
  assert.match(said, /rooted in|closely tied|Familiar places|made a home/i);
  assert.match(said, /Ragny|stood by|kept faith|centre of/);
  assert.match(said, /Bera|quarrel|feud|dispute/);
});

test('pass 2 themes fire and render dedicated prose', () => {
  const scholar = person({
    occupation: 'Scribe',
    disposition: { ...DISPOSITION, learning: 0.75 },
  });
  assert.ok(buildInterpretations(scholar, 55).some((i) => i.theme === 'ScholarlyLife'));
  assert.match(spoken(scholar, 55, context([scholar])), /books|Learning ran|studied, copied/);

  const devout = person({
    occupation: 'Clergy',
    disposition: { ...DISPOSITION, piety: 0.8 },
    journeys: [
      {
        kind: 'Pilgrimage',
        year: 30,
        day: 0,
        fromSettlementId: 'set:1',
        toSettlementId: 'set:9',
        durationDays: 20,
        outcome: 'Returned',
        returnYear: 31,
      },
    ],
  });
  assert.ok(buildInterpretations(devout, 50).some((i) => i.theme === 'ReligiousDevotion'));
  assert.match(spoken(devout, 50, context([devout])), /Ilen|pilgrimage|faith/);
  assert.doesNotMatch(spoken(devout, 50, context([devout])), /lived in a way the record can name/);

  const both = person({
    occupation: 'Clergy',
    disposition: { ...DISPOSITION, piety: 0.75, learning: 0.75 },
    observations: [{ cometIndex: 0, year: 30, grade: 'Notable' }],
  });
  const taken: HistoryEvent = {
    id: 2,
    year: 40,
    day: 0,
    kind: 'OccupationTaken',
    significance: 'Routine',
    subject: both.id,
  };
  assert.ok(buildInterpretations(both, 50, 3, [taken]).some((i) => i.theme === 'ReligiousScholarship'));
  assert.match(
    spoken(both, 50, context([both], [taken])),
    /religious office|servant of the faith|read, served, and copied/,
  );

  const ruler = person({
    disposition: { ...DISPOSITION, centralism: 0.75 },
    titles: [{ kind: 'Ruler', title: 'Consul', civilizationId: 'civ:1', fromYear: 20 }],
  });
  assert.ok(buildInterpretations(ruler, 50).some((i) => i.theme === 'ConsolidationOfPower'));
  assert.match(spoken(ruler, 50, context([ruler])), /gathered power|made authority personal|single will/);

  const servant = person({
    disposition: { ...DISPOSITION, centralism: 0.58 },
    titles: [{ kind: 'Governor', title: 'Governor', civilizationId: 'civ:1', fromYear: 20 }],
  });
  assert.ok(buildInterpretations(servant, 50).some((i) => i.theme === 'InstitutionalService'));
  assert.match(spoken(servant, 50, context([servant])), /institutions of the realm|Public office defined|held posts/);

  const rival = person({ id: 'fig:2', name: 'Dera' });
  const passed = person({
    disposition: { ...DISPOSITION, independence: 0.7 },
    disputes: [
      {
        id: 1,
        otherId: 'fig:2',
        opened: true,
        cause: 'PassedOverForOffice',
        sourceKind: 'OfficeRevoked',
        stage: 'Grudge',
        outcome: 'Open',
        startYear: 30,
        lastActionYear: 30,
        acts: [],
      },
    ],
  });
  assert.ok(buildInterpretations(passed, 45).some((i) => i.theme === 'ResistanceToAuthority'));
  assert.match(spoken(passed, 45, context([passed, rival])), /resented the offices|passed over|chafed under/);

  const migrant = person({
    disposition: { ...DISPOSITION, independence: 0.72 },
    residences: [{ settlementId: 'set:3', fromYear: 35, reason: 'Flight' }],
  });
  const moved = buildInterpretations(migrant, 50).map((i) => i.theme);
  assert.ok(moved.includes('FrontierLife'));
  assert.ok(moved.includes('Isolation'));
  assert.match(spoken(migrant, 50, context([migrant])), /edge of the known world|Migration brought|kept apart|Distance and independence|lived at a remove/);

  const captain = person({
    disposition: { ...DISPOSITION, expansionism: 0.75 },
    campaigns: [
      {
        warId: 'war:1',
        battleId: 'bat:1',
        sideId: 'civ:1',
        year: 40,
        role: 'Commanded',
        fate: 'ReturnedUnharmed',
        renownGained: 1,
        traumatized: false,
        deserted: false,
      },
    ],
  });
  assert.ok(buildInterpretations(captain, 50).some((i) => i.theme === 'TerritorialAmbition'));
  assert.match(spoken(captain, 50, context([captain])), /pressed outward|on campaign|Territory and victory/);
});

test('betrayal fires a dedicated theme and names the other party', () => {
  const other = person({ id: 'fig:2', name: 'Hara', birthYear: 8 });
  const betrayer = person({
    disposition: { ...DISPOSITION, tradition: 0.7 },
    betrayals: [
      {
        id: 1,
        betrayerId: 'fig:1',
        betrayedId: 'fig:2',
        otherId: 'fig:2',
        turned: true,
        tie: 'Friendship',
        cause: 'Grievance',
        sourceKind: 'OfficeRevoked',
        year: 40,
      },
    ],
  });
  const themes = buildInterpretations(betrayer, 50);
  assert.ok(themes.some((i) => i.theme === 'Betrayal' && i.outcome === 'Negative'));
  const said = spoken(betrayer, 50, context([betrayer, other]));
  assert.match(said, /Hara/);
  assert.match(said, /turned on|betrayed/);
  assert.doesNotMatch(said, /lived in a way the record can name/);
});

test('recent occupation taken is not long occupation', () => {
  const figure = person({
    occupation: 'Scribe',
    disposition: { ...DISPOSITION, learning: 0.8 },
  });
  assert.ok(extractEvidence(figure, 60).some((e) => e.kind === 'LongOccupation'));

  const taken: HistoryEvent = {
    id: 1,
    year: 50,
    day: 0,
    kind: 'OccupationTaken',
    significance: 'Routine',
    subject: 'fig:1',
  };
  assert.ok(!extractEvidence(figure, 60, [taken]).some((e) => e.kind === 'LongOccupation'));
});

test('standing year uses death when reading a deceased figure', () => {
  const figure = person({ deathYear: 70 });
  assert.equal(standingYear(figure, 200), 70);
});
