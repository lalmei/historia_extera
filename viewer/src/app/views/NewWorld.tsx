import type React from 'react';
import { useEffect, useState } from 'react';
import {
  BOUNDS,
  DEFAULT_PARAMS,
  POLL_MS,
  cancelRun,
  randomSeed,
  readRun,
  startRun,
  type Run,
} from '../generate';
import { Panel } from '../components/common';

/**
 * Run a seed and open what comes out.
 *
 * The loop this replaces was: leave the viewer, run the CLI in another terminal, come
 * back, reload, discover the seed was dull, repeat.
 *
 * This is the only interactive part of `/new`, so it is the only part that ships as an
 * island — the page around it is static Astro. When a run finishes it hands the world to
 * the viewer as a `?world=` link rather than parsing it here: the viewer is the thing
 * that knows how to read a chronicle, and a generator that also held one in memory would
 * be parsing megabytes it is about to navigate away from.
 *
 * Development only — the endpoint behind it does not exist in a built viewer.
 */
export function NewWorld() {
  const [form, setForm] = useState({
    seed: String(DEFAULT_PARAMS.seed),
    years: String(DEFAULT_PARAMS.years),
    civs: String(DEFAULT_PARAMS.civs),
    size: String(DEFAULT_PARAMS.size),
    eastWestPeriodic: DEFAULT_PARAMS.eastWestPeriodic,
  });

  const [run, setRun] = useState<Run | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [opening, setOpening] = useState(false);

  const busy = run?.status === 'running' || opening;

  // Poll while the CLI is working. Keyed on id and status rather than the run object, so
  // an answer that only moved the clock forward does not tear the timer down and rebuild it.
  useEffect(() => {
    if (run?.status !== 'running') return;

    const id = run.id;
    let stopped = false;

    const timer = setInterval(async () => {
      try {
        const next = await readRun(id);
        if (!stopped) setRun(next);
      } catch (cause) {
        if (stopped) return;
        setError(messageOf(cause));
        setRun(null);
      }
    }, POLL_MS);

    return () => {
      stopped = true;
      clearInterval(timer);
    };
  }, [run?.id, run?.status]);

  // Then leave for the viewer, pointed at what was just written. `?world=` before the
  // hash is the form that survives navigation, so the world stays selected from here on.
  useEffect(() => {
    if (run?.status !== 'done' || !run.world) return;

    setOpening(true);
    window.location.assign(viewerUrl(run.world));
  }, [run?.status, run?.world]);

  async function submit(event: React.SyntheticEvent) {
    event.preventDefault();
    setError(null);

    try {
      setRun(
        await startRun({
          seed: Number(form.seed),
          years: Number(form.years),
          civs: Number(form.civs),
          size: Number(form.size),
          eastWestPeriodic: form.eastWestPeriodic,
        }),
      );
    } catch (cause) {
      setError(messageOf(cause));
    }
  }

  async function abandon() {
    if (!run) return;

    try {
      setRun(await cancelRun(run.id));
    } catch (cause) {
      setError(messageOf(cause));
    }
  }

  return (
    <Panel title="Simulate a world">
      <form onSubmit={submit} className="flex flex-wrap items-end gap-3">
        <NumberField
          label="Seed"
          value={form.seed}
          bounds={BOUNDS.seed}
          width="w-36"
          disabled={busy}
          onChange={(seed) => setForm({ ...form, seed })}
        />

        <button
          type="button"
          onClick={() => setForm({ ...form, seed: String(randomSeed()) })}
          disabled={busy}
          title="Pick a seed at random"
          className="mb-0.5 rounded border border-[var(--rule)] px-2 py-1.5 text-sm text-[var(--ink-soft)] transition-colors hover:border-[var(--accent)] hover:text-[var(--accent)] disabled:opacity-40"
        >
          Random
        </button>

        <NumberField
          label="Years"
          value={form.years}
          bounds={BOUNDS.years}
          width="w-24"
          disabled={busy}
          onChange={(years) => setForm({ ...form, years })}
        />

        <NumberField
          label="World size"
          value={form.size}
          bounds={BOUNDS.size}
          step={256}
          width="w-28"
          disabled={busy}
          onChange={(size) => setForm({ ...form, size })}
        />

        <NumberField
          label="Civilizations"
          value={form.civs}
          bounds={BOUNDS.civs}
          width="w-24"
          disabled={busy}
          onChange={(civs) => setForm({ ...form, civs })}
        />

        <label className="mb-0.5 flex items-center gap-2 rounded border border-[var(--rule)] px-2.5 py-1.5 text-sm text-[var(--ink-soft)]">
          <input
            type="checkbox"
            checked={form.eastWestPeriodic}
            disabled={busy}
            onChange={(event) =>
              setForm({ ...form, eastWestPeriodic: event.target.checked })
            }
          />
          Join east/west edges
        </label>

        <button
          type="submit"
          disabled={busy}
          className="mb-0.5 rounded border border-[var(--accent)] bg-[var(--accent-soft)] px-3 py-1.5 text-sm font-medium text-[var(--accent)] transition-opacity disabled:opacity-40"
        >
          {busy ? 'Working…' : 'Generate'}
        </button>
      </form>

      <p className="mt-3 text-xs text-[var(--ink-faint)]">
        A seed worth keeping is worth writing down: the same settings always give the same
        history. A periodic world wraps terrain, rivers, travel, and expansion across its east and
        west edges. Each run lands in{' '}
        <code className="rounded bg-[var(--page)] px-1 py-0.5">viewer/public/worlds/</code> and can
        be reopened later with <code className="rounded bg-[var(--page)] px-1 py-0.5">?world=</code>.
      </p>

      {error && (
        <p className="mt-3 rounded border border-[var(--accent)] bg-[var(--accent-soft)] px-3 py-2 text-sm text-[var(--accent)]">
          {error}
        </p>
      )}

      {run && <Progress run={run} opening={opening} onCancel={abandon} />}
    </Panel>
  );
}

