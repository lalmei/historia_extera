import { useEffect } from 'react';
import { SiteShell } from '../components/SiteChrome';
import { LanguageProvider, useLanguage } from '../i18n/LanguageProvider';
import { translateIn } from '../i18n/translations.ts';
import { NewWorld } from './NewWorld';

/**
 * The generator, on its own page, behind the library's Generate button.
 *
 * Lives at `/new` under `astro dev` only — see `viewer/dev/new.astro`.
 */
export function GeneratePage() {
  return (
    <LanguageProvider>
      <GeneratePageContent />
    </LanguageProvider>
  );
}

function GeneratePageContent() {
  const { language } = useLanguage();
  const tr = (key: string) => translateIn(language, 'generatorPage', key);
  useEffect(() => {
    const previous = document.title;
    document.title = `${tr('title')} — Historia Extera`;
    return () => {
      document.title = previous;
    };
  }, [language]);

  return (
    <SiteShell active="worlds">
      <div className="mx-auto w-full max-w-xl">
        <h1 className="he-headline">{tr('title')}</h1>
        <p className="mt-3 text-[var(--ink-soft)]">
          {tr('description')}
        </p>

        <div className="mt-8">
          <NewWorld />
        </div>
      </div>
    </SiteShell>
  );
}
