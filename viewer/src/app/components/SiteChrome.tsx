import type React from 'react';
import { IconHelp, IconSearch, IconSettings } from './icons';

/** Matches `WorldExporter.EngineVersion` — the number the footer is for. */
export const ENGINE_VERSION = '0.9.2';

const DOCS = 'https://github.com/lalmei/historia_extera/blob/main/docs/guide/viewer.md';
const CLI = 'https://github.com/lalmei/historia_extera/blob/main/docs/guide/cli.md';
const SUPPORT = 'https://github.com/lalmei/historia_extera/issues';

export function worldsHref(): string {
  return import.meta.env.BASE_URL;
}

export function readingHref(world = 'worlds/world.json'): string {
  return `${import.meta.env.BASE_URL}?world=${encodeURIComponent(world)}#/`;
}

export function generateHref(): string {
  return `${import.meta.env.BASE_URL}new/`;
}

/**
 * Shared chrome: Historia Extera Legends, Worlds / Reading, help.
 * Active nav uses a 2px underline.
 */
export function SiteShell({
  active,
  search,
  readingHref: reading,
  children,
}: {
  active: 'worlds' | 'reading';
  search?: { value: string; onChange: (value: string) => void };
  readingHref?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteNav active={active} search={search} readingHref={reading} />
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-8 md:px-10 md:py-10">{children}</main>
      <SiteFooter />
    </div>
  );
}

export function SiteNav({
  active,
  search,
  readingHref: reading,
  fluid = false,
}: {
  active: 'worlds' | 'reading';
  search?: { value: string; onChange: (value: string) => void };
  readingHref?: string;
  fluid?: boolean;
}) {
  const chronicle = reading ?? readingHref();
  return (
    <header className="shrink-0 border-b border-[var(--rule)]">
      <div
        className={`mx-auto flex h-14 items-center gap-8 px-4 ${
          fluid ? 'max-w-none md:px-6' : 'max-w-6xl md:px-10'
        }`}
      >
        <a href={worldsHref()} className="shrink-0 text-[15px] font-medium tracking-tight">
          Historia Extera Legends
        </a>

        <nav className="flex h-full items-stretch gap-6 text-sm">
          <NavLink href={worldsHref()} active={active === 'worlds'}>
            Worlds
          </NavLink>
          <NavLink href={chronicle} active={active === 'reading'}>
            Reading
          </NavLink>
        </nav>

        <div className="ml-auto flex items-center gap-2">
          {search && (
            <label className="relative">
              <span className="pointer-events-none absolute top-1/2 left-2.5 -translate-y-1/2 text-[var(--ink-faint)]">
                <IconSearch className="h-4 w-4" />
              </span>
              <input
                type="search"
                value={search.value}
                onChange={(event) => search.onChange(event.target.value)}
                placeholder="Search archives..."
                className="w-44 rounded border border-[var(--rule)] bg-[var(--input)] py-1.5 pr-3 pl-8 text-sm outline-none focus:border-[var(--primary)] sm:w-56"
              />
            </label>
          )}

          <button
            type="button"
            disabled
            title="No settings on this page"
            className="inline-flex h-8 w-8 items-center justify-center rounded text-[var(--ink-faint)]"
          >
            <IconSettings className="h-4 w-4" />
          </button>
          <a
            href={DOCS}
            title="Documentation"
            aria-label="Documentation"
            className="inline-flex h-8 w-8 items-center justify-center rounded text-[var(--ink-faint)] transition-colors hover:text-[var(--primary)]"
          >
            <IconHelp className="h-4 w-4" />
          </a>
        </div>
      </div>
    </header>
  );
}

function NavLink({
  href,
  active,
  children,
}: {
  href: string;
  active: boolean;
  children: React.ReactNode;
}) {
  return (
    <a
      href={href}
      className={`inline-flex items-center border-b-2 px-0.5 transition-colors ${
        active
          ? 'border-[var(--primary)] font-medium text-[var(--ink)]'
          : 'border-transparent text-[var(--ink-soft)] hover:text-[var(--primary)]'
      }`}
    >
      {children}
    </a>
  );
}

export function SiteFooter({ fluid = false }: { fluid?: boolean }) {
  return (
    <footer className="mt-auto shrink-0 border-t border-[var(--rule)] bg-[var(--surface-container-low)]">
      <div
        className={`mx-auto flex flex-wrap items-center justify-between gap-3 px-4 py-3 text-sm text-[var(--ink-soft)] ${
          fluid ? 'max-w-none md:px-6' : 'max-w-6xl md:px-10'
        }`}
      >
        <span>Historia Extera Engine v{ENGINE_VERSION}</span>
        <nav className="flex flex-wrap gap-5">
          <a href={DOCS} className="transition-colors hover:text-[var(--primary)]">
            Documentation
          </a>
          <a href={CLI} className="transition-colors hover:text-[var(--primary)]">
            API Reference
          </a>
          <a href={SUPPORT} className="transition-colors hover:text-[var(--primary)]">
            Support
          </a>
        </nav>
      </div>
    </footer>
  );
}
