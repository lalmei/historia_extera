import assert from 'node:assert/strict';
import test from 'node:test';
import { readHistoriography } from './historiography.ts';
import { buildWorld } from './store.ts';
import type { Artifact, WorldExport } from './types.ts';

function book(id = 'art:0', createdYear = 20, extra: Record<string, unknown> = {}) {
  return { id, originSettlementId: 'set:1', kind: 'Tome', name: id, createdYear, provenance: [], tomeContents: {
    kind: 'Campaign', subjectId: 'fig:0', contextId: 'war:0',
    sections: [
      { heading: 'Service', year: createdYear, text: 'An aggregate.', references: ['bat:0', 'bat:1'] },
      { heading: 'Recorded engagements', year: createdYear, text: 'The first battle.', references: ['bat:0'] },
      { heading: 'Aftermath', year: createdYear, text: 'Unsettled.', references: ['war:0'] },
    ], copies: [{ year: 80, settlementId: 'set:2', sourceSettlementId: 'set:1' }], ...extra,
  } } as Artifact;
}
function fixture(artifacts: Artifact[]) {
  return buildWorld({ schemaVersion: 63, meta: { startYear: 1, endYear: 100 },
    world: { raster: { resolution: 1, minHeight: 0, maxHeight: 1, height: 'AA==', biome: 'AA==', flags: 'AA==' } },
    artifacts,
    wars: [{ id: 'war:0', startYear: 5, endYear: 40, battleIds: ['bat:0', 'bat:1', 'bat:2'] },
      { id: 'war:1', startYear: 5, endYear: 40, battleIds: [] }],
    battles: [
      { id: 'bat:0', warId: 'war:0', year: 10, attackerCommanderId: 'fig:0' },
      { id: 'bat:1', warId: 'war:0', year: 15, defenderCommanderId: 'fig:0' },
      { id: 'bat:2', warId: 'war:0', year: 30, attackerCommanderId: 'fig:0' },
      { id: 'bat:3', warId: 'war:0', year: 12, attackerCommanderId: 'fig:9' },
    ],
    events: [{ id: 0, year: 30, kind: 'BattleFought', subject: 'bat:2', object: 'war:0' },
      { id: 1, year: 40, kind: 'WarEnded', subject: 'war:0' }],
  } as unknown as WorldExport);
}

test('the frozen account separates individual coverage, aggregate-only omissions and the later war', () => {
  const world = fixture([book()]);
  const before = JSON.stringify(world.export);
  const account = readHistoriography(world)[0];
  const original = account.versions[0];
  assert.deepEqual(original.described.map((x) => x.id), ['bat:0']);
  assert.deepEqual(original.omitted.map((x) => x.id), ['bat:1']);
  assert.deepEqual(original.summarized.map((x) => x.id), ['bat:1']);
  assert.deepEqual(original.later.map((x) => x.id), [0, 1]);
  assert.equal(original.laterEnding, 40);
  assert.equal(original.year, 20); // The copy made in 80 does not update authorship.
  assert.equal(JSON.stringify(world.export), before);
  assert.equal(readHistoriography(world), readHistoriography(world));
});

test('a continuation adds coverage without rewriting the original comparison', () => {
  const work = book();
  work.tomeContents!.sections.push({ heading: 'Continuation, 35', year: 35, text: 'Later battles.', references: ['bat:1', 'bat:2'] });
  const versions = readHistoriography(fixture([work]))[0].versions;
  assert.equal(versions.length, 2);
  assert.equal(versions[0].continuation, false);
  assert.deepEqual(versions[0].omitted.map((x) => x.id), ['bat:1']);
  assert.equal(versions[1].continuation, true);
  assert.equal(versions[1].sections.length, 1);
  assert.equal(versions[1].described.length, 3);
  assert.equal(versions[1].omitted.length, 0);
  assert.deepEqual(versions[1].later.map((x) => x.id), [1]);
});

test('later rival accounts need the same commander and war and genuinely additional coverage', () => {
  const earlier = book();
  const fuller = book('art:1', 50);
  const otherCommander = book('art:2', 50, { subjectId: 'fig:9' });
  const otherWar = book('art:3', 50, { contextId: 'war:1' });
  const sameCoverage = book('art:4', 21);
  const lost = book('art:5', 50, { copies: [] });
  lost.lostYear = 60;
  const accounts = readHistoriography(fixture([earlier, fuller, otherCommander, otherWar, sameCoverage, lost]));
  assert.deepEqual(accounts[0].laterRivals.map((x) => x.id), ['art:1']);
});

test('a lost original with a surviving copy is still a surviving account', () => {
  const work = book();
  work.lostYear = 50;
  assert.equal(readHistoriography(fixture([work]))[0].survives, true);
  work.tomeContents!.copies![0].lostYear = 90;
  assert.equal(readHistoriography(fixture([work]))[0].survives, false);
});

test('undated original sections use creation year; unsupported tome kinds get no invented comparison', () => {
  const work = book();
  for (const section of work.tomeContents!.sections) delete section.year;
  const annals = book('art:1', 20, { kind: 'Annals' });
  const accounts = readHistoriography(fixture([work, annals]));
  assert.equal(accounts.length, 1);
  assert.equal(accounts[0].versions[0].year, 20);
});
