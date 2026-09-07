import assert from 'node:assert/strict';
import test from 'node:test';
import {
  biographySignatures,
  buildInterpretations,
  extractEvidence,
  selectInterpretations,
  standingYear,
  type BiographyEvidence,
} from './biographyInterpret.ts';
import { standingAt, standingSentence, type LifeContext } from './biography.ts';
import type { Affinity, Disposition, Figure } from './types.ts';

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

function context(figures: Figure[]): LifeContext {
  const byId = new Map(figures.map((figure) => [figure.id, figure]));
  return {
    endYear: 200,
    figureOf: (id) => byId.get(id),
    eventsFor: () => [],
    nameOf: (id) => byId.get(id)?.name ?? id,
  };
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
    openerId: 'fig:1',
    friendId: 'fig:2',
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
  const rooted = person({
    id: 'fig:100',
    disposition: { ...DISPOSITION, tradition: 0.82 },
    residences: [{ settlementId: 'set:1', fromYear: 10, reason: 'Birth' }],
  });
  const signatures = biographySignatures(rooted, 60);
  assert.equal(signatures.length, 1);
  assert.match(signatures[0], /^Rootedness\|Tradition:0\.6\|Neutral\|LongResidence\|/);

  const loyal = person({
    id: 'fig:101',
    disposition: { ...DISPOSITION, tradition: 0.7 },
    affinities: [
      {
        id: 1,
        openerId: 'fig:101',
        friendId: 'fig:102',
        startYear: 17,
        origin: 'SharedResidence',
        sourceKind: 'FigureBorn',
        sourceEntityId: 'fig:101',
        placeId: 'set:1',
        stage: 'Friendship',
        outcome: 'Open',
        lastActionYear: 17,
        acts: [
          { year: 17, sourceKind: 'FigureBorn', stage: 'Friendship', actorId: 'fig:101', detail: 'friendship' },
        ],
      },
    ],
  });
  const loyalThemes = biographySignatures(loyal, 60).map((line) => line.split('|')[0]);
  assert.ok(loyalThemes.includes('EnduringLoyalty'));
});

test('standing year uses death when reading a deceased figure', () => {
  const figure = person({ deathYear: 70 });
  assert.equal(standingYear(figure, 200), 70);
});
