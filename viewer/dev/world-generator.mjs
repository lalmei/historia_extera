// @ts-check
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { copyFile, mkdir, open, readdir, rename, stat, unlink } from 'node:fs/promises';
import path from 'node:path';

/**
 * Runs the generator CLI on request, for the dev server only.
 *
 * The viewer is a static bundle and stays one. This is an Astro integration that does
 * nothing unless the command is `dev`, and then does two things: it injects `/new` (the
 * page in this directory, which the file router never sees) and it installs the endpoint
 * below as Vite middleware rather than as an Astro route — an on-demand route would make
 * `astro build` demand an adapter, which is exactly the coupling the static shell exists
 * to avoid.
 *
 * So a built viewer has no generator page, no endpoint, and none of the island's code:
 * it is still a folder of files that opens from disk, with the export as its whole
 * contract with the engine. What this buys during development is the loop that was
 * otherwise two terminals — pick a seed, run it, look at it.
 *
 * Editing this file needs the dev server restarted by hand. Astro restarts itself when
 * the config changes, but Node has already imported this module and keeps the copy it
 * has, so a reload silently runs the old code.
 */

const RUNS = '/api/worlds/runs';

/** Lists finished exports without making the browser download each entire world. */
const WORLD_CATALOG = '/api/worlds';

/** Exact world files that can be moved out of the catalog. */
const WORLD_FILES = `${WORLD_CATALOG}/files/`;

/** Where the viewer asks for a world file, and what `?world=` points at. */
const WORLDS = '/worlds/';

/** Where the CLI lives during repository development. */
const CLI_PROJECT = 'src/HistoryEngine.Cli';

/** Default generated-world folder during repository development. */
const WORLD_DIR = ['viewer', 'public', 'worlds'];

/**
 * Where a trashed export goes, relative to the repository root.
 *
 * Outside `public/` deliberately. Astro copies `public/` into `dist/` wholesale, including
 * dotfolders, so a recovery folder kept beside the worlds would put every world the user
 * thought they had deleted back into the built site — the one place a deleted world must not
 * be. `build/` is already the repository's home for regenerable scratch output, and ignored.
 */
const TRASH_DIR = ['build', 'world-trash'];

/** Native release builds override these with writable Application Support paths. */
const NATIVE_CLI = process.env.HISTORIA_CLI?.trim();
const NATIVE_WORLD_DIR = process.env.HISTORIA_WORLD_DIR?.trim();
const NATIVE_TRASH_DIR = process.env.HISTORIA_TRASH_DIR?.trim();

/**
 * What the form may set, and the bounds each value is held to.
 *
 * Bounds are not a security control — the arguments never reach a shell — but a typo of
 * 500000 years would wedge the machine for the rest of the afternoon. World size is aligned
 * to the engine's 256-unit terrain lattice so a periodic seam can close exactly. `--raster`
 * remains at the CLI's default.
 *
 * The size floor is the smallest world that can seat a single civilization at all; the
 * regions-per-civilization check in `readParams` is what holds a given civ count to a size.
 *
 * The seed ceiling is JavaScript's, not the engine's: seeds are `ulong` in C#, but a
 * number past 2^53 would arrive here already rounded, and silently simulating a
 * different seed than the one typed is worse than refusing it.
 */
const PARAMS = {
  seed: { fallback: 42, min: 0, max: Number.MAX_SAFE_INTEGER },
  years: { fallback: 300, min: 1, max: 5000 },
  civs: { fallback: 8, min: 1, max: 64 },
  size: { fallback: 4096, min: 512, max: 8192 },
};

/**
 * The engine's own siting floor, mirrored: `WorldConfig.RegionsPerCivilization` and the
 * `RegionSize` the CLI is left at. Below this a world reports success and contains nothing.
 */
const REGION_SIZE = 128;
const REGIONS_PER_CIV = 16;

/** Lines of CLI output kept per run. Enough for the summary, bounded against a runaway build log. */
const LOG_LINES = 200;

/** Finished runs kept for inspection before the oldest are dropped. */
const RUN_HISTORY = 20;

