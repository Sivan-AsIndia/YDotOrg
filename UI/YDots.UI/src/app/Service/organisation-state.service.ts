/**
 * The organisation workspace state.
 *
 * THIS FILE HELD A SECOND COPY of the store in `Shared/services`, and the two had drifted apart -
 * three screens listed one set of organisations and a fourth listed another, both of them seeded.
 * There is now one implementation; this re-exports it so the existing imports keep working.
 */
export { OrganisationStateService } from '../Shared/services/organisation-state.service';

export type {
  CreateOrganisationInput,
  EditableOrganisationFields,
} from '../Shared/services/organisation-state.service';
