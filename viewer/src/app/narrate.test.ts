import assert from 'node:assert/strict';
import test from 'node:test';
import {
  NARRATION_SYNTAX_VERSION,
  narrateText,
  stitchYears,
  templateFor,
  variantIndex,
} from './narrate.ts';
import type { HistoryEvent } from './types.ts';

test('syntax version matches the engine contract', () => {
  assert.equal(NARRATION_SYNTAX_VERSION, 4);
});

test('nested optionals drop independently', () => {
  const templates = {
    FigureDied: '{subject} died[ as {data:office}][, of {data:cause}].',
  };
  const nameOf = (id: string) => id;
  const base = event({ kind: 'FigureDied', subject: 'fig:2', data: { cause: 'fever' } });

  assert.equal(narrateText(base, templates, nameOf), 'fig:2 died, of fever.');
  assert.equal(
    narrateText({ ...base, data: { office: 'chancellor', cause: 'fever' } }, templates, nameOf),
    'fig:2 died as chancellor, of fever.',
  );
});

test('articles match the next word and do not double the', () => {
  const templates = { Unknown: 'grew into {a}{data:tier}.' };
  const nameOf = (id: string) => id;
  const town = event({ data: { tier: 'town' } });
  const port = event({ data: { tier: 'entrepot' } });

  assert.equal(narrateText(town, templates, nameOf), 'grew into a town.');
  assert.equal(narrateText(port, templates, nameOf), 'grew into an entrepot.');
  assert.equal(
    narrateText(event({ data: { name: 'the Crown of Aeda' } }), { Unknown: '{the}{data:name} was lost.' }, nameOf),
    'The Crown of Aeda was lost.',
  );
});

test('a dropped prefix capitalizes the next word', () => {
  const templates = { FigureBorn: '[{self:subject}{cap}was born].' };
  const nameOf = (id: string) => id;
  const birth = event({ kind: 'FigureBorn', subject: 'fig:1' });

  assert.equal(narrateText(birth, templates, nameOf, 'fig:1'), 'Was born.');
});

test('pronouns follow the named figure\'s sex', () => {
  const templates = { Unknown: '{they:subject} took {them:object}.' };
  const nameOf = (id: string) => id;
  const entry = event({ subject: 'fig:7', object: 'fig:4' });
  const sexOf = (id: string) => (id === 'fig:7' ? 'Female' : 'Male') as const;

  assert.equal(narrateText(entry, templates, nameOf, undefined, sexOf), 'she took him.');
  assert.equal(narrateText(entry, templates, nameOf), 'they took them.');
});

test('numbered variants are forked on the event id', () => {
  const templates = {
    FigureMarried: '{subject} married {object}.',
    'FigureMarried.1': '{subject} and {object} were wed.',
  };
  const nameOf = (id: string) => id;
  const a = event({ id: 0, kind: 'FigureMarried', subject: 'fig:4', object: 'fig:5' });
  const b = event({ id: 1, kind: 'FigureMarried', subject: 'fig:4', object: 'fig:5' });

  assert.equal(variantIndex(0, 2), 0);
  assert.equal(variantIndex(1, 2), 1);
  assert.equal(templateFor(a, templates), templates.FigureMarried);
  assert.equal(templateFor(b, templates), templates['FigureMarried.1']);
  assert.equal(narrateText(a, templates, nameOf), 'fig:4 married fig:5.');
  assert.equal(narrateText(b, templates, nameOf), 'fig:4 and fig:5 were wed.');
});

test('a voice key selects a factual register without a kind switch', () => {
  const templates = {
    RulerCrowned: '{subject} became {data:title} of {object}.',
    'RulerCrowned.elective': '{subject} was chosen as {data:title} of {object}.',
    'RulerCrowned.elective.self': 'Was chosen as {data:title} of {object}.',
  };
  const nameOf = (id: string) => id;
  const crowned = event({
    kind: 'RulerCrowned',
    subject: 'fig:3',
    object: 'civ:1',
    data: { title: 'Queen', voice: 'elective' },
  });

  assert.equal(narrateText(crowned, templates, nameOf), 'fig:3 was chosen as Queen of civ:1.');
  assert.equal(narrateText(crowned, templates, nameOf, 'fig:3'), 'Was chosen as Queen of civ:1.');
});

test('consecutive events of the same year stitch into one group', () => {
  const groups = stitchYears([
    event({ id: 0, year: 40, kind: 'RulerCrowned' }),
    event({ id: 1, year: 40, kind: 'FigureMarried' }),
    event({ id: 2, year: 41, kind: 'WarDeclared' }),
  ]);

  assert.equal(groups.length, 2);
  assert.deepEqual(groups[0].map((entry) => entry.id), [0, 1]);
  assert.equal(groups[1][0].year, 41);
});

function event(overrides: Partial<HistoryEvent> & { kind?: string } = {}): HistoryEvent {
  return {
    id: 0,
    year: 1,
    day: 0,
    kind: 'Unknown',
    significance: 'Notable',
    ...overrides,
  };
}
