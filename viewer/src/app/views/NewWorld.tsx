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
  type RunParams,
} from '../generate';
import { loadWorld, type World } from '../store';
import { Panel } from '../components/common';

/**
 * Run a seed and open what comes out.
 *
 * The loop this replaces was: leave the viewer, run the CLI in another terminal, come
 * back, reload, discover the seed was dull, repeat. Here the finished export is fetched
 * and swapped into the running app, so trying ten seeds costs ten clicks and never
 * reloads the page.
 *
 * Development only — `CAN_GENERATE` guards every call site, and the endpoint behind it
 * does not exist in a built viewer.
 */
export function NewWorld({ onLoaded }: { onLoaded: (world: World, url: string) => void }) {
  const [form, setForm] = useState<Record<keyof RunParams, string>>({
    seed: String(DEFAULT_PARAMS.seed),
    years: String(DEFAULT_PARAMS.years),
    civs: String(DEFAULT_PARAMS.civs),
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

  // Then read the file it wrote. Parsing and indexing megabytes of chronicle is the slow
  // half of "generate", so it gets its own visible step rather than looking like a hang.
  useEffect(() => {
    if (run?.status !== 'done' || !run.world) return;

    const url = run.world;
    let stopped = false;

    setOpening(true);
    loadWorld(url)
      .then((world) => {
        if (!stopped) onLoaded(world, url);
      })
      .catch((cause: unknown) => {
        if (stopped) return;
        setError(messageOf(cause));
        setOpening(false);
      });

    return () => {
      stopped = true;
    };
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
          label="Civilizations"
          value={form.civs}
          bounds={BOUNDS.civs}
          width="w-24"
          disabled={busy}
          onChange={(civs) => setForm({ ...form, civs })}
        />

        <button
          type="submit"
          disabled={busy}
          className="mb-0.5 rounded border border-[var(--accent)] bg-[var(--accent-soft)] px-3 py-1.5 text-sm font-medium text-[var(--accent)] transition-opacity disabled:opacity-40"
        >
          {busy ? 'Working…' : 'Generate'}
        </button>
      </form>

      <p className="mt-3 text-xs text-[var(--ink-faint)]">
        Runs the engine and opens the result here. Identical settings always produce an
        identical history, so a seed worth keeping is worth writing down — the file lands in{' '}
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
      ? `Simulating ${run.params.years} years, seed ${run.params.seed} — ${seconds}s`
      : run.status === 'done'
        ? opening
          ? `Reading ${megabytes(run.bytes)} of chronicle…`
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
  width,
  disabled,
  onChange,
}: {
  label: string;
  value: string;
  bounds: { min: number; max: number };
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
        step={1}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
        className={`mt-1 rounded border border-[var(--rule)] bg-[var(--page)] px-2.5 py-1.5 text-sm tabular-nums outline-none focus:border-[var(--accent)] disabled:opacity-50 ${width}`}
      />
    </label>
  );
}

function megabytes(bytes: number | undefined): string {
  return bytes === undefined ? 'the export' : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function messageOf(cause: unknown): string {
  return cause instanceof Error ? cause.message : String(cause);
}
