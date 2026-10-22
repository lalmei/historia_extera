import type { Figure } from '../types.ts';
import type { LifeContext, LifeStanding, StandingPart } from '../biography.ts';
import { translateIn, type Language } from './translations.ts';

export function loreLabel(language: Language | undefined, value: string): string {
  return translateIn(language, 'lore', value);
}

export function frCount(value: number, singular: string, plural = `${singular}s`): string {
  const unit = value === 1 ? singular : plural;
  const count = value.toLocaleString(translateIn('fr', 'meta', 'numberLocale'));
  return translateIn('fr', 'count', 'format', { count, unit });
}

function localizedCount(value: number, unit: string): string {
  const form = new Intl.PluralRules('fr').select(value);
  const label = translateIn('fr', 'count', `units.${unit}.${form}`);
  const count = value.toLocaleString(translateIn('fr', 'meta', 'numberLocale'));
  return translateIn('fr', 'count', 'format', { count, unit: label });
}

export function standingSentenceFr(
  figure: Figure,
  standing: LifeStanding,
  ctx: LifeContext,
  place: string | undefined,
  interpretationParts: StandingPart[],
): StandingPart[] {
  const parts: StandingPart[] = [];
  const text = (key: string, values: Readonly<Record<string, string | number>> = {}) =>
    parts.push({ type: 'text', text: translateIn('fr', 'biography', key, values) });
  const female = figure.sex === 'Female';
  const subject = translateIn('fr', 'biography', female ? 'subject.female' : 'subject.male');

  text('standing.opening', { age: standing.age, name: figure.name });
  if (standing.position) {
    text('standing.position', { position: loreLabel('fr', standing.position) });
    text(place ? 'standing.place' : 'standing.noPlace', place ? { place } : {});
  } else {
    text('standing.noPosition');
  }

  const household = translateIn(
    'fr',
    'biography',
    `standing.household.${standing.household}.${female ? 'female' : 'male'}`,
  );
  const children = standing.childCount === 0
    ? translateIn('fr', 'biography', 'standing.children.none')
    : standing.childCount === 1
      ? translateIn('fr', 'biography', 'standing.children.one')
      : translateIn('fr', 'biography', 'standing.children.many', {
        count: standing.childCount.toLocaleString(translateIn('fr', 'meta', 'numberLocale')),
      });
  text('standing.householdAndChildren', { subject, household, children });

  if (standing.closest) {
    text('standing.closest', { subject });
    parts.push({ type: 'entity', id: standing.closest.id });
    text('standing.closestReading', { reading: loreLabel('fr', standing.closest.reading) });
  } else {
    text('standing.period');
  }

  if (interpretationParts.length > 0) {
    parts.push(...interpretationParts, {
      type: 'text',
      text: translateIn('fr', 'biography', 'standing.space'),
    });
  }
  if (standing.activeMemories > 0) {
    text('standing.memories', {
      subject,
      count: localizedCount(standing.activeMemories, 'memory'),
    });
  }
  if (!standing.alive && figure.deathYear !== undefined) {
    const past = ctx.endYear - figure.deathYear;
    text('standing.death', {
      subject,
      form: translateIn('fr', 'biography', female ? 'standing.deathForm.female' : 'standing.deathForm.male'),
      year: figure.deathYear,
    });
    text(
      past > 0 ? 'standing.deathAftermath' : 'standing.period',
      past > 0 ? {
        count: localizedCount(past, 'year'),
        subject: translateIn('fr', 'biography', female ? 'standing.deathPossessive.female' : 'standing.deathPossessive.male'),
      } : {},
    );
  }
  return parts;
}
