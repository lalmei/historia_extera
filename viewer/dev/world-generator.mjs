// @ts-check
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { stat } from 'node:fs/promises';
import path from 'node:path';

/**
 * Runs the generator CLI on request, for the dev server only.
 *
 * The viewer is a static bundle and stays one: this is a Vite dev middleware, not an
 * Astro route, so nothing about it survives `astro build`. A built viewer still has no
 * server behind it, still opens from disk, and still treats the export file as the whole
 * contract with the engine. What it buys during development is the loop that was
 * otherwise two terminals — pick a seed, run it, look at it — done in the page that is
 * already open.
 *
 * The client half is `src/app/generate.ts`, gated on `import.meta.env.DEV`, so the
 * calling code is compiled out of the production bundle rather than merely unused.
 */

const RUNS = '/api/worlds/runs';

/** Where the viewer asks for a world file, and what `?world=` points at. */
const WORLDS = '/worlds/';

/** Where the CLI lives, relative to the repository root. */
const CLI_PROJECT = 'src/HistoryEngine.Cli';

/** Where generated worlds land, relative to the repository root, and what the viewer serves. */
const WORLD_DIR = ['viewer', 'public', 'worlds'];

/**
 * What the form may set, and the bounds each value is held to.
 *
 * Bounds are not a security control — the arguments never reach a shell — but a typo of
 * 500000 years would wedge the machine for the rest of the afternoon. `--size` and
 * `--raster` are deliberately absent and stay at the CLI's own defaults, which is what
 * `make generate` uses, so a world generated here matches one generated from a terminal.
 *
 * The seed ceiling is JavaScript's, not the engine's: seeds are `ulong` in C#, but a
 * number past 2^53 would arrive here already rounded, and silently simulating a
 * different seed than the one typed is worse than refusing it.
 */
const PARAMS = {
  seed: { fallback: 42, min: 0, max: Number.MAX_SAFE_INTEGER },
  years: { fallback: 300, min: 1, max: 5000 },
  civs: { fallback: 8, min: 1, max: 64 },
};

/** Lines of CLI output kept per run. Enough for the summary, bounded against a runaway build log. */
const LOG_LINES = 200;

/** Finished runs kept for inspection before the oldest are dropped. */
const RUN_HISTORY = 20;

/**
 * @typedef {{ seed: number, years: number, civs: number }} RunParams
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
 *   child?: import('node:child_process').ChildProcess,
 * }} Run
 */

/**
 * @returns {import('vite').Plugin}
 */
export function worldGenerator() {
  /** @type {Map<string, Run>} */
  const runs = new Map();

  /** At most one at a time: concurrent `dotnet run` invocations contend over the same obj/bin. */
  /** @type {Run | null} */
  let active = null;

  return {
    name: 'historia:world-generator',

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
          watch: { ignored: [`**/${WORLD_DIR.join('/')}/**`] },
        },
      };
    },

    configureServer(server) {
      const root = path.resolve(server.config.root, '..');

      const worldDir = path.join(root, ...WORLD_DIR);

      server.middlewares.use((req, res, next) => {
        const url = new URL(req.url ?? '/', 'http://localhost');

        if (url.pathname.startsWith(WORLDS)) return serveWorld(url.pathname, req, res, next);
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

        const name = `world-s${params.seed}-y${params.years}-c${params.civs}.json`;
        const output = path.join(...WORLD_DIR, name);

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
        const child = spawn(
          'dotnet',
          [
            'run',
            '--project',
            CLI_PROJECT,
            '--',
            '--seed',
            String(params.seed),
            '--years',
            String(params.years),
            '--civs',
            String(params.civs),
            '--out',
            output,
            '--sample',
            '0',
          ],
          {
            cwd: root,
            stdio: ['ignore', 'pipe', 'pipe'],
            // Its own process group, so cancelling reaches the simulation `dotnet run`
            // starts as a child rather than only the launcher.
            detached: process.platform !== 'win32',
          },
        );

        run.child = child;
        runs.set(run.id, run);
        active = run;
        prune(runs);

        collect(child.stdout, run);
        collect(child.stderr, run);

        child.on('error', (cause) => {
          finish(run, 'failed', `could not run dotnet: ${message(cause)}`);
        });

        child.on('close', async (code) => {
          if (run.status !== 'running') return; // Already cancelled.

          if (code !== 0) {
            return finish(run, 'failed', `the generator exited with code ${code}`);
          }

          try {
            run.bytes = (await stat(path.join(root, output))).size;
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

  return {
    seed: readNumber('seed', given.seed),
    years: readNumber('years', given.years),
    civs: readNumber('civs', given.civs),
  };
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
      if (text.length > 0) run.log.push(text);
    }

    if (run.log.length > LOG_LINES) run.log.splice(0, run.log.length - LOG_LINES);
  });

  stream.on('end', () => {
    const text = pending.trimEnd();
    if (text.length > 0) run.log.push(text);
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
