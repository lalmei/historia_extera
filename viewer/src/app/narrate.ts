import { kindOf, type EntityId, type HistoryEvent, type Sex } from './types.ts';

/**
 * Renders an event using the template the engine shipped for its kind.
 *
 * This is the payoff of putting templates in the export: the viewer has no
 * per-event-kind knowledge at all. When Milestone 6 adds wars and battles, or
 * Milestone 8 adds plagues and artifacts, they render correctly here without a
 * line of viewer code changing. A `switch` on event kind would have to be kept in
 * sync across a language boundary, and would not be.
 *
 * The grammar is small and must match `HistoryEngine.Events.Narration` exactly:
 *
 *   - `{subject}` `{object}` `{location}` — entity slots, emitted as segments so
 *     the caller can turn them into cross-links.
 *   - `{data:key}` — plain text from the event's data payload.
 *   - `{extra:kind}` — the first entity of that short kind prefix (`hol`, `rel`, `civ`, …) among
 *     the event's `extra` ids, emitted as a link like the named slots. Absent when the event
 *     carries none of that kind, which is what lets one template hold several mutually exclusive
 *     clauses — a journey's reason is a holy site, a faith or a realm depending on the errand.
 *   - `{self}` `{other}` — the figure whose page is being read, and the other
 *     figure among subject and object.
 *   - `{as:key}` `{not:key}` `{self:subject}` (also object, location, extra) —
 *     role tests that succeed as empty text.
 *   - `{cap}` — capitalize the next letter. A leading `the`/`a`/`an` is also
 *     capitalized, so a house line can start `{the}{subject}`. A dropped prefix
 *     still needs `{cap}`.
 *   - `{a}` `{an}` — the indefinite article matching the next word. `{the}` is `the`,
 *     unless that word is already `the`.
 *   - `{they:slot}` `{them:slot}` `{their:slot}` — a pronoun for the named figure
 *     in that slot. Absent sex falls back to they/them/their.
 *   - `[ ... ]` — optional segment, dropped whole if any placeholder inside it is
 *     absent. Segments nest.
 *
 * A `Kind.self` template, when present, is the same fact told from that figure's
 * point of view. Numbered keys (`Kind.1`) and a `voice` data field (`Kind.elective`)
 * are other wordings of the same fact, selected by the engine. The viewer only looks
 * them up.
 *
 * `meta.narrationSyntaxVersion` guards against the grammar changing under us.
 */
export const NARRATION_SYNTAX_VERSION = 4;

export const SELF_KEY_SUFFIX = '.self';

export const VOICE_DATA_KEY = 'voice';

/** Must match `Narration.VariantMix` so numbered templates pick the same line. */
export const VARIANT_MIX = 2654435761;

export type NarrationPart =
  | { type: 'text'; text: string }
  | { type: 'entity'; id: EntityId };

type Mark = 'cap' | 'a' | 'the';

type RawPart =
  | NarrationPart
  | { type: 'mark'; mark: Mark };

/**
 * Renders to parts rather than a string, so entity slots can become links.
 * `narrateText` wraps this for the plain-text cases.
 */
export function narrate(
  event: HistoryEvent,
  templates: Record<string, string>,
  nameOf: (id: EntityId) => string,
  viewpoint?: EntityId,
  sexOf?: (id: EntityId) => Sex | undefined,
): NarrationPart[] {
  const template = templateFor(event, templates, viewpoint);
  const parts = renderTemplate(template, event, nameOf, viewpoint, sexOf);

  if (viewpoint && parts.length === 0) {
    return renderTemplate(templateFor(event, templates), event, nameOf, viewpoint, sexOf);
  }

  return parts;
}

export function narrateText(
  event: HistoryEvent,
  templates: Record<string, string>,
  nameOf: (id: EntityId) => string,
  viewpoint?: EntityId,
  sexOf?: (id: EntityId) => Sex | undefined,
): string {
  return narrate(event, templates, nameOf, viewpoint, sexOf)
    .map((part) => (part.type === 'text' ? part.text : nameOf(part.id)))
    .join('');
}

/** Consecutive events of the same year, in the order they were given. */
export function stitchYears(events: HistoryEvent[]): HistoryEvent[][] {
  const groups: HistoryEvent[][] = [];

  for (const event of events) {
    const last = groups[groups.length - 1];
    if (last && last[0].year === event.year) last.push(event);
    else groups.push([event]);
  }

  return groups;
}