/**
 * @typedef {{
 *   seed: number,
 *   years: number,
 *   civs: number,
 *   size: number,
 *   eastWestPeriodic: boolean,
 * }} RunParams
 * @typedef {'running' | 'done' | 'failed' | 'cancelled'} RunStatus
 * @typedef {{
 *   id: string,
 *   params: RunParams,
 *   status: RunStatus,
 *   log: string[],
 *   world?: string,
 *   bytes?: number,
 *   error?: string,
 *   startedAt: number,
 *   finishedAt?: number,
 *   year?: number,
 *   endYear?: number,
 *   child?: import('node:child_process').ChildProcess,
 * }} Run
 */

/**
 * @returns {import('astro').AstroIntegration}
 */
export function worldGenerator() {
  return {
    name: 'historia:world-generator',
    hooks: {
      'astro:config:setup': ({ command, injectRoute, updateConfig }) => {
        // `build` and `preview` get nothing at all — not the route, not the endpoint.
        if (command !== 'dev') return;

        injectRoute({ pattern: '/new', entrypoint: './dev/new.astro' });
        updateConfig({ vite: { plugins: [generatorEndpoint()] } });
      },
    },
  };
}

/**
 * @returns {import('vite').Plugin}
 */
function generatorEndpoint() {
  /** @type {Map<string, Run>} */
  const runs = new Map();

  /** At most one at a time: repository `dotnet run` invocations contend over the same obj/bin. */
  /** @type {Run | null} */
  let active = null;

  return {
    name: 'historia:world-generator-endpoint',

    // Ordered ahead of Astro's own dev middleware, which would otherwise answer these
    // paths with its 404 page.
    enforce: 'pre',

    config() {
      return {
        server: {
          // A finished world is megabytes of JSON appearing inside `public/`, which the
          // watcher would answer with a full page reload — throwing away the very page
          // that asked for it, mid-poll. Nothing in `worlds/` is ever imported, so
          // there is nothing to reload for.
          watch: {
            ignored: [
              `**/${WORLD_DIR.join('/')}/**`,
              ...(NATIVE_WORLD_DIR ? [`${path.resolve(NATIVE_WORLD_DIR)}/**`] : []),
            ],
          },
        },
      };
    },

    configureServer(server) {
      const root = path.resolve(server.config.root, '..');
      const worldDir = configuredDirectory(NATIVE_WORLD_DIR, root, WORLD_DIR);
      const trashDir = configuredDirectory(NATIVE_TRASH_DIR, root, TRASH_DIR);

      // The repository path normally exists already. A first native-app launch has an
      // empty Application Support directory, so create it before listing or generating.
      void mkdir(worldDir, { recursive: true });

      server.middlewares.use((req, res, next) => {
        const url = new URL(req.url ?? '/', 'http://localhost');

        if (url.pathname.startsWith(WORLDS)) return serveWorld(url.pathname, req, res, next);

        if (req.method === 'GET' && url.pathname === WORLD_CATALOG) {
          listWorlds(worldDir)
            .then((worlds) => send(res, 200, { worlds }))
            .catch((cause) => send(res, 500, { error: message(cause) }));
          return;
        }

        if (req.method === 'DELETE' && url.pathname.startsWith(WORLD_FILES)) {
          let name;
          let permanent;
          try {
            name = worldName(url.pathname.slice(WORLD_FILES.length));
            permanent = permanentDeletion(url.searchParams.get('permanent'));
          } catch (cause) {
            send(res, 400, { error: message(cause) });
            return;
          }

          removeWorld(worldDir, trashDir, name, permanent)
            .then((deleted) => send(res, 200, deleted))
            .catch((cause) =>
              send(res, isNodeError(cause) && cause.code === 'ENOENT' ? 404 : 500, {
                error: message(cause),
              }),
            );
          return;
        }

        if (!url.pathname.startsWith(RUNS)) return next();

        const id = url.pathname.slice(RUNS.length).replace(/^\//, '');

        if (req.method === 'POST' && id === '') {
          start(req, res);
          return;
        }

        if (req.method === 'GET' && id !== '') {
          const run = runs.get(id);
          if (!run) return send(res, 404, { error: `no run ${id}` });
          return send(res, 200, report(run));
        }

        if (req.method === 'DELETE' && id !== '') {
          const run = runs.get(id);
          if (!run) return send(res, 404, { error: `no run ${id}` });
          if (run.status === 'running') stop(run);
          return send(res, 200, report(run));
        }

        send(res, 405, { error: `${req.method} ${url.pathname} is not a thing` });
      });

      // A dev server that goes away should not leave a detached simulation behind.
      server.httpServer?.on('close', () => {
        if (active) stop(active);
      });

      /**
       * @param {import('node:http').IncomingMessage} req
       * @param {import('node:http').ServerResponse} res
       */
      async function start(req, res) {
        if (active?.status === 'running') {
          return send(res, 409, {
            error: 'a world is already being generated',
            running: report(active),
          });
        }

        /** @type {RunParams} */
        let params;
        try {
          params = readParams(await readJson(req));
        } catch (cause) {
          return send(res, 400, { error: message(cause) });
        }

        const boundary = params.eastWestPeriodic ? '-ewp' : '';
        const name =
          `world-s${params.seed}-y${params.years}-c${params.civs}-z${params.size}${boundary}.json`;
        const output = path.join(worldDir, name);

        const generatorArgs = [
          '--seed',
          String(params.seed),
          '--years',
          String(params.years),
          '--civs',
          String(params.civs),
          '--size',
          String(params.size),
          ...(params.eastWestPeriodic ? ['--east-west-periodic'] : []),
          '--out',
          output,
          '--sample',
          '0',
        ];
        const command = NATIVE_CLI || 'dotnet';
        const commandArgs = NATIVE_CLI
          ? generatorArgs
          : ['run', '--project', CLI_PROJECT, '--', ...generatorArgs];

        /** @type {Run} */
        const run = {
          id: randomUUID(),
          params,
          status: 'running',
          log: [],
          startedAt: Date.now(),
        };

        // No shell, and every argument is a validated number — the only strings the
        // command line carries are ones written above.
        const child = spawn(command, commandArgs, {
          cwd: root,
          stdio: ['ignore', 'pipe', 'pipe'],
          // Its own process group, so cancelling reaches the simulation `dotnet run`
          // starts as a child rather than only the launcher.
          detached: process.platform !== 'win32',
        });

        run.child = child;
        runs.set(run.id, run);
        active = run;
        prune(runs);

        collect(child.stdout, run);
        collect(child.stderr, run);

        child.on('error', (cause) => {
          finish(run, 'failed', `could not run the generator: ${message(cause)}`);
        });

        child.on('close', async (code) => {
          if (run.status !== 'running') return; // Already cancelled.

          if (code !== 0) {
            return finish(run, 'failed', `the generator exited with code ${code}`);
          }

          try {
            run.bytes = (await stat(output)).size;
          } catch {
            return finish(run, 'failed', `the generator reported success but wrote no ${output}`);
          }

          // Viewer-relative, the same form `?world=` takes, so it resolves against the
          // page however the viewer is based.
          run.world = `worlds/${name}`;
          finish(run, 'done');
        });

        send(res, 201, report(run));
      }

      /**
       * Serves `public/worlds/` on every request rather than from the listing Vite took
       * at startup.
       *
       * Vite's own static handler never sees a world written after the dev server came
       * up — which is not only this feature's problem: `make generate OUT=…` into an
       * open dev server has always needed a restart before the file could be opened. A
       * few lines here make both work, and anything this does not resolve falls through
       * to Vite untouched.
       *
       * @param {string} pathname
       * @param {import('node:http').IncomingMessage} req
       * @param {import('node:http').ServerResponse} res
       * @param {() => void} next
       */
      async function serveWorld(pathname, req, res, next) {
        if (req.method !== 'GET' && req.method !== 'HEAD') return next();

        // basename, so nothing addresses its way out of the directory.
        const name = path.basename(decodeURIComponent(pathname.slice(WORLDS.length)));
        if (!name.endsWith('.json')) return next();

        const file = path.join(worldDir, name);

        let size;
        try {
          const found = await stat(file);
          if (!found.isFile()) return next();
          size = found.size;
        } catch {
          return next();
        }

        res.statusCode = 200;
        res.setHeader('content-type', 'application/json; charset=utf-8');
        res.setHeader('content-length', String(size));
        // Regenerating a seed rewrites the same name in place, and a cached copy of the
        // previous run is the one thing that would make this feature look broken.
        res.setHeader('cache-control', 'no-store');

        if (req.method === 'HEAD') return res.end();

        createReadStream(file).pipe(res);
      }

      /** @param {Run} run */
      function stop(run) {
        const child = run.child;
        if (!child?.pid) return;

        try {
          // Negative pid is the group, so the simulation goes with the launcher.
          if (process.platform !== 'win32') process.kill(-child.pid, 'SIGTERM');
          else child.kill();
        } catch {
          child.kill();
        }

        finish(run, 'cancelled', 'cancelled');
      }

      /**
       * @param {Run} run
       * @param {RunStatus} status
       * @param {string} [error]
       */
      function finish(run, status, error) {
        run.status = status;
        run.finishedAt = Date.now();
        if (error) run.error = error;
        run.child = undefined;
        if (active === run) active = null;
      }
    },
  };
}

/**
 * Lists every JSON export without parsing the chronicle. Identity (seed, years,
 * size, engine) is read from the file header; civilization count comes from the
 * generator filename when the name carries it.
 *
 * @param {string} worldDir
 */
async function listWorlds(worldDir) {
  let entries;
  try {
    entries = await readdir(worldDir, { withFileTypes: true });
  } catch (cause) {
    if (isNodeError(cause) && cause.code === 'ENOENT') return [];
    throw cause;
  }

  const worlds = await Promise.all(
    entries
      .filter((entry) => entry.isFile() && entry.name.endsWith('.json'))
      .map(async (entry) => inspectWorld(path.join(worldDir, entry.name), entry.name)),
  );

  return worlds.sort((a, b) => (b.modifiedAt ?? '').localeCompare(a.modifiedAt ?? ''));
}

/**
 * @param {string} file
 * @param {string} name
 */
async function inspectWorld(file, name) {
  let info;
  try {
    info = await stat(file);
  } catch (cause) {
    return {
      name,
      world: `worlds/${name}`,
      bytes: 0,
      schemaVersion: null,
      params: null,
      engineVersion: null,
      error: message(cause),
    };
  }

  try {
    const header = await readWorldHeader(file);
    return {
      name,
      world: `worlds/${name}`,
      bytes: info.size,
      modifiedAt: info.mtime.toISOString(),
      schemaVersion: header.schemaVersion,
      engineVersion: header.engineVersion,
      designation: header.designation,
      worldName: header.worldName,
      kind: header.kind,
      params: paramsFor(name, header),
    };
  } catch (cause) {
    return {
      name,
      world: `worlds/${name}`,
      bytes: info.size,
      modifiedAt: info.mtime.toISOString(),
      schemaVersion: null,
      params: paramsFromFilename(name),
      engineVersion: null,
      error: message(cause),
    };
  }
}

/**
 * Reads only the beginning of each export. Schema, seed, years and size all sit
 * before the raster payload, so cataloguing several multi-megabyte worlds does
 * not mean parsing or retaining their chronicles.
 *
 * @param {string} file
 */
async function readWorldHeader(file) {
  const handle = await open(file, 'r');

  try {
    const buffer = Buffer.alloc(32 * 1024);
    const { bytesRead } = await handle.read(buffer, 0, buffer.length, 0);
    const header = buffer.toString('utf8', 0, bytesRead);

    // Cut before the raster so a number inside the base64 cannot be mistaken for
    // width or years. The fields we want are all declared before it.
    const rasterAt = header.search(/"raster"\s*:/);
    const prefix = rasterAt === -1 ? header : header.slice(0, rasterAt);

    const schemaVersion = readIntField(prefix, 'schemaVersion');
    if (schemaVersion === null) throw new Error('schemaVersion is missing from the file header');

    return {
      schemaVersion,
      seed: readIntField(prefix, 'seed'),
      years: readIntField(prefix, 'yearsSimulated'),
      size: readIntField(prefix, 'width'),
      eastWestPeriodic: readBoolField(prefix, 'eastWestPeriodic'),
      engineVersion: readStringField(prefix, 'engineVersion'),
      designation: readStringField(prefix, 'designation'),
      worldName: readWorldName(prefix),
      kind: readWorldKind(prefix),
    };
  } finally {
    await handle.close();
  }
}

/**
 * @param {string} name
 * @param {{
 *   seed: number | null,
 *   years: number | null,
 *   size: number | null,
 *   eastWestPeriodic: boolean | null,
 * }} header
 */
function paramsFor(name, header) {
  const named = paramsFromFilename(name);

  const seed = header.seed ?? named?.seed;
  const years = header.years ?? named?.years;
  const civs = named?.civs ?? PARAMS.civs.fallback;
  const size = header.size ?? named?.size ?? PARAMS.size.fallback;

  if (seed === undefined || years === undefined) return named;

  return {
    seed,
    years,
    civs,
    size,
    eastWestPeriodic: header.eastWestPeriodic ?? named?.eastWestPeriodic ?? false,
  };
}

/**
 * @param {string} name
 * @returns {RunParams | null}
 */
function paramsFromFilename(name) {
  const match = /^world-s(\d+)-y(\d+)-c(\d+)(?:-z(\d+))?(-ewp)?\.json$/i.exec(name);
  if (!match) return null;

  return {
    seed: Number(match[1]),
    years: Number(match[2]),
    civs: Number(match[3]),
    size: match[4] ? Number(match[4]) : PARAMS.size.fallback,
    eastWestPeriodic: Boolean(match[5]),
  };
}

/**
 * @param {string} text
 * @param {string} name
 */
function readIntField(text, name) {
  const match = new RegExp(`"${name}"\\s*:\\s*(-?\\d+)`).exec(text);
  if (!match) return null;
  const value = Number(match[1]);
  return Number.isSafeInteger(value) ? value : null;
}

/**
 * @param {string} text
 * @param {string} name
 */
function readBoolField(text, name) {
  const match = new RegExp(`"${name}"\\s*:\\s*(true|false)`).exec(text);
  if (!match) return null;
  return match[1] === 'true';
}

/**
 * @param {string} text
 * @param {string} name
 */
function readStringField(text, name) {
  const match = new RegExp(`"${name}"\\s*:\\s*"([^"]*)"`).exec(text);
  return match ? match[1] : null;
}

/**
 * The world's proper name, from the `world` object — not the first `"name"` in the file.
 *
 * @param {string} text
 */
function readWorldName(text) {
  const worldAt = text.search(/"world"\s*:\s*\{/);
  if (worldAt === -1) return null;
  const match = /"name"\s*:\s*"([^"]*)"/.exec(text.slice(worldAt));
  return match ? match[1] : null;
}

