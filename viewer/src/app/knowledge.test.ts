import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import test from 'node:test';
import { MIN_SCHEMA_VERSION } from './compat.ts';
import {
  claimKey,
  claimsAboutTheSame,
  columnOfRegister,
  parseClaimKey,
  readKnowledge,
  type ClaimRecord,
} from './knowledge.ts';
import { buildWorld } from './store.ts';
import type { Claim, WorldExport } from './types.ts';

const directory = new URL('../../public/worlds/', import.meta.url);
let files: string[] = [];
try {
  files = readdirSync(directory).filter((name) => name.endsWith('.json'));
} catch (error) {
  if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
}

const RASTER = {
  resolution: 1,
  minHeight: 0,
  maxHeight: 1,
  height: 'AA==',
  biome: 'AA==',
  flags: 'AA==',
};

function claim(overrides: Partial<Claim> & { id: number }): Claim {
  return {
    subject: 'CometPeriod',
    subjectIndex: 0,
    cometIndex: 0,
    year: 10,
    register: 'Measured',
    reading: 'that it returns every 30 years',
    restsOnYears: [5, 10],
    intervalYears: 30,
    verdict: 'Standing',
    claimantSawTheAnswer: false,
    ...overrides,
  };
}

/**
 * A minimal export shaped around one question.
 *
 * Untyped on purpose, the way the other viewer tests build fixtures: a `Figure` carries two dozen
 * fields none of these cases turn on, and spelling them out would bury what each test is about.
 */
function worldWith(input: Record<string, unknown>) {
  return buildWorld({
    schemaVersion: 53,
    meta: { startYear: 1, endYear: 200, yearsSimulated: 200 },
    world: { raster: RASTER },
    figures: [],
    civilizations: [],
    artifacts: [],
    events: [],
    indices: { eventsByEntity: {} },
    ...input,
  } as unknown as WorldExport);
}

test('a reading is classified by what could settle it, never by whether it was right', () => {
  const measured = claim({ id: 0, register: 'Measured', verdict: 'Refuted' });
  const mythic = claim({ id: 1, register: 'Mythic', verdict: 'NotTestable', quantity: undefined });

  assert.equal(columnOfRegister(measured), 'Observation');
  assert.equal(columnOfRegister(mythic), 'Doctrine');

  // The same register with every verdict the engine can write must land in the same column, or
  // the matrix has become a scoreboard.
  for (const verdict of ['Standing', 'Confirmed', 'Refuted', 'Untested', 'NotTestable'] as const) {
    assert.equal(columnOfRegister({ ...measured, verdict }), 'Observation');
    assert.equal(columnOfRegister({ ...mythic, verdict }), 'Doctrine');
  }
});

test('holdings fold out of transitions, and a re-acquisition is a second span', () => {
  const world = worldWith({
    figures: [
      {
        id: 'fig:1',
        name: 'Brania',
        birthYear: 1,
        deathYear: 60,
        claims: [claim({ id: 0, year: 10, realmId: 'civ:0' })],
      },
    ],
    civilizations: [
      { id: 'civ:0', name: 'Aral' },
      { id: 'civ:1', name: 'Beriv' },
    ],
    claimTransitions: [
      { claimantId: 'fig:1', claimId: 0, realmId: 'civ:0', year: 10, kind: 'Acquired', carrier: 'Claimant', carrierId: 'fig:1' },
      { claimantId: 'fig:1', claimId: 0, realmId: 'civ:1', year: 30, kind: 'Acquired', carrier: 'Text', carrierId: 'art:1' },
      { claimantId: 'fig:1', claimId: 0, realmId: 'civ:0', year: 60, kind: 'Lost', carrier: 'Claimant', carrierId: 'fig:1' },
      { claimantId: 'fig:1', claimId: 0, realmId: 'civ:0', year: 90, kind: 'Acquired', carrier: 'Text', carrierId: 'art:1' },
    ],
  });

  const record = readKnowledge(world).byKey.get('fig:1/0') as ClaimRecord;
  assert.deepEqual(
    record.holdings.map((holding) => [holding.realmId, holding.fromYear, holding.toYear]),
    [
      ['civ:0', 10, 60],
      ['civ:1', 30, undefined],
      ['civ:0', 90, undefined],
    ],
  );
  assert.deepEqual(record.everHeld, ['civ:0', 'civ:1']);
  assert.deepEqual(record.heldNow, ['civ:1', 'civ:0']);
  assert.equal(record.wasLost, true);
});

