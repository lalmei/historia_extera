import assert from 'node:assert/strict';
import test from 'node:test';
import {
  availableLanguages,
  isLanguage,
  translate,
  translateIn,
  translationsIn,
} from './i18n/translations.ts';
import { getNarrationCoverage, getNarrationTemplates } from './i18n/narration.ts';

test('language JSON files define the available language list', () => {
  assert.deepEqual(
    availableLanguages().map(({ code }) => code).sort(),
    ['en', 'fr'],
  );
  for (const language of availableLanguages()) {
    assert.ok(language.name.trim());
    assert.ok(language.nativeName.trim());
    assert.ok(Object.keys(translationsIn(language.code, 'ui')).length > 0);
  }
});

test('language validation accepts only discovered catalogs', () => {
  assert.equal(isLanguage('fr'), true);
  assert.equal(isLanguage('en'), true);
  for (const value of ['de', '', null, undefined, 'FR']) {
    assert.equal(isLanguage(value), false);
  }
});

test('translations fall back to English and preserve unknown source terms', () => {
  assert.equal(translate('fr', 'nav.worlds'), 'Mondes');
  assert.equal(translate('en', 'nav.worlds'), 'Worlds');
  assert.equal(translate('fr', 'app.loading'), 'Lecture de la chronique…');
  assert.equal(translateIn('fr', 'lore', 'Unknown source term'), 'Unknown source term');
});

test('translated messages interpolate values without modifying catalogs', () => {
  assert.equal(
    translate('fr', 'narration.untranslatedSummary', { count: 3 }),
    '3 catégories sans modèle traduit compatible',
  );
  assert.equal(translate('en', 'narration.untranslatedSummary', { count: 2 }), '2 event types have no compatible translated template');
  assert.equal(
    translateIn('fr', 'inline', 'Born in {v0}', { v0: 120 }),
    'Naissance en 120',
  );
});

test('figure-life captions and controls come from locale catalogs', () => {
  assert.equal(translateIn('fr', 'figureLife', 'scale.age'), 'Âge');
  assert.equal(translateIn('fr', 'figureLife', 'ageValue', { value: 34 }), 'âgé de 34 ans');
  assert.match(
    translateIn('fr', 'figureLife', 'constellationCaption', { year: 120 }),
    /en vie en 120/,
  );
  assert.match(translateIn('en', 'figureLife', 'significanceCaption'), /not how good a person they were/);
});

test('event register and historiography text are localized through catalogs', () => {
  assert.equal(translateIn('fr', 'eventList', 'hideRecord'), 'Masquer les détails');
  assert.equal(
    translateIn('fr', 'historiography', 'account.afterWar', { year: 412 }),
    ', guerre commencée en 412. La comparaison s’appuie sur les passages datés et leurs références ; elle n’évalue pas l’exactitude du texte. Les bilans de service peuvent regrouper des engagements sans description individuelle.',
  );
  assert.equal(
    translateIn('fr', 'historiography', 'manySummarizedEngagements', { count: 3 }),
    '3 de ces engagements figurent dans le bilan de service.',
  );
});

test('entity life details use localized templates and plural forms', () => {
  assert.equal(
    translateIn('fr', 'entityPages', 'recordedMentorship.other', { count: 2 }),
    '2 mentorats consignés',
  );
  assert.equal(
    translateIn('fr', 'entityPages', 'guardianship.guardianOf'),
    'Exerce la tutelle de ',
  );
  assert.equal(translateIn('fr', 'entityPages', 'revealed'), 'Complot révélé');
});

test('cosmology descriptions and time units are localized through their catalog', () => {
  assert.equal(
    translateIn('fr', 'cosmology', 'Cosmic timeline description', { year: 18 }),
    'Du Big Bang à l’année où commence la chronique. Les dates passées sont mesurées par rapport à l’année 18.',
  );
  assert.equal(
    translateIn('fr', 'cosmology', 'cometPeriod.days', { count: 42 }),
    '42 j',
  );
});

