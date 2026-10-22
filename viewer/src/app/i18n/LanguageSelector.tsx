import { useId } from 'react';
import { useLanguage } from './LanguageProvider';
import { availableLanguages, isLanguage } from './translations.ts';

export function LanguageSelector() {
  const { language, setLanguage, t } = useLanguage();
  const id = useId();
  const languages = availableLanguages();

  return (
    <label htmlFor={id} className="inline-flex shrink-0 items-center">
      <span className="sr-only">{t('language.label')}</span>
      <select
        id={id}
        value={language}
        title={t('language.label')}
        onChange={(event) => {
          const value = event.target.value;
          if (isLanguage(value)) setLanguage(value);
        }}
        className="max-w-28 rounded border border-[var(--rule)] bg-[var(--input)] px-2 py-1.5 text-sm text-[var(--ink)] outline-none focus:border-[var(--primary)]"
      >
        {languages.map((option) => (
          <option key={option.code} value={option.code} lang={option.code}>
            {option.nativeName}
          </option>
        ))}
      </select>
    </label>
  );
}
