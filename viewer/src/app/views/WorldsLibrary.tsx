import { useEffect, useState } from 'react';
import { MIN_SCHEMA_VERSION } from '../compat';
import { generateHref, SiteShell } from '../components/SiteChrome';
import { SCHEMA_VERSION } from '../types';
import { WorldList } from './WorldList';
import { useLanguage } from '../i18n/LanguageProvider';
import { translateIn } from '../i18n/translations.ts';

/**
 * Catalog of generated exports: pick one to read, rerun, or throw away.
 *
 * Shown as the viewer home under `astro dev` when no `?world=` is selected.
 * A built viewer has no catalog endpoint, so this page does not ship.
 */
export function WorldsLibrary() {
  const [query, setQuery] = useState('');
  const { language } = useLanguage();
  const tr = (key: string, values?: Readonly<Record<string, string | number | undefined>>) =>
    translateIn(language, 'library', key, values);

  useEffect(() => {
    const previous = document.title;
    document.title = `${tr('title')} — Historia Extera`;
    return () => {
      document.title = previous;
    };
  }, [language]);

  return (
    <SiteShell active="worlds" search={{ value: query, onChange: setQuery }}>
      <div className="mb-8 flex flex-wrap items-start justify-between gap-4">
        <div className="max-w-2xl">
          <h1 className="he-headline">{tr('title')}</h1>
          <p className="mt-2 text-[var(--ink-soft)]">
            {tr('description')}
          </p>
          <p className="mt-3 text-sm text-[var(--ink-faint)]">
            {tr('compatibility', {
              min: MIN_SCHEMA_VERSION,
              max: SCHEMA_VERSION,
            })}
          </p>
        </div>
        <a
          href={generateHref()}
          className="he-btn-primary px-4 py-2.5 text-sm font-semibold tracking-wide uppercase"
        >
          + {tr('generate')}
        </a>
      </div>

      <WorldList query={query} />
    </SiteShell>
  );
}
