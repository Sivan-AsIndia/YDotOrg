import { Injectable, signal } from '@angular/core';

/**
 * Resolves the Organisation the currently logged-in Organisation Owner belongs to.
 *
 * This codebase's existing Login/User Management is UI-only (no backend session/JWT
 * carrying a real user↔organisation relationship), so there is nothing to bind to yet.
 * This service is the single place that stands in for that lookup — the Owner screen
 * asks it "which organisation is mine?" instead of ever reading an organisation ID out
 * of the URL, which is what Section 18/19 of the Owner spec requires ("never allow an
 * Owner to access another Organisation by changing an ID in the request"). When the real
 * User Management system exposes a logged-in user's organisation, only this file's
 * `currentOrganisationId` needs to be wired to it — nothing in the Owner screen changes.
 */
@Injectable({ providedIn: 'root' })
export class OwnerSessionService {
  readonly currentOrganisationId = signal('ORG-000005');
  readonly currentOwnerName = signal('Ravi Menon');
}