/**
 * @param {string} text
 * @returns {'Planet' | 'Moon' | null}
 */
function readWorldKind(text) {
  const match = /"kind"\s*:\s*"(Planet|Moon)"/.exec(text);
  if (match === null) return null;

  // The alternation already limits the capture to these two, but only the comparison
  // says so in a way the declared return type can be checked against.
  return match[1] === 'Moon' ? 'Moon' : 'Planet';
}

/**
 * Accepts one encoded basename only. Rejecting path components before joining it to
 * `worldDir` keeps a crafted request from moving anything outside the generated-world folder.
 *
 * @param {string} encoded
 */
function worldName(encoded) {
  let name;
  try {
    name = decodeURIComponent(encoded);
  } catch {
    throw new Error('world name is not valid URL text');
  }

  if (name.length === 0 || name !== path.basename(name) || !name.endsWith('.json')) {
    throw new Error('world name must be one JSON filename');
  }

  return name;
}

/** @param {string | null} value */
function permanentDeletion(value) {
  if (value === null || value === 'false') return false;
  if (value === 'true') return true;
  throw new Error('permanent must be true or false');
}

/**
 * @param {string} worldDir
 * @param {string} trashDir
 * @param {string} name
 * @param {boolean} permanent
 */
async function removeWorld(worldDir, trashDir, name, permanent) {
  const source = await worldFile(worldDir, name);

  if (permanent) {
    await unlink(source);
    return { name, permanent: true };
  }

  return trashWorld(trashDir, name, source);
}

