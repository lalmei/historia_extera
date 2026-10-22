import { translationsIn, type Language } from './translations.ts';

export interface NarrationCoverage {
  translatedKinds: string[];
  originalKinds: string[];
}

interface CachedNarration {
  templates: Record<string, string>;
  coverage: NarrationCoverage;
}

const cache = new WeakMap<Record<string, string>, Map<Language, CachedNarration>>();

function own(object: Readonly<Record<string, string>>, key: string): boolean {
  return Object.prototype.hasOwnProperty.call(object, key);
}

function localizedNarration(
  original: Record<string, string>,
  language: Language,
): CachedNarration {
  let languageCache = cache.get(original);
  if (!languageCache) {
    languageCache = new Map();
    cache.set(original, languageCache);
  }

  const existing = languageCache.get(language);
  if (existing) return existing;

  const localized = translationsIn(language, 'narration');
  const reference = translationsIn('en', 'narration');
  const families = new Map<string, string[]>();
  for (const key of Object.keys(original)) {
    const kind = key.split('.', 1)[0];
    const keys = families.get(kind);
    if (keys) keys.push(key);
    else families.set(kind, [key]);
  }

  const templates = { ...original };
  const translatedKinds: string[] = [];
  const originalKinds: string[] = [];

  for (const [kind, keys] of families) {
    const supported = keys.every((key) =>
      own(reference, key) &&
      own(localized, key) &&
      original[key] === reference[key],
    );

    if (!supported) {
      originalKinds.push(kind);
      continue;
    }

    translatedKinds.push(kind);
    for (const key of keys) templates[key] = localized[key];
  }

  const result: CachedNarration = {
    templates,
    coverage: {
      translatedKinds: translatedKinds.sort(),
      originalKinds: originalKinds.sort(),
    },
  };
  languageCache.set(language, result);
  return result;
}

/** Selects display templates without mutating the export or adding missing keys. */
export function getNarrationTemplates(
  original: Record<string, string>,
  language: Language = 'en',
): Record<string, string> {
  if (language === 'en') return original;
  return localizedNarration(original, language).templates;
}

/** Reports event families that retain their source wording in the selected language. */
export function getNarrationCoverage(
  original: Record<string, string>,
  language: Language,
): NarrationCoverage {
  const { coverage } = localizedNarration(original, language);
  return {
    translatedKinds: [...coverage.translatedKinds],
    originalKinds: [...coverage.originalKinds],
  };
}
