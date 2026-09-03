/**
 * Reading exports the current engine did not write.
 *
 * The viewer used to accept exactly one `schemaVersion` and refuse everything else, on the
 * reasoning that a wrong chronicle is worse than a refused one. That reasoning still holds
 * for the *shape* of a file, but it was doing more work than it had to: every schema change
 * since v21 has added fields rather than moved or reinterpreted them, so an older export is
 * not a file the viewer misunderstands — it is a file that has less in it.
 *
 * So the pin becomes a range. Inside it, `normalizeExport` fills in the containers a later
 * schema introduced, with nothing in them, and the views read "none recorded" where they
 * would otherwise read a list. Outside it the viewer still refuses, and says which side of
 * the range the file fell off.
 *
 * Two rules keep this honest:
 *
 *   - **Fill containers, never facts.** A missing list becomes empty; a missing value stays
 *     missing. An older world may show fewer journeys than it lived, but never a journey it
 *     did not. Anything the export cannot answer is marked absent rather than defaulted, so
 *     the type system makes the views ask.
 *   - **Say so on screen.** `schemaVerdict` names the version and what a reader will not find
 *     in it. The Worlds Library shows that per row and the reading view banners it, so nobody
 *     mistakes an empty panel for a quiet century.
 *
 * `MIN_SCHEMA_VERSION` is the oldest export this has actually been run against — see
 * `compat.test.ts`, which loads one file of every schema kept in `public/worlds/`. Raising the
 * floor is a decision about what we are willing to test, not a guess about what might work.
 */

import { SCHEMA_VERSION, type WorldExport } from './types.ts';

/** Oldest export the viewer will open. Below this it refuses and says to regenerate. */
export const MIN_SCHEMA_VERSION = 21;

export type SchemaState = 'current' | 'older' | 'too-old' | 'too-new' | 'unreadable';

export interface SchemaVerdict {
  state: SchemaState;
  version: number | null;
  /** Whether the viewer will open this file at all. */
  readable: boolean;
  /** One sentence, fit for a table cell's title or a banner. */
  summary: string;
  /** What this export predates, newest schema first. Empty unless `state` is `older`. */
  missing: string[];
}

/**
 * What each schema version added, in the words the viewer guide uses.
 *
 * Taken from the guide's own version history rather than inferred by diffing files: an
 * absent field and an empty list look identical in an export, and only the record knows
 * which of the two an old world has.
 */
const ADDED_IN: readonly { since: number; feature: string }[] = [
  { since: 47, feature: 'the ranks soldiers rose through in their realm’s army' },
  { since: 46, feature: 'the giants of the local system, their moons and the world’s orientation' },
  { since: 45, feature: 'the chronicle line behind a holy site’s dedication' },
  { since: 44, feature: 'journey durations and dated returns' },
  { since: 42, feature: 'where a figure lived in any year' },
  { since: 41, feature: 'hardship carried by the people who lived through it' },
  { since: 40, feature: 'backgrounds, guardianships and mentorships' },
  { since: 39, feature: 'a plot told from its target’s side' },
  { since: 38, feature: 'persistent conspiracies' },
  { since: 32, feature: 'the system’s comets' },
  { since: 31, feature: 'the host galaxy, the night sky and the cosmology page' },
  { since: 30, feature: 'how a journey ended' },
  { since: 28, feature: 'journeys, officials and scribes' },
  { since: 27, feature: 'a figure’s campaigns' },
];

export function schemaVerdict(version: number | null | undefined): SchemaVerdict {
  if (version === null || version === undefined || !Number.isFinite(version)) {
    return {
      state: 'unreadable',
      version: null,
      readable: false,
      summary: 'This file does not declare a schema version, so it is not a world export the viewer can read.',
      missing: [],
    };
  }

  if (version > SCHEMA_VERSION) {
    return {
      state: 'too-new',
      version,
      readable: false,
      summary:
        `Written by a newer engine (schema v${version}); this viewer reads up to v${SCHEMA_VERSION}. ` +
        'Update the viewer, or open it with the engine that wrote it.',
      missing: [],
    };
  }

  if (version < MIN_SCHEMA_VERSION) {
    return {
      state: 'too-old',
      version,
      readable: false,
      summary:
        `Schema v${version} predates the oldest export this viewer can read (v${MIN_SCHEMA_VERSION}). ` +
        'Run the seed again through the current engine.',
      missing: [],
    };
  }

  if (version === SCHEMA_VERSION) {
    return {
      state: 'current',
      version,
      readable: true,
      summary: `Schema v${version} — everything this viewer can show.`,
      missing: [],
    };
  }

  const missing = ADDED_IN.filter((entry) => entry.since > version).map((entry) => entry.feature);

  return {
    state: 'older',
    version,
    readable: true,
    summary:
      `Schema v${version}, written by an earlier engine. It opens, but it was recorded before ` +
      `${missing.length} later addition${missing.length === 1 ? '' : 's'} to the export, ` +
      'so those panels stay empty. Run the seed again to get them.',
    missing,
  };
}

