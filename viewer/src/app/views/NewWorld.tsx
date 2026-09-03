import type React from 'react';
import { useEffect, useRef, useState } from 'react';
import { IconClock, IconClose, IconKey, IconPeople, IconSettings, IconShuffle, IconTerminal } from '../components/icons';
import {
  BOUNDS,
  DEFAULT_PARAMS,
  POLL_MS,
  SIZE_STEP,
  SIZE_TIERS,
  alignSize,
  cancelRun,
  minimumSizeFor,
  paramsFromSearch,
  randomSeed,
  readRun,
  sizeTierOf,
  startRun,
  suggestedContinueYears,
  worldFileName,
  type Run,
  type RunParams,
  type SizeTier,
} from '../generate';

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
export function NewWorld({
  initial,
  sourceLabel,
  title,
  showContinue = false,
}: {
  initial?: RunParams;
  sourceLabel?: string;
  title?: string;
  showContinue?: boolean;
} = {}) {
  const [form, setForm] = useState(() =>
    toForm(initial ?? { ...DEFAULT_PARAMS, civs: 5, eastWestPeriodic: true }),
  );
  const [baseline, setBaseline] = useState(initial ?? DEFAULT_PARAMS);
  const [source, setSource] = useState(sourceLabel);
  const [continueYears, setContinueYears] = useState(() =>
    String(suggestedContinueYears((initial ?? DEFAULT_PARAMS).years)),
  );
  const [run, setRun] = useState<Run | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [opening, setOpening] = useState(false);

  const synthesizing = run !== null && run.status !== 'cancelled';
  const busy = run?.status === 'running' || opening;
  const params = fromForm(form);
  const seedValid = Number.isSafeInteger(params.seed) && params.seed >= BOUNDS.seed.min;
  const tooSmall = params.size < minimumSizeFor(params.civs);
  const continuing = showContinue || Boolean(source);
  const selectedTier = sizeTierOf(params.size);

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
    } else {
      const fresh = freshGenerateParams();
      setBaseline(fresh);
      setForm(toForm(fresh));
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

  useEffect(() => {
    if (run?.status !== 'running') return;

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') void abandon();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [run?.status, run?.id]);

  useEffect(() => {
    if (!synthesizing) return;
    const previous = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = previous;
    };
  }, [synthesizing]);

  async function generate(event?: React.SyntheticEvent, override?: Partial<RunParams>) {
    event?.preventDefault();
    setError(null);
    setNotice(null);

    const next: RunParams = { ...params, ...override };
    if (Number.isFinite(next.size)) next.size = alignSize(next.size);

    if (!Number.isSafeInteger(next.seed) || next.seed < BOUNDS.seed.min) {
      setError('The seed has to be a whole number, or hex like 0x7F8A9B2C.');
      return;
    }

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
      await cancelRun(run.id);
    } catch (cause) {
      setError(messageOf(cause));
    } finally {
      setRun(null);
      setOpening(false);
      setNotice('Synthesis aborted.');
    }
  }

  function dismissFailed() {
    setError(run?.error ?? 'The generator failed');
    setRun(null);
    setOpening(false);
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
  const submitLabel =
    source && unchanged ? 'Run with current engine' : 'Initialize simulation engine';
  const canContinue = continuing && baseline.years < BOUNDS.years.max;

  return (
    <div>
      {title && <h2 className="he-label mb-3">{title}</h2>}

      <form id="simulate" onSubmit={(event) => generate(event)}>
        <div className="space-y-5 rounded-lg border border-[var(--rule)] bg-[var(--panel)] p-5 md:p-6">
          <Field label="Generation seed">
            <div className="relative">
              <span className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-[var(--ink-faint)]">
                <IconKey className="h-4 w-4" />
              </span>
              <input
                type="text"
                spellCheck={false}
                required
                aria-label="Generation seed"
                value={form.seed}
                disabled={busy}
                onChange={(event) => setForm({ ...form, seed: event.target.value })}
                onBlur={() => {
                  if (seedValid) setForm({ ...form, seed: formatSeed(params.seed) });
                }}
                className="he-data w-full rounded-md border border-[var(--rule)] bg-[var(--input)] py-2.5 pr-11 pl-10 text-sm outline-none focus:border-[var(--primary)] disabled:opacity-50"
              />
              <button
                type="button"
                onClick={() => setForm({ ...form, seed: formatSeed(randomSeed()) })}
                disabled={busy}
                title="Pick a seed at random"
                aria-label="Pick a seed at random"
                className="absolute top-1/2 right-2 -translate-y-1/2 rounded p-1.5 text-[var(--ink-faint)] transition-colors hover:text-[var(--primary)] disabled:opacity-40"
              >
                <IconShuffle className="h-4 w-4" />
              </button>
            </div>
          </Field>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Simulation duration (years)">
              <IconNumber
                icon={<IconClock className="h-4 w-4" />}
                value={form.years}
                bounds={BOUNDS.years}
                disabled={busy}
                ariaLabel="Simulation duration in years"
                onChange={(years) => setForm({ ...form, years })}
              />
            </Field>
            <Field label="Civilization count">
              <IconNumber
                icon={<IconPeople className="h-4 w-4" />}
                value={form.civs}
                bounds={BOUNDS.civs}
                disabled={busy}
                ariaLabel="Civilization count"
                onChange={(civs) => setForm({ ...form, civs })}
              />
            </Field>
          </div>

          <Field label="World dimensionality">
            <div className="grid grid-cols-3 gap-1 rounded-md border border-[var(--rule)] bg-[var(--input)] p-1">
              {(Object.keys(SIZE_TIERS) as SizeTier[]).map((tier) => {
                const selected = selectedTier === tier;
                return (
                  <button
                    key={tier}
                    type="button"
                    disabled={busy}
                    onClick={() => setForm({ ...form, size: String(SIZE_TIERS[tier]) })}
                    className={`rounded px-2 py-2 text-xs font-semibold tracking-wide uppercase transition-colors disabled:opacity-40 ${
                      selected
                        ? 'bg-[var(--primary)] text-[var(--on-primary)]'
                        : 'text-[var(--ink-soft)] hover:text-[var(--ink)]'
                    }`}
                    aria-pressed={selected}
                  >
                    {tier}
                  </button>
                );
              })}
            </div>
            <div className="relative mt-3">
              <input
                type="number"
                required
                min={BOUNDS.size.min}
                max={BOUNDS.size.max}
                step={SIZE_STEP}
                value={form.size}
                disabled={busy}
                aria-label="World size in units"
                onChange={(event) => setForm({ ...form, size: event.target.value })}
                onBlur={() => {
                  if (Number.isFinite(params.size)) {
                    setForm({ ...form, size: String(alignSize(params.size)) });
                  }
                }}
                className="he-data w-full rounded-md border border-[var(--rule)] bg-[var(--input)] py-2.5 pr-16 pl-3 text-sm tabular-nums outline-none focus:border-[var(--primary)] disabled:opacity-50"
              />
              <span className="pointer-events-none absolute top-1/2 right-3 -translate-y-1/2 text-xs text-[var(--ink-faint)]">
                units
              </span>
            </div>
            <p className="mt-2 text-xs text-[var(--ink-faint)]">
              Multiples of {SIZE_STEP}, from {BOUNDS.size.min.toLocaleString()} to{' '}
              {BOUNDS.size.max.toLocaleString()}.
            </p>
          </Field>

          <div className="flex items-center justify-between gap-4 border-t border-[var(--rule)] pt-5">
            <div>
              <div className="text-sm font-medium">Planetary wrapping</div>
              <p className="mt-0.5 text-xs text-[var(--ink-faint)]">
                Connect east/west edges seamlessly
              </p>
            </div>
            <button
              type="button"
              role="switch"
              aria-checked={form.eastWestPeriodic}
              disabled={busy}
              onClick={() => setForm({ ...form, eastWestPeriodic: !form.eastWestPeriodic })}
              className={`relative h-6 w-11 shrink-0 rounded-full transition-colors disabled:opacity-40 ${
                form.eastWestPeriodic ? 'bg-[var(--primary)]' : 'bg-[var(--surface-container-highest)]'
              }`}
              aria-label="Planetary wrapping"
            >
              <span
                className={`absolute top-0.5 left-0.5 h-5 w-5 rounded-full transition-transform ${
                  form.eastWestPeriodic
                    ? 'translate-x-5 bg-[var(--on-primary)]'
                    : 'bg-[var(--on-surface)]'
                }`}
              />
            </button>
          </div>
        </div>

        {tooSmall && (
          <p className="mt-3 text-xs text-[var(--accent)]">
            A {form.size}-unit world has too little room to seat {form.civs} civilizations. Raise the
            world size to at least {minimumSizeFor(Number(form.civs))}, or ask for fewer.
          </p>
        )}

        {source && (
          <p className="mt-3 text-xs text-[var(--ink-soft)]">
            Settings taken from <code className="rounded bg-[var(--input)] px-1 py-0.5">{source}</code>.
            Change a number and generate, or run them unchanged through the engine as it stands now.
            The first {baseline.years.toLocaleString()} years of a longer run are the same history —
            the seed is deterministic.
          </p>
        )}

        {notice && !error && (
          <p aria-live="polite" className="mt-3 text-sm text-[var(--ink-soft)]">
            {notice}
          </p>
        )}

        {error && (
          <p className="mt-3 rounded border border-[var(--error)] px-3 py-2 text-sm text-[var(--error)]">
            {error}
          </p>
        )}

        <button
          type="submit"
          disabled={busy || tooSmall || !seedValid}
          className="he-btn-primary mt-4 flex w-full items-center justify-center gap-2 px-4 py-3 text-sm font-semibold tracking-wide uppercase disabled:opacity-40"
        >
          <IconSettings className="h-4 w-4" />
          {submitLabel}
        </button>
      </form>

      {canContinue && (
        <form
          onSubmit={(event) => generate(event, { ...baseline, years: continueTarget })}
          className="mt-4 rounded-lg border border-[var(--rule)] bg-[var(--panel)] p-5"
        >
          <Field label="Continue through year">
            <div className="flex flex-wrap items-center gap-3">
              <IconNumber
                icon={<IconClock className="h-4 w-4" />}
                value={continueYears}
                bounds={{ min: baseline.years + 1, max: BOUNDS.years.max }}
                disabled={busy}
                ariaLabel="Continue through year"
                onChange={setContinueYears}
              />
              <button
                type="submit"
                disabled={busy || !continueReady}
                className="he-btn-secondary px-3 py-2.5 text-sm disabled:opacity-40"
              >
                Continue
              </button>
            </div>
          </Field>
          <p className="mt-2 text-xs text-[var(--ink-faint)]">
            Keeps seed {formatSeed(baseline.seed)}, {baseline.civs} civilizations and a{' '}
            {baseline.size}-unit world. Writes a new file; the {baseline.years}-year export stays on
            disk.
          </p>
        </form>
      )}

      {synthesizing && run && (
        <SynthesisModal
          run={run}
          opening={opening}
          onAbort={abandon}
          onDismiss={dismissFailed}
        />
      )}
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="he-label">{label}</div>
      <div className="mt-2">{children}</div>
    </div>
  );
}

function IconNumber({
  icon,
  value,
  bounds,
  disabled,
  ariaLabel,
  onChange,
}: {
  icon: React.ReactNode;
  value: string;
  bounds: { min: number; max: number };
  disabled: boolean;
  ariaLabel: string;
  onChange: (value: string) => void;
}) {
  return (
    <div className="relative">
      <span className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-[var(--ink-faint)]">
        {icon}
      </span>
      <input
        type="number"
        required
        min={bounds.min}
        max={bounds.max}
        step={1}
        value={value}
        disabled={disabled}
        aria-label={ariaLabel}
        onChange={(event) => onChange(event.target.value)}
        className="he-data w-full rounded-md border border-[var(--rule)] bg-[var(--input)] py-2.5 pr-3 pl-10 text-sm tabular-nums outline-none focus:border-[var(--primary)] disabled:opacity-50"
      />
    </div>
  );
}

/**
 * The engine at work: year, clock, progress and the CLI's own output.
 *
 * Exported because the Worlds Library runs seeds too — its Run button starts one directly
 * rather than sending the reader to this form — and a second progress dialog that drifted
 * from this one would be two answers to "is it still going?".
 */
export function SynthesisModal({
  run,
  opening,
  onAbort,
  onDismiss,
}: {
  run: Run;
  opening: boolean;
  onAbort: () => void;
  onDismiss: () => void;
}) {
  const failed = run.status === 'failed';
  const done = run.status === 'done' || opening;
  const endYear = run.endYear ?? run.params.years;
  const year = done ? endYear : (run.year ?? 0);
  const percent = done ? 100 : Math.min(100, Math.max(year === 0 ? 4 : (year / Math.max(endYear, 1)) * 100, 0));
  const stamps = useLogStamps(run.log, run.elapsedMs);

  const title = failed
    ? 'World synthesis failed'
    : done
      ? 'World synthesis complete'
      : 'World synthesis initiated';

  const subtitle = failed
    ? (run.error ?? 'The generator failed.')
    : done
      ? `Opening the chronicle for seed #${run.params.seed}.`
      : `The procedural engine is currently generating tectonic structures, atmospheric conditions, and initial biological parameters for seed #${run.params.seed}.`;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-[color-mix(in_srgb,var(--canvas)_82%,black)] p-4">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="synthesis-title"
        className="w-full max-w-xl"
      >
        <h2 id="synthesis-title" className="he-headline text-[var(--primary)]">
          {title}
        </h2>
        <p className="mt-3 text-sm text-[var(--ink)]">{subtitle}</p>

        <div className="mt-8 rounded-lg border border-[var(--rule)] bg-[var(--panel)] p-5">
          <div className="flex items-start justify-between gap-6">
            <div>
              <div className="he-label">Simulation year</div>
              <div className="he-data mt-1 text-2xl text-[var(--ink)]">
                {year.toLocaleString()} / {endYear.toLocaleString()}
              </div>
            </div>
            <div className="text-right">
              <div className="he-label">Elapsed time</div>
              <div className="he-data mt-1 text-2xl text-[var(--ink)]">
                {formatElapsed(run.elapsedMs)}
              </div>
            </div>
          </div>

          <div className="mt-5 h-2 overflow-hidden rounded-full bg-[var(--input)]">
            <div
              className="h-full rounded-full bg-[var(--primary)] transition-[width] duration-300"
              style={{ width: `${percent}%` }}
            />
          </div>
          <div className="mt-2 flex justify-between text-[11px] font-semibold tracking-wide text-[var(--ink-faint)] uppercase">
            <span>Epoch 1: Genesis</span>
            <span>Epoch 2: Civilization</span>
          </div>
        </div>

        <div className="mt-4 rounded-lg border border-[var(--rule)] bg-[var(--panel)] p-5">
          <div className="he-label flex items-center gap-2">
            <IconTerminal className="h-3.5 w-3.5" />
            Live engine log
          </div>
          <div
            ref={(node) => {
              if (node) node.scrollTop = node.scrollHeight;
            }}
            className="mt-3 max-h-56 overflow-auto"
          >
            {run.log.length === 0 ? (
              <div className="he-data flex items-start gap-2 text-[var(--ink-soft)]">
                <span className="he-pip mt-1.5 he-pip-ok" aria-hidden="true" />
                <span>Starting engine…</span>
              </div>
            ) : (
              run.log.map((line, index) => (
                <div key={index} className="he-data flex items-start gap-2 py-0.5 text-[var(--ink-soft)]">
                  <span className="shrink-0 text-[var(--ink-faint)]">[{stamps[index] ?? '00:00:00'}]</span>
                  <span className={`he-pip mt-1.5 ${pipClass(line)}`} aria-hidden="true" />
                  <span className="min-w-0 whitespace-pre-wrap">{line}</span>
                </div>
              ))
            )}
          </div>
        </div>

        <div className="mt-8 flex justify-center">
          {run.status === 'running' ? (
            <button
              type="button"
              onClick={onAbort}
              className="inline-flex items-center gap-2 rounded-md border border-[var(--error)] px-4 py-2 text-sm font-semibold tracking-wide text-[var(--error)] uppercase transition-colors hover:bg-[var(--error-container)]"
            >
              <IconClose className="h-4 w-4" />
              Abort synthesis
            </button>
          ) : failed ? (
            <button
              type="button"
              onClick={onDismiss}
              className="he-btn-secondary px-4 py-2 text-sm font-semibold tracking-wide uppercase"
            >
              Back to configuration
            </button>
          ) : null}
        </div>
      </div>
    </div>
  );
}

