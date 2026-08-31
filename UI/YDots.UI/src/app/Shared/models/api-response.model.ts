import { LookupItem as GeneratedLookupItem, OutcomeResponse as GeneratedOutcomeResponse, ValidationError } from './iam-contract.model';

/**
 * The envelope every IAM endpoint returns, for success and failure alike.
 *
 * The server writes all six keys every time, so the client never has to guess whether a field
 * exists. Read `success` first; on false, `message` is safe to show to the person and `errors`
 * holds the per-field detail for a form.
 *
 * WHY THIS ONE IS HAND-WRITTEN WHEN THE REST ARE GENERATED
 * --------------------------------------------------------
 * OpenAPI has no generics, so the server's `ApiResponse<T>` arrives in the document flattened
 * into 120-odd concrete types — `UserDetailResponseApiResponse`, and so on. Those exist in
 * `iam-contract.model.ts` and are perfectly correct, but writing `ApiResponse<UserDetail>` reads
 * far better at every call site than naming a different type for each one. The two agree on
 * every field; only the generic is added back here.
 */
export interface ApiResponse<TData = unknown> {
  success: boolean;
  data: TData | null;
  message: string | null;
  /** A stable code such as VALIDATION_FAILED or PERMISSION_DENIED. Branch on this, not on `message`. */
  errorCode: string | null;
  errors: ApiValidationError[] | null;
  /** Ties this response to one line in the server log. Worth showing on an error screen. */
  correlationId: string | null;
}

/** One field-level problem, ready to attach to the matching form control. */
export type ApiValidationError = Required<Pick<ValidationError, never>> & {
  field: string;
  message: string;
};

/**
 * One page of rows plus the counters a pager needs.
 *
 * NOTE THE FIELD IS `page`, NOT `pageIndex`. That is what the server sends. It reads oddly next
 * to `pageSize`, but a name invented on this side would simply be `undefined` at runtime, which
 * is the failure mode this whole model file exists to prevent.
 */
export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

/**
 * What every state-changing endpoint returns when it has no richer payload: suspend,
 * reactivate, approve, cancel, resend, and so on.
 *
 * `version` is the one to pay attention to — it is the record's new optimistic-concurrency
 * stamp, and the next call that changes the same record must send it back as
 * `expectedVersion`. Screens that keep a stale version get a 409 on their next action.
 *
 * `permittedActions` is what THIS caller may do to the record next, decided by the server from
 * the record's state and the caller's permissions. Render buttons from it rather than deciding
 * in the component, and the buttons can never disagree with what the API will allow.
 */
export type OutcomeResponse = Required<GeneratedOutcomeResponse>;

/**
 * A row in any dropdown the API serves.
 *
 * `id`/`code`/`name`, not `value`/`label` — see the note on PagedResponse.
 */
export type LookupItem = GeneratedLookupItem;

export type { ValidationError } from './iam-contract.model';

/**
 * Narrows an unknown error to the API envelope.
 *
 * The HTTP interceptor rethrows the parsed body, so a `catchError` on any call receives this
 * shape when the server answered, and something else entirely (a `ProgressEvent`, say) when the
 * network failed. Screens branch on the two, which is why this is a type guard rather than a
 * cast.
 */
export function isApiError(error: unknown): error is ApiResponse<unknown> {
  return (
    typeof error === 'object' &&
    error !== null &&
    'success' in error &&
    (error as ApiResponse).success === false
  );
}

/**
 * The envelope carried by a failed HTTP call, wherever it happens to be.
 *
 * THREE SHAPES ARRIVE HERE and only one of them used to be handled:
 *
 *  1. The plain envelope, when something rethrows the parsed body itself.
 *  2. An `HttpErrorResponse` the auth interceptor has already enriched — it defines `message`,
 *     `errorCode` and `validationErrors` on the response object.
 *  3. A bare `HttpErrorResponse` that never passed through the interceptor, whose body is the
 *     envelope, sitting at `.error`.
 *
 * Only the first was recognised. Angular throws an `HttpErrorResponse`, which has no `success`
 * property of its own and — the part that made this invisible — DOES NOT EXTEND `Error`, so both
 * branches missed and every caller fell through to its fallback. The interceptor was carefully
 * attaching a friendly message that nothing could read.
 *
 * The visible consequence was a screen that said "The organisation could not be created." while
 * the server had answered 409 with a precise reason. Nobody could act on it, because the sentence
 * naming the problem was thrown away one function before it was displayed.
 */
function envelopeOf(error: unknown): Partial<ApiResponse> | null {
  if (!error || typeof error !== 'object') {
    return null;
  }

  const candidate = error as Record<string, unknown>;

  if ('success' in candidate && candidate['success'] === false) {
    return candidate as Partial<ApiResponse>;
  }

  const body = candidate['error'];

  if (body && typeof body === 'object' && 'success' in (body as Record<string, unknown>)) {
    return body as Partial<ApiResponse>;
  }

  return null;
}

/** The message to show a person for any thrown error, with a sensible fallback. */
export function apiErrorMessage(error: unknown, fallback = 'Something went wrong. Please try again.'): string {
  const envelope = envelopeOf(error);

  if (envelope?.message) {
    return envelope.message;
  }

  // The interceptor's enrichment, which is a plain string property on the response object.
  if (error && typeof error === 'object') {
    const message = (error as Record<string, unknown>)['message'];

    if (typeof message === 'string' && message.length > 0) {
      return message;
    }
  }

  if (error instanceof Error && error.message) {
    return error.message;
  }

  return fallback;
}

/**
 * The error code behind a failure, for the few screens that branch on one.
 *
 * Worth having beside the message: "SUBDOMAIN_UNAVAILABLE" tells a developer reading a support
 * report exactly which guard refused, where the sentence alone can be ambiguous.
 */
export function apiErrorCode(error: unknown): string | null {
  const envelope = envelopeOf(error);

  if (envelope?.errorCode) {
    return envelope.errorCode;
  }

  if (error && typeof error === 'object') {
    const code = (error as Record<string, unknown>)['errorCode'];

    if (typeof code === 'string' && code.length > 0) {
      return code;
    }
  }

  return null;
}

/** Field-level errors from a failed call, keyed by control name, ready for a reactive form. */
export function apiFieldErrors(error: unknown): Record<string, string> {
  const envelope = envelopeOf(error);

  // Same three shapes as apiErrorMessage. This returned an empty map for every
  // HttpErrorResponse, which meant NO FIELD-LEVEL VALIDATION MESSAGE HAS EVER BEEN SHOWN on any
  // form in the application: the server said which field was wrong and the form said nothing.
  const errors =
    envelope?.errors
    ?? (error && typeof error === 'object'
      ? ((error as Record<string, unknown>)['validationErrors'] as ValidationError[] | undefined)
      : undefined);

  if (!errors || errors.length === 0) {
    return {};
  }

  const map: Record<string, string> = {};

  for (const item of errors) {
    // First one wins: a control shows one message, and the first is the most specific the
    // server produced for that field.
    // The generated ValidationError types both fields as optional, so a row with no message
    // would otherwise write undefined into a Record<string, string>.
    if (item.field && !map[item.field]) {
      map[item.field] = item.message ?? 'That value is not valid.';
    }
  }

  return map;
}
