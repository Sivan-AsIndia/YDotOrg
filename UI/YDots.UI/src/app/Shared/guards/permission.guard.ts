import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { apiErrorMessage } from '../models/api-response.model';
import { AuthTokenService } from '../services/auth-token.service';
import { OrganisationContextService } from '../services/organisation-context.service';
import { ToastService } from '../services/toast.service';

/**
 * Route-level permission checks.
 *
 * THIS IS NAVIGATION, NOT SECURITY — and the distinction matters enough to state plainly. A guard
 * decides which Angular component renders. The permission list it reads came from the browser, and
 * anybody can edit it; somebody who does gets the component and nothing else, because every
 * endpoint behind it re-checks the same permission on the server. What the guard buys is that a
 * person who follows a stale bookmark lands somewhere that explains itself, instead of on a screen
 * that renders empty and looks broken.
 *
 * A SuperAdmin passes every check, matching the server, where global scope short-circuits the
 * permission lookup rather than being handed an enumerated list of every code.
 *
 * Usage:
 *
 *     { path: 'x', component: X, canActivate: [authGuard, requirePermission('iam.users.view')] }
 *
 * `authGuard` still comes first: "not signed in" and "signed in without this permission" are
 * different situations and lead to different places.
 */
export function requirePermission(...codes: string[]): CanActivateFn {
  return (_route, state) => {
    const tokens = inject(AuthTokenService);
    const router = inject(Router);

    if (!tokens.user()) {
      return router.createUrlTree(['/auth/sign-in'], {
        queryParams: { returnUrl: state.url },
      });
    }

    // Any one of the codes is enough. A screen that serves two purposes — a directory somebody
    // may read and somebody else may administer — should open for both.
    if (tokens.hasAnyPermission(...codes)) {
      return true;
    }

    return router.createUrlTree(['/app/access-denied'], {
      queryParams: { returnUrl: state.url, required: codes.join(',') },
    });
  };
}

/**
 * Restricts a route to a global-scope caller.
 *
 * For the genuinely platform-level screens — the BusinessUnit, the permission catalogue, the
 * platform audit trail — which have no meaning inside a single Organisation.
 */
export const superAdminGuard: CanActivateFn = (_route, state) => {
  const tokens = inject(AuthTokenService);
  const router = inject(Router);

  if (!tokens.user()) {
    return router.createUrlTree(['/auth/sign-in'], { queryParams: { returnUrl: state.url } });
  }

  if (tokens.isSuperAdmin()) {
    return true;
  }

  return router.createUrlTree(['/app/access-denied'], { queryParams: { returnUrl: state.url } });
};

/**
 * Requires that the session is operating INSIDE an Organisation.
 *
 * A root user who has not yet chosen one is sent to the picker rather than to an access-denied
 * page: they are perfectly entitled to be there, they simply have not said where "there" is, and
 * the Organisation-scoped screens have nothing to show until they do.
 */
export const organisationContextGuard: CanActivateFn = (_route, state) => {
  const tokens = inject(AuthTokenService);
  const router = inject(Router);

  if (!tokens.user()) {
    return router.createUrlTree(['/auth/sign-in'], { queryParams: { returnUrl: state.url } });
  }

  if (tokens.tenant()?.tenantId) {
    return true;
  }

  return router.createUrlTree(['/auth/select-organisation'], {
    queryParams: { returnUrl: state.url },
  });
};

/**
 * Leaves the Organisation on the way into a PLATFORM screen.
 *
 * THE BUG THIS FIXES. Entering an Organisation swaps the sidebar for that Organisation's menu and
 * takes the platform branch away — which is correct, and which is also why the switcher keeps a
 * "Manage organisations" link, why the browser has a Back button, and why bookmarks exist. All
 * three walked a SuperAdmin onto Manage Organisations with the token still naming TEN001, so the
 * screen said "every organisation on the platform" and the sidebar beside it said TEN001. The menu
 * was not stale in the sense of being out of date; it was accurate about a context the person
 * believed they had left.
 *
 * SO ARRIVING HERE IS TREATED AS LEAVING. These routes have no meaning inside a single
 * Organisation — the directory lists all of them, the catalogues are platform-wide, the audit
 * trail is the platform's. Asking to see one IS the intent to step back out, and the alternative,
 * refusing the navigation and explaining why, makes somebody perform a separate ritual to reach a
 * screen they just asked for.
 *
 * IT DOES NOTHING FOR EVERYBODY ELSE. `isActingInOrganisation` is only ever true for a global
 * caller standing inside an Organisation. A TenantAdmin never satisfies it, and the permission
 * guard beside this one is what keeps them off these routes in the first place.
 *
 * A FAILED EXIT STILL LETS THEM THROUGH, with a toast. The person asked for the screen, the server
 * re-checks every read behind it regardless of the token's Organisation, and stranding them on the
 * previous page with no explanation would be the worse of the two outcomes. The toast is there
 * because the sidebar will still be showing the Organisation they meant to leave.
 */
export const platformScopeGuard: CanActivateFn = () => {
  const tokens = inject(AuthTokenService);
  const organisations = inject(OrganisationContextService);
  const toast = inject(ToastService);

  if (!tokens.isActingInOrganisation()) {
    return true;
  }

  const leaving = tokens.organisationName();

  return organisations.exitToPlatform().pipe(
    map(() => true),
    catchError((error: unknown) => {
      toast.show(
        'Still inside ' + leaving,
        apiErrorMessage(error, 'That organisation could not be left, so the menu still belongs to it.'),
        'warning');

      return of(true);
    }),
  );
};