function useLogStamps(log: string[], elapsedMs: number): string[] {
  const stamps = useRef<string[]>([]);
  const stamp = formatElapsed(elapsedMs);
  while (stamps.current.length < log.length) stamps.current.push(stamp);
  return stamps.current;
}

function formatElapsed(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;
  return [hours, minutes, seconds].map((part) => String(part).padStart(2, '0')).join(':');
}

function pipClass(line: string): string {
  const text = line.toLowerCase();
  if (/\b(error|failed|exception|fatal)\b/.test(text)) return 'he-pip-error';
  if (/\b(warn|warning|cancelled)\b/.test(text)) return 'he-pip-warn';
  if (/\b(done|wrote|saved)\b/.test(text) || /took shape/.test(text)) return 'he-pip-ok';
  return 'he-pip-note';
}

/** Defaults for a new generate, not a rerun from an existing export. */
function freshGenerateParams(): RunParams {
  return {
    ...DEFAULT_PARAMS,
    seed: randomSeed(),
    civs: 5,
    eastWestPeriodic: true,
  };
}

function toForm(params: RunParams) {
  return {
    seed: formatSeed(params.seed),
    years: String(params.years),
    civs: String(params.civs),
    size: String(params.size),
    eastWestPeriodic: params.eastWestPeriodic,
  };
}

function fromForm(form: ReturnType<typeof toForm>): RunParams {
  return {
    seed: parseSeed(form.seed),
    years: Number(form.years),
    civs: Number(form.civs),
    size: Number(form.size),
    eastWestPeriodic: form.eastWestPeriodic,
  };
}

function formatSeed(value: number): string {
  const hex = value.toString(16).toUpperCase();
  return value <= 0xffffffff ? `0x${hex.padStart(8, '0')}` : `0x${hex}`;
}

function parseSeed(value: string): number {
  const trimmed = value.trim();
  if (/^0x[0-9a-f]+$/i.test(trimmed)) {
    const parsed = Number.parseInt(trimmed.slice(2), 16);
    return Number.isSafeInteger(parsed) ? parsed : Number.NaN;
  }
  const parsed = Number(trimmed);
  return Number.isSafeInteger(parsed) ? parsed : Number.NaN;
}

/** The viewer, showing one particular export. `BASE_URL` always ends in a slash. */
function viewerUrl(world: string): string {
  return `${import.meta.env.BASE_URL}?world=${world}`;
}

function messageOf(cause: unknown): string {
  return cause instanceof Error ? cause.message : String(cause);
}
