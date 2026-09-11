import { Pipe, PipeTransform } from '@angular/core';
import { supportReference } from '../models/identifier';

/**
 * A correlation id as a person quotes it: eight characters, upper-case - "7F3A09C2".
 *
 * The full id stays in the API response and in the service log; this is only what is printed
 * beside "Quote this to support". See `supportReference` in Shared/models/identifier.
 *
 * @example {{ verification.supportCorrelationReference | supportRef }}
 */
@Pipe({ name: 'supportRef', standalone: true })
export class SupportReferencePipe implements PipeTransform {
  transform(value: string | null | undefined, fallback = '—'): string {
    return supportReference(value, fallback);
  }
}
