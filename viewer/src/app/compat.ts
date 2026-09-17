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
 * From schema 57 the fill does a second job. The engine stopped writing empty lists at all —
 * a figure who never marched carries no `campaigns` key rather than an empty one — so a
 * container can now be absent from a *current* file too. Both cases mean the same thing and
 * take the same repair, which is why `SHAPES` below has to name every container in the export
 * rather than only the ones some version introduced.
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
 * The supported floor is pinned to the oldest retained historical engine export in
 * `test-fixtures/compat`. See its README for provenance and the boundaries exercised.
 */

import { SCHEMA_VERSION, type WorldExport } from './types.ts';

/** Oldest export the viewer will open. Below this it refuses and says to regenerate. */
export const MIN_SCHEMA_VERSION = 28;

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
export const ADDED_IN: readonly { since: number; feature: string }[] = [
  { since: 58, feature: 'the trade a guild mastery is over, so a town’s weavers and its smiths are two seats' },
  { since: 56, feature: 'the craftsman who made an object, told apart from the patron who paid for it' },
  { since: 55, feature: 'what each realm made of a reading it held — taught, disputed, or set aside — apart from having it' },
  { since: 54, feature: 'the copies a sack, a fire or an abandonment destroyed, and what a reading was lost to' },
  { since: 53, feature: 'what each realm held of those claims, and the carrier that explains every change' },
  { since: 52, feature: 'what a claim is about and the number it stated, apart from the comet it began with' },
  { since: 51, feature: 'a betrayal as an episode both parties hold, with its year, place and cause' },
  { since: 50, feature: 'marriages turned on, and the mark a betrayal leaves on a bond' },
  { since: 49, feature: 'quarrels over a post given to somebody standing beside them' },
  { since: 48, feature: 'the friendships people made, and the betrayals they allowed' },
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

/**
 * Versions that changed the file without adding a fact to it.
 *
 * These must not appear in `ADDED_IN`: an export one of them predates is missing nothing, and
 * telling its reader otherwise would put a phantom in the banner. But a bump has to be
 * accounted for somewhere, or the guard that catches a schema change the viewer was never told
 * about is satisfied by silence. So a size or shape change is declared here instead, and the
 * test in `compat.test.ts` accepts either list.
 *
 * Version 57 dropped the denormalised `indices` section, which the reader rebuilds in one pass;
 * stopped writing doubles at seventeen digits; and stopped writing empty containers. The same
 * history, in 17.6 MB raw and 1.48 MB gzipped where it was 18.7 MB and 1.79 MB.
 */
export const CHANGED_NO_FACTS: readonly number[] = [57];

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
 * Every container in the export, by the shape that carries it.
 *
 * Two different files need this and need the same thing from it. An export older than the
 * current schema is missing the containers later versions added. An export from schema 57 on
 * is missing every container that happened to be empty, because the engine stopped writing
 * `"campaigns":[]` twenty-five thousand times per world. In both cases the key is absent and
 * means "none", and in both cases `[].map` over `undefined` is the crash this prevents.
 *
 * That second case is why this table has to be complete rather than merely cover what the
 * views happen to read today. Before schema 57 a container missing from the file was a
 * container the engine had never heard of; now any of them can be missing from any object. A
 * list added to the export and not added here is a page that throws on the first world where
 * nobody did that thing.
 *
 * `lists` are array fields to fill; `children` are the fields whose values carry containers of
 * their own, so the walk knows where to go next.
 */
interface Shape {
  lists?: readonly string[];
  children?: Readonly<Record<string, string>>;
}

const SHAPES: Readonly<Record<string, Shape>> = {
  root: {
    lists: [
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
      'claimTransitions',
      'claimStandings',
      'series',
    ],
    children: {
      meta: 'meta',
      world: 'world',
      regions: 'region',
      cultures: 'culture',
      civilizations: 'civilization',
      dynasties: 'dynasty',
      tradeRoutes: 'tradeRoute',
      figures: 'figure',
      wars: 'war',
      religions: 'religion',
      artifacts: 'artifact',
      series: 'series',
    },
  },
  meta: { lists: ['systemOrder'] },
  world: { lists: ['rivers'], children: { cosmology: 'cosmology' } },
  cosmology: {
    lists: ['companions', 'moons', 'homeMoons', 'comets', 'apparitions', 'checks'],
    children: { companions: 'companion' },
  },
  companion: { lists: ['moons'] },
  region: { lists: ['adjacent'] },
  culture: { children: { lexicon: 'lexicon' } },
  lexicon: { lists: ['sources', 'soundShifts', 'sampleNames', 'samplePlaces'] },
  civilization: {
    lists: ['rulerIds', 'settlementIds', 'territoryRegionIds', 'relations', 'allies'],
  },
  dynasty: { lists: ['rulerIds', 'memberIds'] },
  tradeRoute: { children: { road: 'road' } },
  road: { lists: ['points'] },
  war: { lists: ['attackers', 'defenders', 'battleIds', 'cededRegionIds'] },
  religion: { lists: ['settlementIds'] },
  artifact: { lists: ['provenance'], children: { tomeContents: 'tomeContents' } },
  tomeContents: { lists: ['carries', 'copies', 'sections'], children: { sections: 'tomeSection' } },
  tomeSection: { lists: ['references'] },
  series: { lists: ['values'] },
  figure: {
    lists: [
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
      'affinities',
      'betrayals',
      'plots',
      'guardianships',
      'mentorships',
      'observations',
      'claims',
      'childIds',
      'spouseIds',
    ],
    children: {
      bonds: 'bond',
      undertakings: 'undertaking',
      claims: 'claim',
      plots: 'plot',
      disputes: 'dispute',
      affinities: 'affinity',
    },
  },
  bond: { lists: ['kinds'] },
  undertaking: { lists: ['participantIds', 'steps'] },
  claim: { lists: ['restsOnYears'] },
  plot: { lists: ['members', 'acts'] },
  dispute: { lists: ['acts'] },
  affinity: { lists: ['acts'] },
};

/**
 * Gives an export the containers it does not carry.
 *
 * Mutates the parsed object rather than rebuilding it. The argument is always a value that
 * came straight out of `JSON.parse` and is about to be handed to `buildWorld`, and a world
 * file runs to tens of megabytes — a defensive copy here would double the peak footprint of
 * opening one to no end.
 *
 * `events[].extra` and `events[].data` are deliberately not filled. They have always been
 * optional in the export and every reader already treats them so, and there are three hundred
 * thousand events in a large world to not allocate an empty array for.
 */
export function normalizeExport(data: WorldExport): WorldExport {
  const root = data as unknown as Record<string, unknown>;

  if (!root.narration || typeof root.narration !== 'object') root.narration = {};
  fill(root, 'root');

  return data;
}

function fill(value: Record<string, unknown>, shapeName: string): void {
  const shape = SHAPES[shapeName];
  if (!shape) return;

  for (const field of shape.lists ?? []) {
    if (!Array.isArray(value[field])) value[field] = [];
  }

  if (!shape.children) return;

  for (const [field, childShape] of Object.entries(shape.children)) {
    const child = value[field];
    if (Array.isArray(child)) {
      for (const element of child as Record<string, unknown>[]) fill(element, childShape);
    } else if (child && typeof child === 'object') {
      fill(child as Record<string, unknown>, childShape);
    }
  }
}