function Progress({
  run,
  opening,
  onCancel,
}: {
  run: Run;
  opening: boolean;
  onCancel: () => void;
}) {
  const seconds = (run.elapsedMs / 1000).toFixed(1);

  const status =
    run.status === 'running'
      ? `Simulating ${run.params.years} years on a ${run.params.size}×${run.params.size}${
          run.params.eastWestPeriodic ? ' periodic' : ''
        } world, seed ${run.params.seed} — ${seconds}s`
      : run.status === 'done'
        ? opening
          ? `Done in ${seconds}s — opening ${megabytes(run.bytes)}…`
          : `Done in ${seconds}s`
        : run.status === 'cancelled'
          ? 'Cancelled'
          : (run.error ?? 'The generator failed');

  return (
    <div className="mt-4 border-t border-[var(--rule)] pt-3">
      <div className="flex flex-wrap items-center gap-3">
        <span className="text-sm tabular-nums">{status}</span>

        {run.status === 'running' && (
          <button
            type="button"
            onClick={onCancel}
            className="text-xs text-[var(--ink-faint)] underline hover:text-[var(--accent)]"
          >
            cancel
          </button>
        )}
      </div>

      {/* The CLI's own summary, which is the answer to "was that a world worth looking at" —
          how many realms stood, how many fell, how long it took. Newest lines at the bottom,
          scrolled to, so a long build log does not push the summary out of view. */}
      {run.log.length > 0 && (
        <pre
          ref={(node) => {
            if (node) node.scrollTop = node.scrollHeight;
          }}
          className="mt-2 max-h-52 overflow-auto rounded border border-[var(--rule)] bg-[var(--page)] p-3 font-mono text-xs leading-relaxed text-[var(--ink-soft)]"
        >
          {run.log.join('\n')}
        </pre>
      )}
    </div>
  );
}

function NumberField({
  label,
  value,
  bounds,
  step = 1,
  width,
  disabled,
  onChange,
}: {
  label: string;
  value: string;
  bounds: { min: number; max: number };
  step?: number;
  width: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <label className="block">
      <span className="block text-[0.7rem] font-medium tracking-wide uppercase text-[var(--ink-faint)]">
        {label}
      </span>
      <input
        type="number"
        required
        min={bounds.min}
        max={bounds.max}
        step={step}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
        className={`mt-1 rounded border border-[var(--rule)] bg-[var(--page)] px-2.5 py-1.5 text-sm tabular-nums outline-none focus:border-[var(--accent)] disabled:opacity-50 ${width}`}
      />
    </label>
  );
}

/** The viewer, showing one particular export. `BASE_URL` always ends in a slash. */
function viewerUrl(world: string): string {
  return `${import.meta.env.BASE_URL}?world=${world}`;
}

function megabytes(bytes: number | undefined): string {
  return bytes === undefined ? 'the export' : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function messageOf(cause: unknown): string {
  return cause instanceof Error ? cause.message : String(cause);
}
