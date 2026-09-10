import { Pipe, PipeTransform } from '@angular/core';
import { readableIdentifier } from '../models/identifier';

/**
 * Renders a reference a person can use, and never a GUID.
 *
 * `{{ record.id | readableId }}` prints the id when it is something readable - a campaign code, a
 * user reference, an organisation code - and a plain sentence when it is a GUID. It exists so a
 * template can be made safe without the component behind it having to grow a computed for the
 * purpose, which is what makes it usable on the screens that render an id straight out of a
 * seeded list.
 *
 * PREFER A REAL NAME WHERE ONE CAN BE RESOLVED. This is the last line of defence, not the first:
 * a screen that can look the record up should show its NAME, and reach for this only for the
 * reference beside it.
 *
 * @example {{ conversation.id | readableId }}
 * @example {{ row.ownerId | readableId: 'Unassigned' }}
 */
@Pipe({ name: 'readableId', standalone: true })
export class ReadableIdPipe implements PipeTransform {
  transform(value: string | null | undefined, fallback = 'Not available'): string {
    return readableIdentifier(value, fallback);
  }
}
