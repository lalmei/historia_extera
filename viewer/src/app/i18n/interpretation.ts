import { translateIn } from './translations.ts';
import type { Figure, EntityId } from '../types.ts';
import type { LifeContext, StandingPart } from '../biography.ts';
import { THEME_LABELS, type BiographyInterpretation, type BiographyEvidence } from '../biographyInterpret.ts';

type TemplateValues = Readonly<Record<string, string | number | undefined>>;

function firstEvidence(
  interpretation: BiographyInterpretation,
  kind: BiographyEvidence['kind'],
): BiographyEvidence {
  const found = interpretation.evidence.find((e) => e.kind === kind);
  if (!found) throw new Error(`missing evidence ${kind}`);
  return found;
}

function renderTemplate(
  ctx: LifeContext,
  key: string,
  values: TemplateValues,
  linkedEntities: Readonly<Record<string, EntityId>> = {},
): StandingPart[] {
  const localized = translateIn(ctx.language, 'interpretation', key, values);
  const parts: StandingPart[] = [];
  const token = /<<([a-zA-Z0-9_]+)>>/g;
  let cursor = 0;

  for (const match of localized.matchAll(token)) {
    const index = match.index;
    if (index > cursor) parts.push({ type: 'text', text: localized.slice(cursor, index) });
    const id = linkedEntities[match[1]];
    if (!id) throw new Error(`missing linked entity for interpretation template ${key}: ${match[1]}`);
    parts.push({ type: 'entity', id });
    cursor = index + match[0].length;
  }

  if (cursor < localized.length) parts.push({ type: 'text', text: localized.slice(cursor) });
  return parts;
}

function renderVariant(
  ctx: LifeContext,
  key: string,
  variant: number,
  values: TemplateValues,
  linkedEntities?: Readonly<Record<string, EntityId>>,
): StandingPart[] {
  const selected = Number.isInteger(variant) && variant >= 0 && variant < 3 ? variant : 0;
  return renderTemplate(ctx, `${key}.${selected}`, values, linkedEntities);
}

export function renderInterpretationPartsLocalized(
  figure: Figure,
  interpretation: BiographyInterpretation,
  ctx: LifeContext,
  variant: number,
): StandingPart[] {
  const name = figure.name;
  const placeName = (id?: EntityId) =>
    id ? ctx.nameOf(id) : translateIn(ctx.language, 'interpretation', 'unrecordedPlace');
  const values = (extra: TemplateValues = {}) => ({ name, ...extra });
  const hasEvidence = (kind: BiographyEvidence['kind']) =>
    interpretation.evidence.some((e) => e.kind === kind);

  switch (interpretation.theme) {
    case 'Rootedness': {
      const residence = firstEvidence(interpretation, 'LongResidence');
      const path = interpretation.dials.Independence !== undefined
        ? 'rootedness.independent'
        : residence.strength > 0.8
          ? 'rootedness.deep'
          : 'rootedness.tied';
      return renderVariant(ctx, path, variant, values({ place: placeName(residence.placeId) }));
    }
    case 'EnduringLoyalty': {
      const friendship = firstEvidence(interpretation, 'Friendship');
      return renderVariant(
        ctx,
        'loyalty',
        variant,
        values({ years: friendship.endYear - friendship.startYear }),
        { friend: friendship.relatedFigureId! },
      );
    }
    case 'TransmissionOfKnowledge': {
      const mentorship = firstEvidence(interpretation, 'Mentorship');
      const isMentor = figure.mentorships?.some(
        (m) => m.mentorId === figure.id && m.apprenticeId === mentorship.relatedFigureId,
      );
      return renderVariant(
        ctx,
        isMentor ? 'knowledge.mentor' : 'knowledge.apprentice',
        variant,
        values(),
        { other: mentorship.relatedFigureId! },
      );
    }
    case 'ViolentConflict':
      return renderVariant(
        ctx,
        hasEvidence('Killing') ? 'conflict.killing' : 'conflict.feud',
        variant,
        values(),
        { rival: firstEvidence(interpretation, 'Feud').relatedFigureId! },
      );
    case 'CommercialSuccess':
    case 'CommercialFailure':
      return renderVariant(
        ctx,
        interpretation.theme === 'CommercialSuccess' ? 'trade.success' : 'trade.failure',
        variant,
        values(),
      );
    case 'ScholarlyLife': {
      const discovery = hasEvidence('Discovery');
      const evidence = discovery ? firstEvidence(interpretation, 'Discovery') : undefined;
      return renderVariant(
        ctx,
        discovery ? 'scholarly.discovery' : 'scholarly.life',
        variant,
        values({ place: evidence ? placeName(evidence.placeId) : undefined }),
      );
    }
    case 'ReligiousDevotion': {
      const pilgrimage = hasEvidence('Pilgrimage');
      const evidence = pilgrimage ? firstEvidence(interpretation, 'Pilgrimage') : undefined;
      return renderVariant(
        ctx,
        pilgrimage ? 'devotion.pilgrimage' : 'devotion.office',
        variant,
        values({ place: evidence ? placeName(evidence.placeId) : undefined }),
      );
    }
    case 'ReligiousScholarship':
      return renderVariant(ctx, 'religiousScholarship', variant, values());
    case 'ConsolidationOfPower':
      return renderVariant(ctx, 'consolidation', variant, values());
    case 'InstitutionalService':
      return renderVariant(ctx, 'institutionalService', variant, values());
    case 'ResistanceToAuthority':
      return renderVariant(
        ctx,
        hasEvidence('Revolt') ? 'resistance.conspiracy' : 'resistance.denied',
        variant,
        values(),
      );
    case 'FrontierLife': {
      const migration = firstEvidence(interpretation, 'Migration');
      return renderVariant(ctx, 'frontier', variant, values({ place: placeName(migration.placeId) }));
    }
    case 'TerritorialAmbition':
      return renderVariant(ctx, 'territorialAmbition', variant, values());
    case 'Isolation':
      return renderVariant(ctx, 'isolation', variant, values());
    case 'Betrayal': {
      const betrayal = firstEvidence(interpretation, 'Betrayal');
      const turned = interpretation.evidence.some(
        (e) => e.kind === 'Betrayal' && e.tag === 'Turned',
      );
      return renderVariant(
        ctx,
        turned ? 'betrayal.turned' : 'betrayal.suffered',
        variant,
        values(),
        { other: betrayal.relatedFigureId! },
      );
    }
    default:
      return renderTemplate(ctx, 'fallback', values({
        theme: translateIn(ctx.language, 'lore', THEME_LABELS[interpretation.theme]).toLowerCase(),
      }));
  }
}