/** Whether a version can be opened at all. Cheaper to read at a call site than the verdict. */
export function isReadableSchema(version: number | null | undefined): boolean {
  return schemaVerdict(version).readable;
}

/**
 * Lists the export gained after v21, by the entity that carries them.
 *
 * Every one of these is a container the views iterate. An older file simply has no key
 * there, and `[].map` over `undefined` is the crash this exists to prevent.
 */
const LIST_FIELDS = {
  root: [
    'regions',
    'cultures',
    'civilizations',
    'dynasties',
    'settlements',
    'tradeRoutes',
    'figures',
    'wars',
    'battles',
    'religions',
    'holySites',
    'artifacts',
    'events',
    'series',
  ],
  regions: ['adjacent'],
  civilizations: ['rulerIds', 'settlementIds', 'territoryRegionIds', 'relations', 'allies'],
  dynasties: ['rulerIds', 'memberIds'],
  wars: ['attackers', 'defenders', 'battleIds', 'cededRegionIds'],
  religions: ['settlementIds'],
  artifacts: ['provenance'],
  series: ['values'],
  figures: [
    'residences',
    'titles',
    'service',
    'campaigns',
    'journeys',
    'bonds',
    'memories',
    'injuries',
    'undertakings',
    'disputes',
    'plots',
    'guardianships',
    'mentorships',
    'observations',
    'claims',
    'childIds',
    'spouseIds',
  ],
} as const;

/**
 * Gives an older export the containers a later schema introduced.
 *
 * Mutates the parsed object rather than rebuilding it. The argument is always a value that
 * came straight out of `JSON.parse` and is about to be handed to `buildWorld`, and a world
 * file runs to tens of megabytes — a defensive copy here would double the peak footprint of
 * opening one to no end.
 */
export function normalizeExport(data: WorldExport): WorldExport {
  const root = data as unknown as Record<string, unknown>;

  for (const field of LIST_FIELDS.root) {
    if (!Array.isArray(root[field])) root[field] = [];
  }

  if (!root.narration || typeof root.narration !== 'object') root.narration = {};

  const indices = (root.indices ?? {}) as Record<string, unknown>;
  if (!indices.eventsByEntity || typeof indices.eventsByEntity !== 'object') {
    indices.eventsByEntity = {};
  }
  if (!indices.eventsByYear || typeof indices.eventsByYear !== 'object') indices.eventsByYear = {};
  if (!indices.eventCountsByKind || typeof indices.eventCountsByKind !== 'object') {
    indices.eventCountsByKind = {};
  }
  root.indices = indices;

  const meta = root.meta as Record<string, unknown> | undefined;
  if (meta && !Array.isArray(meta.systemOrder)) meta.systemOrder = [];

  const world = root.world as Record<string, unknown> | undefined;
  if (world && !Array.isArray(world.rivers)) world.rivers = [];

  for (const [collection, fields] of Object.entries(LIST_FIELDS)) {
    if (collection === 'root') continue;
    const entities = root[collection];
    if (!Array.isArray(entities)) continue;

    for (const entity of entities as Record<string, unknown>[]) {
      for (const field of fields) {
        if (!Array.isArray(entity[field])) entity[field] = [];
      }
    }
  }

  // Nested lists, for the structures that gained one after the version that introduced them.
  for (const figure of root.figures as Record<string, unknown>[]) {
    for (const bond of figure.bonds as Record<string, unknown>[]) {
      if (!Array.isArray(bond.kinds)) bond.kinds = [];
    }
    for (const plot of figure.plots as Record<string, unknown>[]) {
      if (!Array.isArray(plot.members)) plot.members = [];
      if (!Array.isArray(plot.acts)) plot.acts = [];
    }
    for (const dispute of figure.disputes as Record<string, unknown>[]) {
      if (!Array.isArray(dispute.acts)) dispute.acts = [];
    }
    for (const undertaking of figure.undertakings as Record<string, unknown>[]) {
      if (!Array.isArray(undertaking.participantIds)) undertaking.participantIds = [];
      if (!Array.isArray(undertaking.steps)) undertaking.steps = [];
    }
  }

  return data;
}