/**
 * @param {string} worldDir
 * @param {string} name
 */
async function worldFile(worldDir, name) {
  const source = path.join(worldDir, name);
  const info = await stat(source);
  if (!info.isFile()) throw new Error(`${name} is not a generated world file`);
  return source;
}

/**
 * Moves a generated export into a recovery folder outside `public/` instead of unlinking it.
 * The UUID keeps repeated deletions of the same regenerated filename from overwriting
 * an earlier recovery copy.
 *
 * @param {string} trashDir
 * @param {string} name
 * @param {string} source
 */
async function trashWorld(trashDir, name, source) {
  await mkdir(trashDir, { recursive: true });

  const extension = path.extname(name);
  const stem = name.slice(0, -extension.length);
  const trashedName = `${stem}-${Date.now()}-${randomUUID().slice(0, 8)}${extension}`;

  // Across directories, so `rename` can fail with EXDEV where the repository straddles two
  // filesystems. Copying and unlinking is the fallback that keeps the move atomic enough.
  const target = path.join(trashDir, trashedName);
  try {
    await rename(source, target);
  } catch (cause) {
    if (!isNodeError(cause) || cause.code !== 'EXDEV') throw cause;
    await copyFile(source, target);
    await unlink(source);
  }

  return {
    name,
    permanent: false,
    recoveryPath: path.join(trashDir, trashedName),
  };
}

