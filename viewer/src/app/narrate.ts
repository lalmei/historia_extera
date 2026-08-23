import { kindOf, type EntityId, type HistoryEvent } from './types';

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
 *   - `[ ... ]` — optional segment, dropped whole if any placeholder inside it is
 *     absent. This is what keeps prose grammatical: a figure born before any
 *     settlement exists renders "Aeda was born." and not "Aeda was born in ."
 *
 * A `Kind.self` template, when present, is the same fact told from that figure's
 * point of view. Kinds without one keep the world wording.
 *
 * `meta.narrationSyntaxVersion` guards against the grammar changing under us.
 */
export const NARRATION_SYNTAX_VERSION = 3;

export const SELF_KEY_SUFFIX = '.self';

export type NarrationPart =
  | { type: 'text'; text: string }
  | { type: 'entity'; id: EntityId };

/**
 * Renders to parts rather than a string, so entity slots can become links.
 * `narrateText` wraps this for the plain-text cases.
 */
export function narrate(
  event: HistoryEvent,
  templates: Record<string, string>,
  nameOf: (id: EntityId) => string,
  viewpoint?: EntityId,
): NarrationPart[] {
  const world = templates[event.kind] ?? templates.Unknown ?? 'Something happened.';
  const self = viewpoint ? templates[`${event.kind}${SELF_KEY_SUFFIX}`] : undefined;
  const parts = renderTemplate(self ?? world, event, nameOf, viewpoint);

  if (viewpoint && self && parts.length === 0) {
    return renderTemplate(world, event, nameOf, viewpoint);
  }

  return parts;
}

export function narrateText(
  event: HistoryEvent,
  templates: Record<string, string>,
  nameOf: (id: EntityId) => string,
  viewpoint?: EntityId,
): string {
  return narrate(event, templates, nameOf, viewpoint)
    .map((part) => (part.type === 'text' ? part.text : nameOf(part.id)))
    .join('');
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
): { data: [string, string][]; extra: EntityId[] } {
  const world = templates[event.kind] ?? templates.Unknown ?? '';
  const self = viewpoint ? templates[`${event.kind}${SELF_KEY_SUFFIX}`] : undefined;
  const template = self ?? world;
  const printed = new Set<string>();

  for (const [, inner] of template.matchAll(/\[([^\]]*)\]/g)) {
    if (!segmentHolds(inner, event, nameOf, viewpoint)) continue;
    for (const [, key] of inner.matchAll(/\{data:(\w+)\}/g)) printed.add(key);
  }

  for (const [, key] of template.replace(/\[[^\]]*\]/g, '').matchAll(/\{data:(\w+)\}/g)) {
    if (event.data?.[key]) printed.add(key);
  }

  const named = new Set<EntityId | undefined>([
    event.subject,
    event.object,
    event.location,
    viewpoint,
  ]);

  // An extra the template named through {extra:kind} has been printed, so it is not left over.
  // Only from segments that survived, for the same reason a {data:key} inside a dropped segment
  // still counts as unprinted.
  for (const [, inner] of template.matchAll(/\[([^\]]*)\]/g)) {
    if (!segmentHolds(inner, event, nameOf, viewpoint)) continue;
    for (const [, prefix] of inner.matchAll(/\{extra:(\w+)\}/g)) {
      named.add(firstExtraOfKind(event, prefix));
    }
  }

  for (const [, prefix] of template
    .replace(/\[[^\]]*\]/g, '')
    .matchAll(/\{extra:(\w+)\}/g)) {
    named.add(firstExtraOfKind(event, prefix));
  }

  return {
    data: Object.entries(event.data ?? {}).filter(([key]) => !printed.has(key)),
    extra: (event.extra ?? []).filter((id) => !named.has(id)),
  };
}

function renderTemplate(
  template: string,
  event: HistoryEvent,
  nameOf: (id: EntityId) => string,
  viewpoint: EntityId | undefined,
): NarrationPart[] {
  const parts: NarrationPart[] = [];

  let i = 0;
  while (i < template.length) {
    const c = template[i];

    if (c === '[') {
      const close = template.indexOf(']', i);
      if (close < 0) {
        parts.push({ type: 'text', text: template.slice(i) });
        break;
      }

      const segment = renderSegment(template.slice(i + 1, close), event, nameOf, viewpoint);
      if (segment) parts.push(...segment);

      i = close + 1;
      continue;
    }

    if (c === '{') {
      const close = template.indexOf('}', i);
      if (close < 0) {
        parts.push({ type: 'text', text: template.slice(i) });
        break;
      }

      const resolved = resolve(template.slice(i + 1, close), event, nameOf, viewpoint);
      if (resolved) parts.push(resolved);

      i = close + 1;
      continue;
    }

    let end = i;
    while (end < template.length && template[end] !== '{' && template[end] !== '[') end++;
    parts.push({ type: 'text', text: template.slice(i, end) });
    i = end;
  }

  return merge(parts);
}

/** Whether an optional segment survives — it is dropped whole if anything in it is absent. */
function segmentHolds(
  inner: string,
  event: HistoryEvent,
  nameOf: (id: EntityId) => string,
  viewpoint: EntityId | undefined,
): boolean {
  for (const [, token] of inner.matchAll(/\{([^}]*)\}/g)) {
    if (resolve(token, event, nameOf, viewpoint) === null) return false;
  }

  return true;
}

/** Returns null if any placeholder inside the segment is unresolvable. */
function renderSegment(
  inner: string,
  event: HistoryEvent,
  nameOf: (id: EntityId) => string,
  viewpoint: EntityId | undefined,
): NarrationPart[] | null {
  const parts: NarrationPart[] = [];

  let i = 0;
  while (i < inner.length) {
    if (inner[i] !== '{') {
      let end = i;
      while (end < inner.length && inner[end] !== '{') end++;
      parts.push({ type: 'text', text: inner.slice(i, end) });
      i = end;
      continue;
    }

    const close = inner.indexOf('}', i);
    if (close < 0) {
      parts.push({ type: 'text', text: inner.slice(i) });
      break;
    }

    const resolved = resolve(inner.slice(i + 1, close), event, nameOf, viewpoint);
    if (!resolved) return null;

    parts.push(resolved);
    i = close + 1;
  }

  return parts;
}

function resolve(
  token: string,
  event: HistoryEvent,
  nameOf: (id: EntityId) => string,
  viewpoint: EntityId | undefined,
): NarrationPart | null {
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

  const id =
    token === 'subject'
      ? event.subject
      : token === 'object'
        ? event.object
        : token === 'location'
          ? event.location
          : token === 'self'
            ? viewpoint
            : token === 'other'
              ? otherFigure(event, viewpoint)
              : undefined;

  if (!id) return null;

  return { type: 'entity', id };
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
