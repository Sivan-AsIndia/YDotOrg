/**
 * Keeping GUIDs off the screen.
 *
 * WHY THIS EXISTS. Every record in the platform is keyed by a GUID, and a GUID is the one value in
 * the system that means nothing whatever to the person reading it: "9fb11890-a08e-4adc-95ca-
 * 8e4d71f4dd21" cannot be read out over a telephone, cannot be searched for, cannot be recognised
 * on a second screen, and cannot be told apart from the next one at a glance. Every record ALSO
 * carries something a person can use - a campaign code, a user reference, an organisation code, a
 * display name - and that is what belongs on screen.
 *
 * THE LEAK IS ALMOST ALWAYS A FALLBACK, not a deliberate binding. Somebody writes
 * `name(id) ?? id`, or `code ?? id`, and it reads perfectly until the day the lookup misses - and
 * then the screen prints thirty-six characters of hexadecimal where a name should be, in
 * production, to a user. So the helpers here are written to be used AS the fallback rather than
 * beside it.
 *
 * IT IS NOT VALIDATION. Nothing here rejects anything or changes what is sent to the server; the
 * ids still travel on every request exactly as they did. This is only about what is rendered.
 */

/**
 * The canonical 8-4-4-4-12 form, with or without braces, in either case.
 *
 * DELIBERATELY STRICT. It matches a GUID and nothing else - not a campaign code, not USR-00001,
 * not an intent reference, not a correlation id that happens to contain hyphens - because
 * anything it matches gets replaced on screen, and replacing something a person could have used
 * is worse than letting a GUID through.
 */
const GUID_PATTERN =
  /^\{?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\}?$/i;

/**
 * A GUID INSIDE a longer string: the 8-4-4-4-12 form and the 32-hex "N" form correlation ids use.
 *
 * FOR FREE TEXT ONLY - a server message, an audit note, a toast. It is looser than GUID_PATTERN
 * because it has to find the value mid-sentence, which is exactly why it is never used to decide
 * whether a whole field is an id.
 */
const EMBEDDED_GUID_PATTERN =
  /\{?\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b\}?|\b[0-9a-f]{32}\b/gi;

/** True when this value is a bare GUID and therefore must not be shown to anybody. */
export function isGuid(value: unknown): boolean {
  return typeof value === 'string' && GUID_PATTERN.test(value.trim());
}

/**
 * The value if a person can use it; the fallback if it is a GUID.
 *
 * USE IT AS THE FALLBACK ITSELF. The shape to reach for is
 * `readableIdentifier(lookupCode(id), 'Not known')` rather than `lookupCode(id) ?? id` - the
 * second is the line that puts a GUID on screen the day the lookup misses.
 *
 * AN EMPTY VALUE GETS THE FALLBACK TOO, because a blank where a reference should be reads as a
 * rendering fault; a sentence saying the reference is not known reads as an answer.
 */
export function readableIdentifier(
  value: string | null | undefined,
  fallback = 'Not available',
): string {
  const trimmed = (value ?? '').trim();

  if (!trimmed || isGuid(trimmed)) {
    return fallback;
  }

  return trimmed;
}

/**
 * The first of several candidates a person can actually read.
 *
 * FOR THE COMMON "code, else reference, else id" SHAPE. Records arrive from different endpoints
 * with different halves of their identity populated - a list projection may carry the code while
 * a detail response carries the name - so the caller lists what it has, in the order it would
 * rather show, and anything GUID-shaped is skipped rather than used as a last resort.
 */
export function firstReadable(
  candidates: readonly (string | null | undefined)[],
  fallback = 'Not available',
): string {
  for (const candidate of candidates) {
    const trimmed = (candidate ?? '').trim();

    if (trimmed && !isGuid(trimmed)) {
      return trimmed;
    }
  }

  return fallback;
}

/**
 * A sentence with every GUID in it replaced by words.
 *
 * THE SAFETY NET FOR TEXT THIS SIDE DID NOT WRITE. Server messages, notification bodies and audit
 * notes are composed elsewhere, and a single `{record.Id}` in one of them puts thirty-six
 * characters of hexadecimal into a toast. The screen cannot know the name behind an id it was
 * only handed inside a sentence, so the id is replaced by a phrase that still reads.
 */
export function withoutGuids(text: string | null | undefined, replacement = 'this record'): string {
  if (!text) {
    return text ?? '';
  }

  return text.replace(EMBEDDED_GUID_PATTERN, replacement);
}

/**
 * The short form of a correlation id, for a person to quote to support: the first eight
 * characters, upper-case - "7F3A09C2".
 *
 * NOT A GUID, AND STILL FINDABLE. Support searches the service log by prefix, and eight hex
 * characters are unique across any realistic window of requests; thirty-two are unreadable over a
 * telephone, which is the only place a support reference is ever used.
 */
export function supportReference(value: string | null | undefined, fallback = '—'): string {
  const compact = (value ?? '').replace(/[^0-9a-z]/gi, '');

  return compact ? compact.slice(0, 8).toUpperCase() : fallback;
}
