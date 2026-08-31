import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthSessionService } from '../services/auth-session.service';
import { AuthTokenService } from '../services/auth-token.service';

/**
 * Decides whether a person may open a protected route.
 *
 * THIS IS CONVENIENCE, NOT SECURITY
 * ---------------------------------
 * A guard only decides which Angular component renders. Anyone can edit browser memory and walk
 * straight past it — but they still get nothing, because every screen is empty until the API
 * answers, and the API checks the JWT and the permission on every single request. The guard's job
 * is to send people somewhere sensible, not to protect data.
 *
 * Three outcomes:
 *   • no identity held        → /auth/sign-in, remembering where they were headed
 *   • idle for too long       → /auth/reauthenticate
 *   • otherwise               → allow
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const session = inject(AuthSessionService);
  const tokens = inject(AuthTokenService);
  const router = inject(Router);

  if (!tokens.user()) {
    // returnUrl means the person lands back on the page they asked for after signing in,
    // instead of being dumped on the dashboard.
    return router.createUrlTree(['/auth/sign-in'], { queryParams: { returnUrl: state.url } });
  }

  if (session.isReauthRequired() || session.isIdleTimedOut()) {
    return router.createUrlTree(['/auth/reauthenticate'], { queryParams: { returnUrl: state.url } });
  }

  return true;
};

/**
 * The mirror image, for the sign-in and recovery screens: somebody who is already signed in has
 * no business on the sign-in page, so send them to the dashboard instead.
 */
export const anonymousOnlyGuard: CanActivateFn = () => {
  const tokens = inject(AuthTokenService);
  const session = inject(AuthSessionService);
  const router = inject(Router);

  if (tokens.user() && !session.isReauthRequired() && !session.isIdleTimedOut()) {
    return router.createUrlTree(['/app/dashboard']);
  }

  return true;
};
