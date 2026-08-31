/**
 * The acting session.
 *
 * THIS FILE HELD A SECOND COPY of the mock session that lived in `Shared/services`, with its own
 * hard-coded profile list. Three files imported this one and the rest imported the other, so two
 * campaign screens were asking a different object what the caller could do - and the two arrays
 * had already drifted apart.
 *
 * There is now one implementation. This re-exports it so the existing imports keep working.
 */
export {
  CurrentUserService,
  type CampaignRole,
  type CamPermissionCode,
} from '../Shared/services/current-user.service';
