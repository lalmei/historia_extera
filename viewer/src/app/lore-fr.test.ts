import assert from 'node:assert/strict';
import test from 'node:test';
import {
  buildLifeArc,
  historicalSignificance,
  knownFor,
  standingSentence,
  type LifeContext,
  type LifeStanding,
} from './biography.ts';
import {
  renderInterpretationParts,
  type BiographyInterpretation,
} from './biographyInterpret.ts';
import { loreLabel } from './i18n/lore.ts';
import type { Figure } from './types.ts';

function person(overrides: Partial<Figure> = {}): Figure {
  return {
    id: 'fig:1',
    name: 'Áldren',
    sex: 'Female',
    civilizationId: 'civ:1',
    cultureId: 'cul:1',
    birthYear: 10,
    deathCause: 'Unknown',
    origin: 'Unrecorded',
    residences: [],
    grades: [],
    titles: [],
    service: [],
    campaigns: [],
    journeys: [],
    bonds: [],
    memories: [],
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

function context(language?: 'fr' | 'en'): LifeContext {
  return {
    language,
    endYear: 100,
    nameOf: (id) => id === 'fig:2' ? 'Éléna' : id === 'set:1' ? 'Ville Épine' : id,
    figureOf: () => undefined,
    eventsFor: () => [],
  };
}

const standing: LifeStanding = {
  year: 100,
  age: 60,
  alive: false,
  position: 'King',
  household: 'married',
  childCount: 2,
  closest: { id: 'fig:2', reading: 'deeply trusted' },
  activeMemories: 0,
};

function plain(parts: ReturnType<typeof standingSentence>, ctx: LifeContext): string {
  return parts.map((part) => part.type === 'text' ? part.text : ctx.nameOf(part.id)).join('');
}

test('French biography keeps proper names, entity links and dated facts', () => {
  const figure = person({ deathYear: 70 });
  const ctx = context('fr');
  const parts = standingSentence(figure, standing, ctx, 'Ville Épine');
  const text = plain(parts, ctx);
  assert.match(text, /À 60 ans, Áldren/);
  assert.match(text, /Ville Épine/);
  assert.match(text, /mariée/);
  assert.match(text, /Éléna/);
  assert.match(text, /morte en 70/);
  assert.match(text, /30 ans/);
  assert.deepEqual(parts.filter((part) => part.type === 'entity'), [{ type: 'entity', id: 'fig:2' }]);
});

test('omitted language preserves the original English summary', () => {
  const figure = person({ deathYear: 70 });
  const original = standingSentence(figure, standing, context(), 'Ville Épine');
  const english = standingSentence(figure, standing, context('en'), 'Ville Épine');
  assert.deepEqual(original, english);
  assert.match(plain(english, context('en')), /At age 60, Áldren was a king/);
});

test('language changes life-arc labels but not dates, kinds or ranking', () => {
  const figure = person({ deathYear: 70 });
  const english = buildLifeArc(figure, [], context('en'));
  const french = buildLifeArc(figure, [], context('fr'));
  const facts = (arc: typeof english) => arc.moments.map(({ key, year, age, kind, score }) => ({ key, year, age, kind, score }));
  assert.deepEqual(facts(english), facts(french));
  assert.deepEqual(english.density, french.density);
  assert.equal(english.moments[0].label, 'Born');
  assert.equal(french.moments[0].label, 'Naissance');
});

test('French interpretation preserves linked figures and duration', () => {
  const figure = person();
  const interpretation: BiographyInterpretation = {
    theme: 'EnduringLoyalty',
    dials: {},
    score: 1,
    outcome: 'Neutral',
    evidence: [{ kind: 'Friendship', strength: 0.8, startYear: 20, endYear: 40, relatedFigureId: 'fig:2' }],
  };
  for (const variant of [0, 1, 2]) {
    const english = renderInterpretationParts(figure, interpretation, context('en'), variant);
    const french = renderInterpretationParts(figure, interpretation, context('fr'), variant);
    assert.deepEqual(french.filter((part) => part.type === 'entity'), english.filter((part) => part.type === 'entity'));
    assert.match(plain(french, context('fr')), /20 ans/);
    assert.match(plain(french, context('fr')), /Éléna/);
  }
});

test('language keeps known-for keys and significance score unchanged', () => {
  const figure = person({
    titles: [{ kind: 'Ruler', title: 'King', civilizationId: 'civ:1', fromYear: 30 }],
  });
  assert.deepEqual(
    knownFor(figure, [], 80, context('fr')).map((line) => line.key),
    knownFor(figure, [], 80, context('en')).map((line) => line.key),
  );
  const en = historicalSignificance(figure, [], 80, context('en'));
  const fr = historicalSignificance(figure, [], 80, context('fr'));
  assert.equal(fr.score, en.score);
  assert.equal(fr.band, en.band);
});

test('display labels never replace unknown proper names', () => {
  assert.equal(loreLabel('fr', 'King'), 'Roi');
  assert.equal(loreLabel('en', 'King'), 'King');
  assert.equal(loreLabel('fr', 'Ville Épine'), 'Ville Épine');
});