test('a standing belongs to the span it happened inside, and a later span starts received', () => {
  const world = worldWith({
    schemaVersion: 55,
    figures: [
      {
        id: 'fig:1',
        name: 'Brania',
        birthYear: 1,
        deathYear: 60,
        claims: [claim({ id: 0, year: 10, realmId: 'civ:0', verdict: 'Refuted', settledYear: 45 })],
      },
    ],
    civilizations: [{ id: 'civ:0', name: 'Aral' }],
    claimTransitions: [
      { claimantId: 'fig:1', claimId: 0, realmId: 'civ:0', year: 10, kind: 'Acquired', carrier: 'Claimant', carrierId: 'fig:1' },
      { claimantId: 'fig:1', claimId: 0, realmId: 'civ:0', year: 60, kind: 'Lost', carrier: 'Claimant', carrierId: 'fig:1' },
      { claimantId: 'fig:1', claimId: 0, realmId: 'civ:0', year: 90, kind: 'Acquired', carrier: 'Text', carrierId: 'art:1' },
    ],
    claimStandings: [
      { claimantId: 'fig:1', claimId: 0, realmId: 'civ:0', year: 20, from: 'Received', to: 'Taught', cause: 'taken up by the realm\u2019s learned' },
      { claimantId: 'fig:1', claimId: 0, realmId: 'civ:0', year: 55, from: 'Taught', to: 'SetAside', cause: 'set aside where the faith already explains it' },
    ],
  });

  const index = readKnowledge(world);
  const record = index.byKey.get('fig:1/0') as ClaimRecord;

  assert.equal(index.hasStandings, true);
  assert.deepEqual(
    record.holdings.map((holding) => [holding.fromYear, holding.standing, holding.standings.length]),
    [
      [10, 'SetAside', 2],
      // Coming by it again is a fresh arrival, and arrival is not adoption.
      [90, 'Received', 0],
    ],
  );

  // The sky had already answered by 55, and nothing about that decided either standing.
  assert.equal(record.claim.verdict, 'Refuted');
});

test('a loss with no open span is kept as an anomaly rather than dropped', () => {
  const world = worldWith({
    figures: [{ id: 'fig:1', name: 'Sergin', birthYear: 1, claims: [claim({ id: 0 })] }],
    civilizations: [{ id: 'civ:0', name: 'Aral' }],
    claimTransitions: [
      { claimantId: 'fig:1', claimId: 0, realmId: 'civ:0', year: 40, kind: 'Lost', carrier: 'Text' },
    ],
  });

  const record = readKnowledge(world).byKey.get('fig:1/0') as ClaimRecord;
  assert.equal(record.holdings.length, 1);
  assert.deepEqual(
    [record.holdings[0].fromYear, record.holdings[0].toYear],
    [40, 40],
    'an unexplained loss must stay visible',
  );
  assert.equal(record.wasLost, true);
});

test('the matrix shows only the columns the export populates, and doctrine is one of them', () => {
  const world = worldWith({
    figures: [
      { id: 'fig:1', name: 'Brania', birthYear: 1, claims: [claim({ id: 0, verdict: 'Confirmed', settledYear: 40 })] },
      {
        id: 'fig:2',
        name: 'Gran',
        birthYear: 1,
        observations: [{ cometIndex: 0, year: 5, grade: 'Great' }],
        claims: [claim({ id: 0, register: 'Mythic', verdict: 'NotTestable', reading: 'that it is an old light on an errand of its own' })],
      },
    ],
  });

  const index = readKnowledge(world);
  assert.deepEqual(index.columns, ['Observation', 'Doctrine']);
  assert.deepEqual(index.domains, ['Astronomy']);
  assert.equal(index.cellAt('Astronomy', 'Practice'), undefined);
  assert.equal(index.cellAt('Astronomy', 'Observation')!.claims.length, 1);
  assert.equal(index.cellAt('Astronomy', 'Doctrine')!.claims.length, 1);
  // Sightings are evidence for what the sky can answer; attaching them to doctrine would imply a
  // mythic reading rests on measurement it never claimed.
  assert.equal(index.cellAt('Astronomy', 'Observation')!.observations, 1);
  assert.equal(index.cellAt('Astronomy', 'Doctrine')!.observations, 0);
});

