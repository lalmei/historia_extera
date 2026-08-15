import type React from 'react';
import { useEffect, useState } from 'react';
import {
  BOUNDS,
  DEFAULT_PARAMS,
  POLL_MS,
  cancelRun,
  minimumSizeFor,
  paramsFromSearch,
  randomSeed,
  readRun,
  startRun,
  suggestedContinueYears,
  worldFileName,
  type Run,
  type RunParams,
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
 * The same form is mounted on a world's Overview, prefilled from that export, so a seed
 * already on screen can be pushed through more years, different knobs, or the engine as
 * it stands now without walking back to `/new`.
 *
 * Development only — the endpoint behind it does not exist in a built viewer.
 */
export function NewWorld({
  initial,
  sourceLabel,
  title = 'Simulate a world',
  showContinue = false,
}: {
  initial?: RunParams;
  sourceLabel?: string;
  title?: string;
  showContinue?: boolean;
} = {}) {
  const [form, setForm] = useState(() => toForm(initial ?? DEFAULT_PARAMS));
  const [baseline, setBaseline] = useState(initial ?? DEFAULT_PARAMS);
  const [source, setSource] = useState(sourceLabel);
  const [continueYears, setContinueYears] = useState(() =>
    String(suggestedContinueYears((initial ?? DEFAULT_PARAMS).years)),
  );
  const [run, setRun] = useState<Run | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [opening, setOpening] = useState(false);

  const busy = run?.status === 'running' || opening;
  const params = fromForm(form);
  const tooSmall = params.size < minimumSizeFor(params.civs);
  const continuing = showContinue || Boolean(source);

  // `/new?seed=` is how the world list hands a previous export to this form. Read it
  // after mount: the island is prerendered, and `window` is not there yet.
  useEffect(() => {
    if (initial) {
      setBaseline(initial);
      setForm(toForm(initial));
      setContinueYears(String(suggestedContinueYears(initial.years)));
      setSource(sourceLabel);
      return;
    }

    const search = paramsFromSearch(window.location.search);
    const from = new URLSearchParams(window.location.search).get('from') ?? undefined;
    if (search) {
      setBaseline(search);
      setForm(toForm(search));
      setContinueYears(String(suggestedContinueYears(search.years)));
    }
    if (from) setSource(from);
  }, [initial, sourceLabel]);

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
  // Regenerating the file already on screen has to reload even when the URL is unchanged,
  // or the viewer keeps the chronicle it loaded at first paint.
  useEffect(() => {
    if (run?.status !== 'done' || !run.world) return;

    setOpening(true);
    const next = viewerUrl(run.world);
    const here = `${window.location.pathname}${window.location.search}`;
    const sameWorld = here === next || window.location.search === `?world=${run.world}`;

    if (sameWorld) window.location.reload();
    else window.location.assign(next);
  }, [run?.status, run?.world]);

  async function generate(event?: React.SyntheticEvent, override?: Partial<RunParams>) {
    event?.preventDefault();
    setError(null);

    const next: RunParams = { ...params, ...override };

    if (next.size < minimumSizeFor(next.civs)) {
      setError(
        `A ${next.size}-unit world has too little room to seat ${next.civs} civilizations.`,
      );
      return;
    }

    const output = worldFileName(next);
    if (source === output) {
      const confirmed = window.confirm(
        `Replace ${output}?\n\nThe file you are reusing will be overwritten with a new run of these settings through the current engine.`,
      );
      if (!confirmed) return;
    }

    try {
      setRun(await startRun(next));
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

  const continueTarget = Number(continueYears);
  const continueReady =
    Number.isSafeInteger(continueTarget) &&
    continueTarget > baseline.years &&
    continueTarget <= BOUNDS.years.max;
  const unchanged =
    params.seed === baseline.seed &&
    params.years === baseline.years &&
    params.civs === baseline.civs &&
    params.size === baseline.size &&
    params.eastWestPeriodic === baseline.eastWestPeriodic;
  const submitLabel = busy
    ? 'Working…'
    : source && unchanged
      ? 'Run with current engine'
      : 'Generate';
  const canContinue = continuing && baseline.years < BOUNDS.years.max;

  return (
    <Panel title={title}>
      <form id="simulate" onSubmit={(event) => generate(event)} className="flex flex-wrap items-end gap-3">
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
          disabled={busy || tooSmall}
          className="mb-0.5 rounded border border-[var(--accent)] bg-[var(--accent-soft)] px-3 py-1.5 text-sm font-medium text-[var(--accent)] transition-opacity disabled:opacity-40"
        >
          {submitLabel}
        </button>
      </form>

      {/* Said before the run rather than after it: a world with no room for its civilizations
          simulates happily and produces an empty chronicle, which reads as a broken engine. */}
      {tooSmall && (
        <p className="mt-3 text-xs text-[var(--accent)]">
          A {form.size}-unit world has too little room to seat {form.civs} civilizations. Raise the
          world size to at least {minimumSizeFor(Number(form.civs))}, or ask for fewer.
        </p>
      )}

      {source && (
        <p className="mt-3 text-xs text-[var(--ink-soft)]">
          Settings taken from <code className="rounded bg-[var(--page)] px-1 py-0.5">{source}</code>.
          Change a number and generate, or run them unchanged through the engine as it stands now.
          The first {baseline.years.toLocaleString()} years of a longer run are the same history —
          the seed is deterministic.
        </p>
      )}

      {canContinue && (
        <form
          onSubmit={(event) => generate(event, { ...baseline, years: continueTarget })}
          className="mt-4 flex flex-wrap items-end gap-3 border-t border-[var(--rule)] pt-3"
        >
          <NumberField
            label="Continue through year"
            value={continueYears}
            bounds={{ min: baseline.years + 1, max: BOUNDS.years.max }}
            width="w-36"
            disabled={busy}
            onChange={setContinueYears}
          />
          <button
            type="submit"
            disabled={busy || !continueReady}
            className="mb-0.5 rounded border border-[var(--rule)] px-3 py-1.5 text-sm text-[var(--ink-soft)] transition-colors hover:border-[var(--accent)] hover:text-[var(--accent)] disabled:opacity-40"
          >
            Continue
          </button>
          <p className="mb-1 max-w-prose text-xs text-[var(--ink-faint)]">
            Keeps seed {baseline.seed}, {baseline.civs} civilizations and a {baseline.size}-unit
            world. Writes a new file; the {baseline.years}-year export stays on disk.
          </p>
        </form>
      )}

      {!source && (
        <p className="mt-3 text-xs text-[var(--ink-faint)]">
          A seed worth keeping is worth writing down: the same settings always give the same
          history. A periodic world wraps terrain, rivers, travel, and expansion across its east and
          west edges. Each run lands in{' '}
          <code className="rounded bg-[var(--page)] px-1 py-0.5">viewer/public/worlds/</code> and can
          be reopened later with <code className="rounded bg-[var(--page)] px-1 py-0.5">?world=</code>
          . Reusing a previous export from the list below fills these numbers so it can be run
          again, stretched, or pushed through a newer engine.
        </p>
      )}

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

function toForm(params: RunParams) {
  return {
    seed: String(params.seed),
    years: String(params.years),
    civs: String(params.civs),
    size: String(params.size),
    eastWestPeriodic: params.eastWestPeriodic,
  };
}

function fromForm(form: ReturnType<typeof toForm>): RunParams {
  return {
    seed: Number(form.seed),
    years: Number(form.years),
    civs: Number(form.civs),
    size: Number(form.size),
    eastWestPeriodic: form.eastWestPeriodic,
  };
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