/**
 * The template key the engine selected: a factual voice, a numbered variant mixed
 * from the event id, a `.self` line, or the world wording.
 */
export function templateFor(
  event: HistoryEvent,
  templates: Record<string, string>,
  viewpoint?: EntityId,
): string {
  const kind = event.kind;
  const voice = event.data?.[VOICE_DATA_KEY];
  const self = Boolean(viewpoint) && kindOf(viewpoint) === 'fig';

  if (voice && self && templates[`${kind}.${voice}${SELF_KEY_SUFFIX}`]) {
    return templates[`${kind}.${voice}${SELF_KEY_SUFFIX}`];
  }

  if (self && templates[`${kind}${SELF_KEY_SUFFIX}`]) {
    const keys = numberedKeys(kind, SELF_KEY_SUFFIX, templates);
    if (keys.length > 1) return templates[keys[variantIndex(event.id, keys.length)]];
    return templates[`${kind}${SELF_KEY_SUFFIX}`];
  }

  if (voice && templates[`${kind}.${voice}`]) return templates[`${kind}.${voice}`];

  const keys = numberedKeys(kind, '', templates);
  if (keys.length > 1) return templates[keys[variantIndex(event.id, keys.length)]];

  return templates[kind] ?? templates.Unknown ?? 'Something happened.';
}

export function variantIndex(eventId: number, count: number): number {
  if (count <= 1) return 0;
  return ((Math.imul(eventId, VARIANT_MIX) >>> 0) % count);
}

/**
 * The parts of an event no template prints: leftover data, and the entities it is only
 * indexed under.
 *
 * Templates are prose and are allowed to leave things out — a coronation reads better without
 * the new king's age in it, and a battle without the id of the war it belongs to. Everything
 * they leave out is still in the export, and this is what lets the chronicle show it on
 * request without the viewer learning what any particular event kind carries.
 *
 * A `{data:key}` inside a dropped optional segment counts as unprinted, because it is.
 */
export function unnarrated(
  event: HistoryEvent,
  templates: Record<string, string>,
  nameOf: (id: EntityId) => string,
  viewpoint?: EntityId,
  sexOf?: (id: EntityId) => Sex | undefined,
): { data: [string, string][]; extra: EntityId[] } {
  const template = templateFor(event, templates, viewpoint);
  const printed = new Set<string>();
  const named = new Set<EntityId | undefined>([
    event.subject,
    event.object,
    event.location,
    viewpoint,
  ]);

  walkPrinted(template, event, nameOf, viewpoint, sexOf, printed, named);

  return {
    data: Object.entries(event.data ?? {}).filter(([key]) => !printed.has(key)),
    extra: (event.extra ?? []).filter((id) => !named.has(id)),
  };
}

function numberedKeys(kind: string, suffix: string, templates: Record<string, string>): string[] {
  const keys: string[] = [];
  const primary = kind + suffix;
  if (templates[primary]) keys.push(primary);

  for (let n = 1; ; n++) {
    const key = `${kind}.${n}${suffix}`;
    if (!templates[key]) break;
    keys.push(key);
  }

  return keys;
}

function walkPrinted(
  template: string,
  event: HistoryEvent,
  nameOf: (id: EntityId) => string,
  viewpoint: EntityId | undefined,
  sexOf: ((id: EntityId) => Sex | undefined) | undefined,
  printed: Set<string>,
  named: Set<EntityId | undefined>,
): boolean {
  let i = 0;
  while (i < template.length) {
    if (template[i] === '[') {
      const close = findSegmentEnd(template, i);
      if (close < 0) break;
      const innerPrinted = new Set<string>();
      const innerNamed = new Set<EntityId | undefined>();
      if (walkPrinted(
        template.slice(i + 1, close),
        event,
        nameOf,
        viewpoint,
        sexOf,
        innerPrinted,
        innerNamed,
      )) {
        for (const key of innerPrinted) printed.add(key);
        for (const id of innerNamed) named.add(id);
      }
      i = close + 1;
      continue;
    }

    if (template[i] === '{') {
      const close = template.indexOf('}', i);
      if (close < 0) break;
      const token = template.slice(i + 1, close);
      if (resolve(token, event, nameOf, viewpoint, sexOf) === null) return false;
      if (token.startsWith('data:')) printed.add(token.slice(5));
      if (token.startsWith('extra:')) named.add(firstExtraOfKind(event, token.slice(6)));
      i = close + 1;
      continue;
    }

    i++;
  }

  return true;
}

