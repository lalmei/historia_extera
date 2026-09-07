import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { gunzipSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { ADDED_IN, MIN_SCHEMA_VERSION, normalizeExport, schemaVerdict } from './compat.ts';
import { loadWorld } from './store.ts';
import { narrate, narrateText } from './narrate.ts';
import { buildGrid, buildRealms } from './territory.ts';
import {
  fortifiedFromYear,
  housesOnMap,
  landmarkMarks,
  wallsStanding,
  waterMarks,
} from './mapLayers.ts';
import {
  buildBiographyEpisodes,
  buildConstellation,
  buildLifeArc,
  groupJourneys,
  historicalSignificance,
  knownFor,
  lifeVantages,
  ripplesAfter,
  standingAt,
  standingSentence,
  type LifeContext,
} from './biography.ts';
import { SCHEMA_VERSION, type WorldExport } from './types.ts';

/** Historical engine output is retained separately from the mutable world library. */
const FIXTURES = fileURLToPath(new URL('../../test-fixtures/compat/', import.meta.url));
const REQUIRED_VERSIONS = [28, 34, 42, 48, 52];
const manifest = JSON.parse(readFileSync(path.join(FIXTURES, 'manifest.json'), 'utf8')) as {
  file: string; schemaVersion: number; sourceCommit: string; sha256: string;
  figures: number; events: number;
}[];
const fixtures = manifest.map((entry) => {
  // A missing or changed file is a failure, never a reason to run fewer tests.
  const bytes = gunzipSync(readFileSync(path.join(FIXTURES, entry.file)));
  assert.equal(createHash('sha256').update(bytes).digest('hex'), entry.sha256, entry.file);
  const data = JSON.parse(bytes.toString('utf8')) as WorldExport;
  assert.equal(data.schemaVersion, entry.schemaVersion, entry.file);
  assert.equal(data.figures.length, entry.figures, entry.file);
  assert.equal(data.events.length, entry.events, entry.file);
  assert.ok(entry.figures > 0 && entry.events > 0, 'fixtures must exercise real histories');
  assert.match(entry.sourceCommit, /^[0-9a-f]{40}$/);
  return { name: entry.file, data };
});

test('required historical boundaries and the supported floor have retained exports', () => {
  assert.deepEqual(manifest.map((entry) => entry.schemaVersion), REQUIRED_VERSIONS);
  assert.equal(REQUIRED_VERSIONS[0], MIN_SCHEMA_VERSION);
});

const WORLDS = fileURLToPath(new URL('../../public/worlds', import.meta.url));

function savedWorlds(): { name: string; data: WorldExport }[] {
  let names: string[];
  try {
    names = readdirSync(WORLDS).filter((name) => name.endsWith('.json'));
  } catch {
    return [];
  }

  return names.map((name) => ({
    name,
    data: JSON.parse(readFileSync(path.join(WORLDS, name), 'utf8')) as WorldExport,
  }));
}

test('historical fixtures contain the additions and omissions they cover', () => {
  for (const { data } of fixtures) {
    const fields = data.figures as unknown as Record<string, unknown>[];
    for (const [field, since] of [['journeys', 28], ['bonds', 34], ['residences', 42], ['affinities', 48], ['claims', 42]] as const) {
      if (data.schemaVersion >= since) {
        assert.ok(fields.some((figure) => Array.isArray(figure[field]) && figure[field].length > 0),
          `v${data.schemaVersion} must exercise ${field}`);
      } else {
        assert.ok(fields.every((figure) => !(field in figure)),
          `v${data.schemaVersion} must retain the original absence of ${field}`);
      }
    }
    const claims = data.figures.flatMap((figure) => figure.claims ?? []);
    if (data.schemaVersion >= 52) {
      assert.ok(claims.some((claim) => claim.subject && claim.quantity));
    } else {
      assert.ok(claims.every((claim) => !('subject' in claim) && !('quantity' in claim)));
    }
  }
});

/** Three sample years: the first, the middle and the last the run reached. */
function probeYears(data: WorldExport): number[] {
  const end = data.meta.yearsSimulated;
  return [...new Set([1, Math.max(1, Math.floor(end / 2)), end])];
}

/**
 * The engine's own constant, read from source rather than copied here.
 *
 * Every other assertion in this file measures `SCHEMA_VERSION` against itself, which is why a
 * viewer pinned a version behind the engine and no test noticed: `schemaVerdict(SCHEMA_VERSION + 1)`
 * is `too-new` whatever number the constant holds. This is the one assertion that reaches across
 * to the other side of the contract, so a schema bump that forgets the viewer fails here instead of
 * on a reader's screen.
 */
function engineSchemaVersion(): number {
  const source = readFileSync(
    fileURLToPath(new URL('../../../src/HistoryEngine/Serialization/WorldExport.cs', import.meta.url)),
    'utf8',
  );
  const match = /CurrentSchemaVersion\s*=\s*(\d+)/.exec(source);
  assert.ok(match, 'WorldExport.cs should declare CurrentSchemaVersion');
  return Number(match[1]);
}

test('the viewer pins the schema the engine actually writes', () => {
  const engine = engineSchemaVersion();
  assert.equal(
    SCHEMA_VERSION,
    engine,
    `the engine writes schema v${engine} and the viewer reads up to v${SCHEMA_VERSION}; ` +
      'bump SCHEMA_VERSION in types.ts and add what it gained to ADDED_IN in compat.ts',
  );
});

test('every version the viewer accepts above the floor says what it added', () => {
  // ADDED_IN is what an older export is told it is missing. A version that changed nothing a
  // reader can see is legitimately absent, but the newest one never is: it is the reason for
  // the bump.
  assert.ok(
    ADDED_IN.some((entry) => entry.since === SCHEMA_VERSION),
    `schema v${SCHEMA_VERSION} should say what it added in ADDED_IN`,
  );
  assert.ok(
    ADDED_IN.every((entry) => entry.since <= SCHEMA_VERSION),
    'ADDED_IN should not describe a schema the viewer refuses to open',
  );
});

test('the schema range is a range, and its edges are refused', () => {
  assert.equal(schemaVerdict(SCHEMA_VERSION).state, 'current');
  assert.equal(schemaVerdict(SCHEMA_VERSION).readable, true);

  assert.equal(schemaVerdict(MIN_SCHEMA_VERSION).state, 'older');
  assert.equal(schemaVerdict(MIN_SCHEMA_VERSION).readable, true);

  assert.equal(schemaVerdict(MIN_SCHEMA_VERSION - 1).state, 'too-old');
  assert.equal(schemaVerdict(MIN_SCHEMA_VERSION - 1).readable, false);

  assert.equal(schemaVerdict(SCHEMA_VERSION + 1).state, 'too-new');
  assert.equal(schemaVerdict(SCHEMA_VERSION + 1).readable, false);

  assert.equal(schemaVerdict(null).state, 'unreadable');
  assert.equal(schemaVerdict(undefined).readable, false);
});

test('an older export is told what it predates, the current one is told nothing', () => {
  assert.deepEqual(schemaVerdict(SCHEMA_VERSION).missing, []);

  const old = schemaVerdict(MIN_SCHEMA_VERSION);
  assert.ok(old.missing.length > 0, 'the oldest supported export predates later additions');
  assert.ok(old.summary.includes(`v${MIN_SCHEMA_VERSION}`));

  // Each step forward can only ever drop entries from the list, never add them.
  for (let version = MIN_SCHEMA_VERSION; version < SCHEMA_VERSION; version++) {
    assert.ok(
      schemaVerdict(version).missing.length >= schemaVerdict(version + 1).missing.length,
      `v${version} should be missing at least as much as v${version + 1}`,
    );
  }
});

test('normalizing fills containers and invents nothing', () => {
  const bare = {
    schemaVersion: MIN_SCHEMA_VERSION,
    meta: { yearsSimulated: 1 },
    world: {},
    figures: [{ id: 'fig:1', name: 'Someone' }],
  } as unknown as WorldExport;

  const filled = normalizeExport(bare);

  assert.deepEqual(filled.events, []);
  assert.deepEqual(filled.civilizations, []);
  assert.deepEqual(filled.narration, {});
  assert.deepEqual(filled.indices.eventsByEntity, {});
  assert.deepEqual(filled.figures[0].journeys, []);
  assert.deepEqual(filled.figures[0].spouseIds, []);

  // The values a later schema records are absent, not defaulted: an older world must not
  // claim a disposition, an occupation or a dedication the engine never wrote down.
  assert.equal(filled.figures[0].disposition, undefined);
  assert.equal(filled.figures[0].occupation, undefined);
  assert.equal(filled.figures[0].feelings, undefined);
});

test('normalizing leaves a well-formed export exactly as it was', () => {
  const already = {
    schemaVersion: SCHEMA_VERSION,
    meta: { yearsSimulated: 2, systemOrder: ['crown'] },
    world: { rivers: [{ id: 1 }] },
    figures: [{ id: 'fig:1', spouseIds: ['fig:2'], journeys: [{ year: 1 }] }],
    events: [{ id: 0 }],
  } as unknown as WorldExport;

  const filled = normalizeExport(already);

  assert.deepEqual(filled.meta.systemOrder, ['crown']);
  assert.equal(filled.world.rivers.length, 1);
  assert.deepEqual(filled.figures[0].spouseIds, ['fig:2']);
  assert.equal(filled.figures[0].journeys.length, 1);
  assert.equal(filled.events.length, 1);
});

const saved = process.env.HISTORIA_TEST_SAVED_WORLDS === '1' ? savedWorlds() : [];
const retained = [...fixtures, ...saved];

test('optional saved exports are inside the supported range', { skip: saved.length === 0 }, () => {

  for (const { name, data } of saved) {
    const verdict = schemaVerdict(data.schemaVersion);
    assert.ok(
      verdict.readable,
      `${name} is schema v${data.schemaVersion}, which the viewer refuses: ${verdict.summary}`,
    );
  }
});

/**
 * Every required historical world loads, narrates, replays, maps and derives biographies.
 *
 * One case per file rather than one loop, so a failure names the export and the schema that
 * broke rather than stopping at the first old world in the directory.
 */
for (const { name, data } of retained) {
  test(`v${data.schemaVersion} ${name} loads and renders`, async () => {
    // Exercise the actual fetch/parse/version-check entry point without a server or network.
    const world = await loadWorld(`data:application/json;base64,${Buffer.from(JSON.stringify(data)).toString('base64')}`);
    assert.equal(world.schema.readable, true);
    assert.equal(world.schema.version, data.schemaVersion);

    for (const event of world.export.events) {
      narrate(event, world.export.narration, (id) => world.nameOf(id));
    }

    const finalOwners = world.timeline.ownersAt(data.meta.yearsSimulated);
    for (const region of data.regions) {
      assert.equal(finalOwners.get(region.id), region.owner, `${name}: final owner of ${region.id}`);
    }
    for (const event of world.export.events) {
      const text = narrateText(event, world.export.narration, (id) => world.nameOf(id));
      assert.ok(text.length > 0, `${name}: event ${event.id} has narration`);
      assert.doesNotMatch(text, /undefined|\[object Object\]/, `${name}: event ${event.id}`);
    }

    const grid = buildGrid(world.export.world, world.export.regions);
    const order = world.export.civilizations.map((civ) => civ.id);
    const raised = fortifiedFromYear(world.export.events);

    for (const year of probeYears(world.export)) {
      buildRealms(grid, world.timeline.ownersAt(year), order);

      const standing = world.timeline.settlementsAt(year);
      waterMarks(standing, world.export.world, world.raster);
      landmarkMarks(standing, world.export.world);
      wallsStanding(standing, year, raised);
      housesOnMap(world, year, null);

      world.timeline.realmsAt(year);
      world.timeline.battlesIn(year);
      world.timeline.warsIn(year);

      for (const civ of world.export.civilizations) {
        world.timeline.rulerAt(civ.id, year);
        world.timeline.capitalAt(civ.id, year);
        world.timeline.extentAt(civ.id, year);
      }
    }

    // Bounded: a thousand-year world has tens of thousands of people, and the point here is
    // the shape of a figure record rather than the size of the population.
    const end = world.export.meta.yearsSimulated;
    const life: LifeContext = {
      endYear: end,
      figureOf: (id) => world.export.figures.find((figure) => figure.id === id),
      eventsFor: (id) => world.eventsFor(id),
      nameOf: (id) => world.nameOf(id),
      realmAt: (id, year) => world.timeline.realmAt(id, year),
    };

    for (const figure of world.export.figures.slice(0, 300)) {
      const events = world.eventsFor(figure.id);
      buildBiographyEpisodes(figure, events, end);
      groupJourneys(figure.journeys, end);

      // The whole-life derivations reach into containers that arrived at different schemas —
      // affinities at 48, disposition and service later than the figures that lack them — so an
      // older world is the only thing that proves they degrade instead of throwing.
      const arc = buildLifeArc(figure, events, life);
      lifeVantages(figure, arc, life);
      for (const year of [figure.birthYear, figure.deathYear ?? end, end]) {
        standingSentence(figure, standingAt(figure, year, life), life);
        buildConstellation(figure, year, life);
      }
      knownFor(figure, events, end, life);
      historicalSignificance(figure, events, end, life);
      ripplesAfter(figure, life);
    }
  });
}