/**
 * Resolve an optional absolute native-app path, otherwise keep the repository layout.
 *
 * @param {string | undefined} configured
 * @param {string} root
 * @param {string[]} fallback
 */
function configuredDirectory(configured, root, fallback) {
  return configured ? path.resolve(configured) : path.join(root, ...fallback);
}

/** @param {unknown} cause */
function isNodeError(cause) {
  return cause instanceof Error && 'code' in cause;
}

/**
 * The run as the client sees it: no child process handle, and elapsed time resolved
 * here so a page that reconnects to a run in flight shows the right clock.
 *
 * @param {Run} run
 */
function report(run) {
  return {
    id: run.id,
    params: run.params,
    status: run.status,
    log: run.log,
    world: run.world,
    bytes: run.bytes,
    error: run.error,
    year: run.year ?? 0,
    endYear: run.endYear ?? run.params.years,
    elapsedMs: (run.finishedAt ?? Date.now()) - run.startedAt,
  };
}

/**
 * @param {unknown} body
 * @returns {RunParams}
 */
function readParams(body) {
  if (typeof body !== 'object' || body === null) throw new Error('expected a JSON object');

  const given = /** @type {Record<string, unknown>} */ (body);

  const civs = readNumber('civs', given.civs);
  const size = readWorldSize(given.size);

  // The engine rejects this too. Checking here as well means the form says so straight away
  // instead of spawning a CLI that exits with the same complaint half a second later.
  const regions = Math.floor(size / REGION_SIZE) ** 2;
  if (regions < REGIONS_PER_CIV * civs) {
    throw new Error(
      `a ${size}-unit world holds ${regions} regions, too few to seat ${civs} ` +
        `civilizations — raise the world size to at least ${minimumSize(civs)} or ask for fewer`,
    );
  }

  return {
    seed: readNumber('seed', given.seed),
    years: readNumber('years', given.years),
    civs,
    size,
    eastWestPeriodic: readBoolean('eastWestPeriodic', given.eastWestPeriodic, false),
  };
}