function renderTemplate(
  template: string,
  event: HistoryEvent,
  nameOf: (id: EntityId) => string,
  viewpoint: EntityId | undefined,
  sexOf: ((id: EntityId) => Sex | undefined) | undefined,
): NarrationPart[] {
  const raw = walk(template, event, nameOf, viewpoint, sexOf, false) ?? [];
  return merge(finish(raw, nameOf));
}

function walk(
  template: string,
  event: HistoryEvent,
  nameOf: (id: EntityId) => string,
  viewpoint: EntityId | undefined,
  sexOf: ((id: EntityId) => Sex | undefined) | undefined,
  optional: boolean,
): RawPart[] | null {
  const parts: RawPart[] = [];
  let i = 0;

  while (i < template.length) {
    const c = template[i];

    if (c === '[') {
      const close = findSegmentEnd(template, i);
      if (close < 0) {
        parts.push({ type: 'text', text: template.slice(i) });
        break;
      }

      const inner = walk(template.slice(i + 1, close), event, nameOf, viewpoint, sexOf, true);
      if (inner) parts.push(...inner);
      i = close + 1;
      continue;
    }

    if (c === '{') {
      const close = template.indexOf('}', i);
      if (close < 0) {
        parts.push({ type: 'text', text: template.slice(i) });
        break;
      }

      const resolved = resolve(template.slice(i + 1, close), event, nameOf, viewpoint, sexOf);
      if (resolved === null) {
        if (optional) return null;
      } else {
        parts.push(resolved);
      }

      i = close + 1;
      continue;
    }

    let end = i;
    while (end < template.length && template[end] !== '{' && template[end] !== '[') end++;
    parts.push({ type: 'text', text: template.slice(i, end) });
    i = end;
  }

  return parts;
}

function findSegmentEnd(template: string, openIndex: number): number {
  let depth = 1;
  for (let i = openIndex + 1; i < template.length; i++) {
    if (template[i] === '[') depth++;
    else if (template[i] === ']') {
      depth--;
      if (depth === 0) return i;
    }
  }

  return -1;
}

function finish(parts: RawPart[], nameOf: (id: EntityId) => string): NarrationPart[] {
  const out: NarrationPart[] = [];
  let capNext = false;

  const peekWord = (from: number): string => {
    let text = '';
    for (let i = from; i < parts.length; i++) {
      const part = parts[i];
      if (part.type === 'mark') continue;
      text += part.type === 'text' ? part.text : nameOf(part.id);
      if (text.trim().length > 0) break;
    }

    return text.trim().split(/\s+/, 1)[0] ?? '';
  };

  const pushText = (text: string) => {
    if (!text) return;
    if (capNext) {
      const idx = [...text].findIndex((ch) => /\p{L}/u.test(ch));
      if (idx >= 0) {
        text = text.slice(0, idx) + text.charAt(idx).toUpperCase() + text.slice(idx + 1);
        capNext = false;
      }
    }

    out.push({ type: 'text', text });
  };

  for (let i = 0; i < parts.length; i++) {
    const part = parts[i];
    if (part.type === 'mark') {
      if (part.mark === 'cap') {
        capNext = true;
        continue;
      }

      const word = peekWord(i + 1);
      if (part.mark === 'the') {
        if (word.toLowerCase() !== 'the') pushText('the ');
      } else if (word.toLowerCase() !== 'a' && word.toLowerCase() !== 'an') {
        pushText(/^[aeiou]/i.test(word) ? 'an ' : 'a ');
      }

      continue;
    }

    if (part.type === 'text') {
      pushText(part.text);
      continue;
    }

    if (capNext) capNext = false;
    out.push(part);
  }

  const merged = merge(out.filter((part) => part.type !== 'text' || part.text.length > 0));
  if (merged.length > 0 && merged[0].type === 'text') {
    merged[0].text = merged[0].text.trimStart();
    const lead = merged[0].text;
    if (/^(the|an?) /i.test(lead) && lead.charAt(0) === lead.charAt(0).toLowerCase()) {
      merged[0].text = lead.charAt(0).toUpperCase() + lead.slice(1);
    }
  }

  const last = merged[merged.length - 1];
  if (last?.type === 'text') last.text = last.text.trimEnd();

  return merged.filter((part) => part.type !== 'text' || part.text.length > 0);
}

