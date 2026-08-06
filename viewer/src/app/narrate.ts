import type { EntityId, HistoryEvent } from './types';

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
 *   - `[ ... ]` — optional segment, dropped whole if any placeholder inside it is
 *     absent. This is what keeps prose grammatical: a figure born before any
 *     settlement exists renders "Aeda was born." and not "Aeda was born in ."
 *
 * `meta.narrationSyntaxVersion` guards against the grammar changing under us.
 */
export const NARRATION_SYNTAX_VERSION = 1;

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
): NarrationPart[] {
  const template = templates[event.kind] ?? templates.Unknown ?? 'Something happened.';
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

      const segment = renderSegment(template.slice(i + 1, close), event, nameOf);
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

      const resolved = resolve(template.slice(i + 1, close), event, nameOf);
      if (resolved) parts.push(resolved);

      i = close + 1;
      continue;
    }

    // Accumulate literal runs so the output is not one part per character.
    let end = i;
    while (end < template.length && template[end] !== '{' && template[end] !== '[') end++;
    parts.push({ type: 'text', text: template.slice(i, end) });
    i = end;
  }

  return merge(parts);
}

export function narrateText(
  event: HistoryEvent,
  templates: Record<string, string>,
  nameOf: (id: EntityId) => string,
): string {
  return narrate(event, templates, nameOf)
    .map((part) => (part.type === 'text' ? part.text : nameOf(part.id)))
    .join('');
}

/** Returns null if any placeholder inside the segment is unresolvable. */
function renderSegment(
  inner: string,
  event: HistoryEvent,
  nameOf: (id: EntityId) => string,
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

    const resolved = resolve(inner.slice(i + 1, close), event, nameOf);
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
): NarrationPart | null {
  if (token.startsWith('data:')) {
    const value = event.data?.[token.slice(5)];
    return value ? { type: 'text', text: value } : null;
  }

  const id =
    token === 'subject'
      ? event.subject
      : token === 'object'
        ? event.object
        : token === 'location'
          ? event.location
          : undefined;

  if (!id) return null;

  // nameOf is not called here — the entity part carries the id so the renderer can
  // link it. Callers that want plain text resolve it themselves.
  void nameOf;
  return { type: 'entity', id };
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
