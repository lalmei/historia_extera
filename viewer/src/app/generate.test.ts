import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { paramsFromExport, paramsFromSearch } from './generate.ts';
import { normalizeExport } from './compat.ts';
import type { WorldExport } from './types.ts';
import { rerunSettings } from './rerun.mjs';
import { readWorldHeader } from '../../dev/world-generator.mjs';

function fixture(): WorldExport {
  return {
    schemaVersion: 62,
    meta: { seed: 42, yearsSimulated: 300, startYear: 1, generation: {
      version: 1, initialCivilizations: 3, terrainSource: '',
    } },
    world: { width: 4096, height: 4096, eastWestPeriodic: false },
  } as WorldExport;
}

test('renamed imports retain the recorded civilization count, ignoring misleading filenames', () => {
  const data = fixture();
  for (const name of ['renamed.json', 'world-s1-y2-c64-z8192.json', undefined]) {
    assert.deepEqual(paramsFromExport(data, name), {
      seed: 42, years: 300, civs: 3, size: 4096, eastWestPeriodic: false,
    });
  }
});

test('filename-only bookmarks cannot silently substitute defaults', () => {
  const params = paramsFromSearch('from=world-s42-y300-c3-z4096.json')!;
  assert.ok(Number.isNaN(params.seed));
  assert.ok(Number.isNaN(params.civs));
});

test('old recipes, custom settings and external terrain cannot become default reruns', () => {
  for (const recipe of [undefined, { version: 2 },
    { version: 1, initialCivilizations: 3, terrainSource: 'raster:abc' },
    { version: 1, initialCivilizations: 3, terrainSource: '', unsupportedReason: 'Use original custom configuration.' },
  ]) {
    const data = fixture();
    data.meta.generation = recipe as any;
    assert.equal(paramsFromExport(data, 'world-s42-y300-c3-z4096.json'), null);
    assert.ok(rerunSettings(data.meta, data.world).reason);
  }
});

test('unsafe ulong seeds are rejected on loading and rerunning, never replaced by defaults', () => {
  for (const raw of ['9007199254740992', '9007199254740993', '18446744073709551615']) {
    const data = fixture();
    data.meta.seed = JSON.parse(raw);
    assert.throws(() => normalizeExport(data), /safe integer range/);
    assert.equal(paramsFromExport(data), null);
    assert.ok(Number.isNaN(paramsFromSearch(`seed=${raw}`)!.seed));
    assert.ok(Number.isNaN(paramsFromSearch(`from=world-s${raw}-y300-c3.json`)!.seed));
  }
  const data = fixture();
  data.meta.seed = Number.MAX_SAFE_INTEGER;
  assert.equal(paramsFromExport(data)!.seed, Number.MAX_SAFE_INTEGER);
});

test('catalog reads the same recipe for compact and pretty renamed files and refuses legacy guesses', async () => {
  const folder = await mkdtemp(path.join(tmpdir(), 'historia-recipe-'));
  try {
    const file = path.join(folder, 'renamed.json');
    for (const spacing of [undefined, 2]) {
      const data = fixture();
      await writeFile(file, JSON.stringify(data, null, spacing));
      assert.deepEqual((await readWorldHeader(file)).rerun.params, paramsFromExport(data));
      delete data.meta.generation;
      await writeFile(file, JSON.stringify(data, null, spacing));
      assert.equal((await readWorldHeader(file)).rerun.params, null);
      data.meta.seed = Number.MAX_SAFE_INTEGER + 1;
      await writeFile(file, JSON.stringify(data, null, spacing));
      assert.match((await readWorldHeader(file)).rerun.reason!, /safe integer range/);
    }
  } finally { await rm(folder, { recursive: true, force: true }); }
});