function resolve(
  token: string,
  event: HistoryEvent,
  nameOf: (id: EntityId) => string,
  viewpoint: EntityId | undefined,
  sexOf: ((id: EntityId) => Sex | undefined) | undefined,
): RawPart | null {
  if (token === 'cap') return { type: 'mark', mark: 'cap' };
  if (token === 'a' || token === 'an') return { type: 'mark', mark: 'a' };
  if (token === 'the') return { type: 'mark', mark: 'the' };

  if (token.startsWith('data:')) {
    const value = event.data?.[token.slice(5)];
    return value ? { type: 'text', text: value } : null;
  }

  if (token.startsWith('extra:')) {
    const found = firstExtraOfKind(event, token.slice(6));
    return found ? { type: 'entity', id: found } : null;
  }

  if (token.startsWith('as:')) {
    if (!viewpoint) return null;
    return event.data?.[token.slice(3)] === nameOf(viewpoint) ? { type: 'text', text: '' } : null;
  }

  if (token.startsWith('not:')) {
    if (!viewpoint) return null;
    return event.data?.[token.slice(4)] === nameOf(viewpoint)
      ? null
      : { type: 'text', text: '' };
  }

  if (token.startsWith('self:')) {
    if (!viewpoint) return null;

    const slot = token.slice(5);
    const holds =
      slot === 'subject'
        ? viewpoint === event.subject
        : slot === 'object'
          ? viewpoint === event.object
          : slot === 'location'
            ? viewpoint === event.location
            : slot === 'extra'
              ? (event.extra ?? []).includes(viewpoint)
              : false;

    return holds ? { type: 'text', text: '' } : null;
  }

  if (token.startsWith('they:') || token.startsWith('them:') || token.startsWith('their:')) {
    const colon = token.indexOf(':');
    const id = slotId(token.slice(colon + 1), event, viewpoint);
    if (!id) return null;
    return { type: 'text', text: pronoun(token.slice(0, colon), sexOf?.(id)) };
  }

  const id = slotId(token, event, viewpoint);
  if (!id) return null;
  return { type: 'entity', id };
}

function pronoun(form: string, sex: Sex | undefined): string {
  const female = sex === 'Female';
  const male = sex === 'Male';
  if (form === 'they') return female ? 'she' : male ? 'he' : 'they';
  if (form === 'them') return female ? 'her' : male ? 'him' : 'them';
  if (form === 'their') return female ? 'her' : male ? 'his' : 'their';
  return 'they';
}

function slotId(
  slot: string,
  event: HistoryEvent,
  viewpoint: EntityId | undefined,
): EntityId | undefined {
  if (slot === 'subject') return event.subject;
  if (slot === 'object') return event.object;
  if (slot === 'location') return event.location;
  if (slot === 'self') return viewpoint;
  if (slot === 'other') return otherFigure(event, viewpoint);
  if (slot === 'extra') return (event.extra ?? []).find((id) => kindOf(id) === 'fig');
  return undefined;
}

/** The first extra id of the given short kind prefix, in the order the engine wrote them. */
function firstExtraOfKind(event: HistoryEvent, prefix: string): EntityId | undefined {
  return (event.extra ?? []).find((id) => kindOf(id) === prefix);
}

function otherFigure(event: HistoryEvent, self: EntityId | undefined): EntityId | undefined {
  if (!self) return undefined;

  if (event.subject && kindOf(event.subject) === 'fig' && event.subject !== self) {
    return event.subject;
  }

  if (event.object && kindOf(event.object) === 'fig' && event.object !== self) {
    return event.object;
  }

  return undefined;
}

function merge(parts: NarrationPart[]): NarrationPart[] {
  const merged: NarrationPart[] = [];

  for (const part of parts) {
    const last = merged[merged.length - 1];
    if (part.type === 'text' && last?.type === 'text') {
      last.text += part.text;
    } else if (part.type !== 'text' || part.text.length > 0) {
      merged.push({ ...part });
    }
  }

  return merged;
}