test('claims about one subject gather across realms, so disagreement is visible', () => {
  const world = worldWith({
    figures: [
      { id: 'fig:1', name: 'Brania', birthYear: 1, claims: [claim({ id: 0, realmId: 'civ:0', subjectIndex: 4, cometIndex: 4 })] },
      {
        id: 'fig:2',
        name: 'Gran',
        birthYear: 1,
        claims: [
          claim({ id: 0, realmId: 'civ:1', subjectIndex: 4, cometIndex: 4, register: 'Mythic', reading: 'that it is a herald' }),
          claim({ id: 1, subjectIndex: 2, cometIndex: 2 }),
        ],
      },
    ],
    civilizations: [
      { id: 'civ:0', name: 'Aral' },
      { id: 'civ:1', name: 'Beriv' },
    ],
  });

  const index = readKnowledge(world);
  const record = index.byKey.get('fig:1/0')!;
  const together = claimsAboutTheSame(index, record);
  assert.deepEqual(together.map((other) => other.key).sort(), ['fig:1/0', 'fig:2/0']);
});

test('a world written before the transitions still reads, with no holdings invented', () => {
  const world = buildWorld({
    schemaVersion: MIN_SCHEMA_VERSION,
    meta: { startYear: 1, endYear: 100, yearsSimulated: 100 },
    world: { raster: RASTER },
    figures: [{ id: 'fig:1', name: 'Sergin', birthYear: 1, claims: [claim({ id: 0 })] }],
    events: [],
    indices: { eventsByEntity: {} },
  } as unknown as WorldExport);

  const index = readKnowledge(world);
  assert.equal(index.hasTransitions, false);
  assert.equal(index.claims.length, 1);
  assert.deepEqual(index.claims[0].holdings, []);
  assert.deepEqual(index.claims[0].everHeld, []);
  assert.equal(index.claims[0].wasLost, false);
  assert.deepEqual(index.realmsHolding, []);
});

test('claim keys round-trip, and an id that names nothing resolves to nothing', () => {
  assert.equal(claimKey('fig:203', 0), 'fig:203/0');
  assert.deepEqual(parseClaimKey('fig:203/0'), { claimantId: 'fig:203', claimId: 0 });
  assert.equal(parseClaimKey('fig:203'), undefined);
  assert.equal(parseClaimKey('fig:203/x'), undefined);
});

for (const name of files) {
  test(`the knowledge index agrees with the export it was read from: ${name}`, () => {
    const world = buildWorld(
      JSON.parse(readFileSync(new URL(name, directory), 'utf8')) as WorldExport,
    );
    const index = readKnowledge(world);

    const exported = world.export.figures.flatMap((figure) =>
      (figure.claims ?? []).map((entry) => claimKey(figure.id, entry.id)),
    );
    assert.equal(index.claims.length, exported.length);
    assert.deepEqual([...index.byKey.keys()].sort(), [...exported].sort());
    assert.equal(readKnowledge(world), index, 'a second reading must reuse the index');

    let placed = 0;
    for (const cell of index.cells) placed += cell.claims.length;
    assert.equal(placed, index.claims.length, 'every claim belongs to exactly one cell');

    // Every transition in the export must land on the claim it names — an orphan would be a
    // holding the section silently drops.
    const folded = index.claims.reduce((sum, record) => sum + record.transitions.length, 0);
    assert.equal(folded, (world.export.claimTransitions ?? []).length);

    for (const record of index.claims) {
      let previous = -Infinity;
      for (const transition of record.transitions) {
        assert.ok(transition.year >= previous, 'transitions must read in the order they happened');
        previous = transition.year;
      }
      for (const holding of record.holdings) {
        assert.ok(holding.toYear === undefined || holding.toYear >= holding.fromYear);
      }
      assert.equal(
        record.column,
        record.claim.register === 'Measured' ? 'Observation' : 'Doctrine',
      );
      for (const text of record.texts) {
        assert.ok(
          (text.tomeContents?.carries ?? []).some(
            (ref) => claimKey(ref.claimantId, ref.claimId) === record.key,
          ),
        );
      }
    }

    let previousYear = -Infinity;
    for (const record of index.claims) {
      assert.ok(record.claim.year >= previousYear, 'readings must read chronologically');
      previousYear = record.claim.year;
    }
  });
}
