import en from './lang/en.json' with { type: 'json' };
import fr from './lang/fr.json' with { type: 'json' };

export type Language = string;
export type TranslationKey = string;

export interface LanguageOption {
  code: Language;
  name: string;
  nativeName: string;
}

interface LanguageFile {
  name: string;
  nativeName: string;
  [namespace: string]: unknown;
}

const bundledLanguages = typeof import.meta.glob === 'function'
  ? import.meta.glob<LanguageFile>('./lang/*.json', {
    eager: true,
    import: 'default',
  })
  : {
    './lang/en.json': en,
    './lang/fr.json': fr,
  };

const languages = Object.fromEntries(
  Object.entries(bundledLanguages).map(([path, file]) => {
    const code = path.split('/').at(-1)!.replace(/\.json$/, '');
    if (!/^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$/.test(code) || !isLanguageFile(file)) {
      throw new Error(`Invalid language catalog: ${path}`);
    }
    return [code, file];
  }),
) as Record<Language, LanguageFile>;

const fallbackLanguage = 'en';

export function availableLanguages(): LanguageOption[] {
  return Object.entries(languages)
    .map(([code, file]) => ({
      code,
      name: file.name,
      nativeName: file.nativeName,
    }))
    .sort((left, right) => left.nativeName.localeCompare(right.nativeName));
}

function isLanguageFile(value: unknown): value is LanguageFile {
  if (typeof value !== 'object' || value === null) return false;
  const file = value as Record<string, unknown>;
  if (typeof file.name !== 'string' || typeof file.nativeName !== 'string') return false;
  return Object.entries(file)
    .filter(([key]) => key !== 'name' && key !== 'nativeName')
    .every(([, translations]) => isTranslationTree(translations));
}

function isTranslationTree(value: unknown): boolean {
  if (typeof value === 'string') return true;
  if (Array.isArray(value)) return value.every(isTranslationTree);
  if (typeof value !== 'object' || value === null) return false;
  return Object.values(value).every(isTranslationTree);
}

export function isLanguage(value: unknown): value is Language {
  return typeof value === 'string' && Object.prototype.hasOwnProperty.call(languages, value);
}

export function translate(
  language: Language,
  key: TranslationKey,
  values?: Readonly<Record<string, string | number | undefined>>,
): string {
  return interpolate(translateIn(language, 'ui', key), values);
}

export function translateIn(
  language: Language | undefined,
  namespace: string,
  key: string,
  values?: Readonly<Record<string, string | number | undefined>>,
): string {
  const localized = lookup(languages[language ?? fallbackLanguage]?.[namespace], key);
  if (localized !== undefined) return interpolate(localized, values);

  const fallback = lookup(languages[fallbackLanguage]?.[namespace], key);
  return interpolate(fallback ?? key, values);
}

export function translationsIn(language: Language, namespace: string): Readonly<Record<string, string>> {
  const localized = languages[language]?.[namespace];
  return isStringRecord(localized) ? localized : {};
}

function interpolate(
  message: string,
  values?: Readonly<Record<string, string | number | undefined>>,
): string {
  if (!values) return message;
  return message.replace(/\{([a-zA-Z0-9_]+)\}/g, (placeholder, key: string) =>
    Object.prototype.hasOwnProperty.call(values, key) ? String(values[key]) : placeholder,
  );
}

function lookup(value: unknown, key: string): string | undefined {
  if (typeof value !== 'object' || value === null) return undefined;
  const record = value as Record<string, unknown>;
  if (typeof record[key] === 'string') return record[key];

  let current: unknown = record;
  for (const segment of key.split('.')) {
    if (typeof current !== 'object' || current === null) return undefined;
    current = (current as Record<string, unknown>)[segment];
  }
  return typeof current === 'string' ? current : undefined;
}

function isStringRecord(value: unknown): value is Record<string, string> {
  return typeof value === 'object' && value !== null &&
    Object.values(value).every((entry) => typeof entry === 'string');
}