/** @param {unknown} value */
function readWorldSize(value) {
  const size = readNumber('size', value);
  if (size % 256 !== 0) throw new Error('size must be a multiple of 256');
  return size;
}

/**
 * Smallest world size that clears the engine's regions-per-civilization floor, rounded up to
 * the 256-unit step the form uses.
 *
 * @param {number} civs
 */
function minimumSize(civs) {
  const step = 256;
  let size = PARAMS.size.min;
  while (Math.floor(size / REGION_SIZE) ** 2 < REGIONS_PER_CIV * civs) size += step;
  return size;
}

/**
 * @param {string} name
 * @param {unknown} value
 * @param {boolean} fallback
 */
function readBoolean(name, value, fallback) {
  if (value === undefined || value === null || value === '') return fallback;
  if (typeof value !== 'boolean') throw new Error(`${name} must be true or false`);
  return value;
}

/**
 * @param {keyof typeof PARAMS} name
 * @param {unknown} value
 */
function readNumber(name, value) {
  const { fallback, min, max } = PARAMS[name];
  if (value === undefined || value === null || value === '') return fallback;

  const parsed = typeof value === 'number' ? value : Number(value);

  if (!Number.isSafeInteger(parsed)) throw new Error(`${name} must be a whole number`);
  if (parsed < min || parsed > max) {
    throw new Error(`${name} must be between ${min} and ${max.toLocaleString('en-US')}`);
  }

  return parsed;
}

