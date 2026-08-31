import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthTokenService } from '../services/auth-token.service';

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