test('map and knowledge surfaces resolve their localized labels and messages', () => {
  assert.equal(translateIn('fr', 'worldMap', 'zoomIn'), 'Agrandir');
  assert.equal(
    translateIn('fr', 'worldMap', 'houseHomeLiving.one', { town: 'Aube', count: 1 }),
    'Ascension à Aube · 1 membre y vit',
  );
  assert.equal(
    translateIn('fr', 'knowledge', 'register.measured'),
    'Mesurée — elle avance un nombre que le ciel peut confirmer ou réfuter',
  );
  assert.equal(
    translateIn('fr', 'knowledge', 'verdict.Confirmed'),
    'Le ciel leur a donné raison',
  );
});

test('lists and overview surfaces resolve their localized labels and interpolated summaries', () => {
  assert.equal(translateIn('fr', 'lists', 'tradeRoutes'), 'Routes commerciales');
  assert.equal(translateIn('fr', 'lists', 'searchRegions'), 'Rechercher des régions…');
  assert.equal(
    translateIn('fr', 'lists', 'eventCount', { visible: 12, total: 85 }),
    '12 événements sur 85',
  );
  assert.equal(
    translateIn('fr', 'lists', 'diedOf', { cause: 'maladie' }),
    'Cause du décès : maladie',
  );
  assert.equal(
    translateIn('fr', 'lists', 'standingSettlements', { count: '1 234' }),
    '1 234 encore debout',
  );
  assert.equal(
    translateIn('fr', 'lists', 'narrationSyntaxVersion', { version: 3 }),
    'syntaxe de narration v3',
  );
});

test('biography interpretation prose is catalog-backed in English and French', () => {
  assert.equal(
    translateIn('fr', 'interpretation', 'trade.success.0', { name: 'Áldren' }),
    'Le commerce apporta une prospérité considérable à Áldren.',
  );
  assert.equal(
    translateIn('en', 'interpretation', 'trade.success.0', { name: 'Aldren' }),
    'Trade brought Aldren considerable prosperity.',
  );
  assert.equal(
    translateIn('fr', 'interpretation', 'loyalty.0', { name: 'Áldren', years: 20 }),
    'Áldren et <<friend>> se soutinrent pendant 20 ans, un lien dont les archives gardèrent la trace.',
  );
});

test('every biography interpretation branch has three translations in both catalogs', () => {
  const branches = [
    'rootedness.independent',
    'rootedness.deep',
    'rootedness.tied',
    'loyalty',
    'knowledge.mentor',
    'knowledge.apprentice',
    'conflict.killing',
    'conflict.feud',
    'trade.success',
    'trade.failure',
    'scholarly.discovery',
    'scholarly.life',
    'devotion.pilgrimage',
    'devotion.office',
    'religiousScholarship',
    'consolidation',
    'institutionalService',
    'resistance.conspiracy',
    'resistance.denied',
    'frontier',
    'territorialAmbition',
    'isolation',
    'betrayal.turned',
    'betrayal.suffered',
  ];

  for (const language of ['en', 'fr']) {
    for (const branch of branches) {
      for (const variant of [0, 1, 2]) {
        const key = `${branch}.${variant}`;
        assert.notEqual(translateIn(language, 'interpretation', key), key);
      }
    }
  }
});

test('narration is localized only when every template variant has a reference', () => {
  const english = translationsIn('en', 'narration');
  const source = {
    WorldCreated: english.WorldCreated,
    UnknownEvent: 'An event introduced by a newer engine.',
  };
  const french = getNarrationTemplates(source, 'fr');
  assert.match(french.WorldCreated, /^L’histoire consignée/);
  assert.equal(french.UnknownEvent, source.UnknownEvent);
  assert.deepEqual(getNarrationCoverage(source, 'fr'), {
    translatedKinds: ['WorldCreated'],
    originalKinds: ['UnknownEvent'],
  });
  assert.equal(source.WorldCreated, english.WorldCreated);
});