/**
 * Keeps the tail of the CLI's output. Partial lines are held until their newline
 * arrives, so a summary table never renders half-written.
 *
 * @param {import('node:stream').Readable | null} stream
 * @param {Run} run
 */
function collect(stream, run) {
  if (!stream) return;

  let pending = '';

  stream.setEncoding('utf8');
  stream.on('data', (chunk) => {
    pending += chunk;
    const lines = pending.split('\n');
    pending = lines.pop() ?? '';

    for (const line of lines) {
      const text = line.trimEnd();
      if (text.length === 0) continue;

      const progress = /^progress (\d+)\/(\d+)$/.exec(text);
      if (progress) {
        run.year = Number(progress[1]);
        run.endYear = Number(progress[2]);
        continue;
      }

      run.log.push(text);
    }

    if (run.log.length > LOG_LINES) run.log.splice(0, run.log.length - LOG_LINES);
  });

  stream.on('end', () => {
    const text = pending.trimEnd();
    if (text.length === 0) return;

    const progress = /^progress (\d+)\/(\d+)$/.exec(text);
    if (progress) {
      run.year = Number(progress[1]);
      run.endYear = Number(progress[2]);
      return;
    }

    run.log.push(text);
  });
}

/** @param {Map<string, Run>} runs */
function prune(runs) {
  const finished = [...runs.values()].filter((run) => run.status !== 'running');
  for (const run of finished.slice(0, Math.max(0, finished.length - RUN_HISTORY))) {
    runs.delete(run.id);
  }
}

/** @param {import('node:http').IncomingMessage} req */
async function readJson(req) {
  /** @type {Buffer[]} */
  const chunks = [];
  let size = 0;

  for await (const chunk of req) {
    size += chunk.length;
    if (size > 64 * 1024) throw new Error('request body too large');
    chunks.push(chunk);
  }

  const text = Buffer.concat(chunks).toString('utf8').trim();
  if (text.length === 0) return {};

  try {
    return JSON.parse(text);
  } catch {
    throw new Error('request body was not JSON');
  }
}

/**
 * @param {import('node:http').ServerResponse} res
 * @param {number} status
 * @param {unknown} body
 */
function send(res, status, body) {
  const json = JSON.stringify(body);
  res.statusCode = status;
  res.setHeader('content-type', 'application/json; charset=utf-8');
  res.setHeader('cache-control', 'no-store');
  res.end(json);
}

/** @param {unknown} cause */
function message(cause) {
  return cause instanceof Error ? cause.message : String(cause);
}
