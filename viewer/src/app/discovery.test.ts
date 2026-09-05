import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import test from 'node:test';
import { historicalSignificance } from './biography.ts';
import { MIN_SCHEMA_VERSION } from './compat.ts';
import { discoverFigures } from './discovery.ts';
import { buildWorld, figureOf, treasuresOwnedBy } from './store.ts';
import type { WorldExport } from './types.ts';

const directory = new URL('../../public/worlds/', import.meta.url);
let files: string[] = [];
try {
  files = readdirSync(directory).filter((name) => name.endsWith('.json'));
} catch (error) {
  if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
}

test('older worlds rank recorded lives without counting events after death', () => {
  const world = buildWorld({
    schemaVersion: MIN_SCHEMA_VERSION,
    meta: { startYear: 1, endYear: 50, yearsSimulated: 50 },
    world: { raster: { resolution: 1, minHeight: 0, maxHeight: 1, height: 'AA==', biome: 'AA==', flags: 'AA==' } },
    figures: [
      { id: 'fig:1', name: 'Earlier life', birthYear: 1, deathYear: 20 },
      { id: 'fig:2', name: 'Ruler', birthYear: 1,
        titles: [{ title: 'Chief', kind: 'Ruler', fromYear: 10 }] },
      { id: 'fig:3', name: 'Unremarked', birthYear: 1 },
    ],
    events: [{ id: 1, year: 30, kind: 'FigureBorn', subject: 'fig:2', object: 'fig:1' }],
    indices: { eventsByEntity: { 'fig:1': [0], 'fig:2': [0] } },
  } as unknown as WorldExport);
  const result = discoverFigures(world);
  assert.equal(result.byId.get('fig:1')!.score, 0);
  assert.ok(result.byId.get('fig:2')!.score > 0);
  assert.deepEqual(result.ranked.map((figure) => figure.id), ['fig:2', 'fig:1', 'fig:3']);
  assert.equal(discoverFigures(world), result);
});

for (const name of files) {
  test(`discovery agrees with every life page and caches indexed work: ${name}`, () => {
    const world = buildWorld(JSON.parse(readFileSync(new URL(name, directory), 'utf8')) as WorldExport);
    const originalOrder = world.export.figures.map((figure) => figure.id);
    const eventsFor = world.eventsFor;
    let reads = 0;
    world.eventsFor = (id) => { reads++; return eventsFor(id); };
    const start = performance.now();
    const discovery = discoverFigures(world);
    const elapsed = performance.now() - start;
    assert.equal(reads, world.export.figures.length);
    assert.equal(discoverFigures(world), discovery);
    assert.equal(reads, world.export.figures.length, 'a second view must reuse the index');
    assert.equal(discovery.byId.size, world.export.figures.length);
    assert.deepEqual(world.export.figures.map((figure) => figure.id), originalOrder);
    let previous = Infinity;
    for (const figure of discovery.ranked) {
      const expected = historicalSignificance(
        figure, eventsFor(figure.id),
        Math.min(figure.deathYear ?? world.export.meta.endYear, world.export.meta.endYear),
        {
          endYear: world.export.meta.endYear,
          figureOf: (id) => figureOf(world, id),
          eventsFor, nameOf: world.nameOf,
        },
        treasuresOwnedBy(world, figure.id).length,
      );
      assert.deepEqual(discovery.byId.get(figure.id), expected);
      assert.ok(expected.score <= previous, 'ranking must descend by score');
      previous = expected.score;
    }
    console.log(`${name}: ${discovery.ranked.length} figures indexed in ${elapsed.toFixed(1)} ms`);
    const other = buildWorld(world.export);
    assert.notEqual(discoverFigures(other), discovery, 'worlds must not share cached readings');
  });
}
