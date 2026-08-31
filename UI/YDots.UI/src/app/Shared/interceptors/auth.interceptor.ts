import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, Observable, catchError, filter, switchMap, take, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response.model';
import { AuthTokenService } from '../services/auth-token.service';

/**
 * The single place every HTTP call to the IAM API passes through.
 *
 * IT DOES FOUR THINGS
 * -------------------
 * 1. **Attaches the access token.** `Authorization: Bearer …` on every API call, so no component
 *    ever has to remember to add it — which is how endpoints quietly end up unauthenticated.
 *
 * 2. **Turns on `withCredentials`.** This is what makes the browser send the HttpOnly refresh
 *    cookie. Without it the cookie exists but is never attached, and every refresh fails.
 *
 * 3. **Renews the token once on a 401, then replays the request.** The access token lives about
 *    fifteen minutes; without this, a person filling in a long form would be thrown out mid-save.
 *
 * 4. **Unwraps the error envelope.** The API answers failures with
 *    `{ success:false, message, errorCode, errors }`. Components should be able to read
 *    `err.message` without digging, so a readable message and the field errors are copied onto
 *    the error object.
 *
 * WHY THE REFRESH IS SHARED
 * -------------------------
 * A dashboard can fire six calls at once. If all six get a 401 and all six call refresh, five of
 * them present a token that the first one has already rotated away — and the server treats a
 * replayed refresh token as theft and kills the whole session. `refreshInFlight` makes the first
 * 401 do the work while the others queue on `refreshedToken$` and replay afterwards.
 */

/** True while a refresh is running, so parallel 401s wait instead of racing. */
let refreshInFlight = false;

/** Emits the new access token when a refresh finishes; null while one is in progress. */
const refreshedToken$ = new BehaviorSubject<string | null>(null);

/** Endpoints that must never carry a bearer token or trigger a refresh loop. */
const ANONYMOUS_PATHS = [
  '/users/sign-in',
  '/users/forgot-password',
  '/users/reset-password',
  '/users/accept-invitation-and-activate-account',
  '/users/mfa-challenge',
  '/users/account-unavailable-and-recovery-guidance',
  '/users/tokens/refresh',

  // THE PUBLIC DONATION FLOW. A donor with a QR code has no account and no token, so these
  // must not carry one - and a 401 from them is a real answer, not an expired session, so
  // they must not trigger a refresh either. A refresh attempt here would send an anonymous
  // visitor to the sign-in page in the middle of giving money.
  '/public/donations',
];

/**
 * Every service base the token is attached to.
 *
 * IT IS A LIST BECAUSE THE PLATFORM IS FOUR SERVICES. IAM signs the token; CAM, DON and PAY
 * validate the same one. Checking only `environment.apiBaseUrl` would send every campaign,
 * donor and payment call out with no Authorization header, and each would answer 401 with
 * nothing in the console to explain why.
 *
 * The bare-path entries cover production, where all four are same-origin paths behind nginx.
 */
const API_BASES = [
  environment.apiBaseUrl,
  environment.campaignApiBaseUrl,
  environment.donorApiBaseUrl,
  environment.paymentApiBaseUrl,
  '/api/',
  '/cam-api/',
  '/don-api/',
  '/pay-api/',
];

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const tokens = inject(AuthTokenService);
  const router = inject(Router);

  // Local asset files are not API calls: no token, no cookie.
  const isApiCall = API_BASES.some((base) => base && request.url.startsWith(base));
  if (!isApiCall) {
    return next(request);
  }

  const isAnonymous = ANONYMOUS_PATHS.some((path) => request.url.includes(path));
  const prepared = attach(request, isAnonymous ? null : tokens.getAccessToken());

  return next(prepared).pipe(
    catchError((error: HttpErrorResponse) => {
      // 401 on an anonymous endpoint means "those credentials are wrong", not "your session
      // ended", so there is nothing to refresh — surface it to the screen as-is.
      //
      // A 401 WITH NO IDENTITY HELD IS THE SAME ANSWER, and this is the guard that was missing.
      // The public screens — activation, reset password, the donor flow — have no session by
      // definition. A renewal there cannot succeed: it fails, clears storage, and navigates to
      // sign-in. And because `router.url` already starts with /auth there is not even a
      // returnUrl, so somebody who followed an invitation link lands on a bare sign-in form with
      // no explanation and no way back. ONE incidental authenticated call on such a page — a
      // country list for a dialling-code picker was enough — bounced the whole screen.
      //
      // `tokens.user()` and not `isSignedIn()`: an expired access token with a live refresh
      // cookie is exactly the case renewal exists for, and it must still go through.
      if (error.status === 401 && !isAnonymous && tokens.user()) {
        return handleUnauthorised(request, next, tokens, router);
      }

      return throwError(() => describe(error));
    }),
  );
};

/** Adds the bearer token and turns on cookie sending. */
function attach(request: HttpRequest<unknown>, accessToken: string | null): HttpRequest<unknown> {
  return request.clone({
    // withCredentials is what makes the browser attach the HttpOnly refresh cookie on a
    // cross-origin call. It is harmless on same-origin calls, so it goes on everything.
    withCredentials: true,
    setHeaders: accessToken ? { Authorization: `Bearer ${accessToken}` } : {},
  });
}

/**
 * Renews the access token and replays the original request.
 * If the refresh itself fails the session really is over: everything is cleared and the person is
 * sent to sign-in with a `returnUrl`, so they land back where they were afterwards.
 */
function handleUnauthorised(
  request: HttpRequest<unknown>,
  next: HttpHandlerFn,
  tokens: AuthTokenService,
  router: Router,
): Observable<HttpEvent<unknown>> {
  if (refreshInFlight) {
    // Another call is already refreshing. Wait for its result, then replay this request with
    // the new token instead of asking for a second one.
    return refreshedToken$.pipe(
      filter((token): token is string => token !== null),
      take(1),
      switchMap((token) => next(attach(request, token))),
    );
  }

  refreshInFlight = true;
  refreshedToken$.next(null);

  // The API service is not injected here on purpose: doing so would route the refresh call back
  // through this same interceptor and risk a loop. A bare fetch keeps the path unambiguous.
  return refreshAccessToken(tokens).pipe(
    switchMap((accessToken) => {
      refreshInFlight = false;
      refreshedToken$.next(accessToken);
      return next(attach(request, accessToken));
    }),
    catchError((refreshError) => {
      refreshInFlight = false;
      refreshedToken$.next(null);
      tokens.clear();

      const returnUrl = router.url && !router.url.startsWith('/auth') ? router.url : undefined;
      void router.navigate(['/auth/sign-in'], returnUrl ? { queryParams: { returnUrl } } : undefined);

      return throwError(() => describe(refreshError));
    }),
  );
}

/**
 * Calls the refresh endpoint with `credentials: 'include'`, so the browser sends the HttpOnly
 * cookie, and stores the new access token. Returns the token so the caller can replay with it.
 */
function refreshAccessToken(tokens: AuthTokenService): Observable<string> {
  return new Observable<string>((subscriber) => {
    fetch(`${environment.apiBaseUrl}/users/tokens/refresh`, {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: '{}',
    })
      .then(async (response) => {
        const body = (await response.json()) as ApiResponse<{
          accessToken: string;
          accessTokenExpiresAtUtc: string;
          sessionId: string;
          refreshToken: string;
          tokenType: string;
          expiresInSeconds: number;
          refreshTokenExpiresAtUtc: string;
        }>;

        if (!response.ok || !body.success || !body.data) {
          subscriber.error(new Error(body.message ?? 'The session could not be renewed.'));
          return;
        }

        tokens.storeAccessToken(
          body.data.accessToken, body.data.accessTokenExpiresAtUtc, body.data.sessionId);
        subscriber.next(body.data.accessToken);
        subscriber.complete();
      })
      .catch((error) => subscriber.error(error));
  });
}

/**
 * Copies the API's message, error code and field errors onto the error object, so a component
 * can read `error.message` and `error.validationErrors` without knowing the envelope shape.
 */
function describe(error: HttpErrorResponse | Error): HttpErrorResponse | Error {
  if (!(error instanceof HttpErrorResponse)) {
    return error;
  }

  const envelope = error.error as ApiResponse | undefined;

  const friendly =
    envelope?.message ??
    (error.status === 0
      ? 'The server could not be reached. Check your connection and try again.'
      : error.status === 403
        ? 'You do not have permission to do that.'
        : error.status >= 500
          ? 'Something went wrong on the server. Please try again.'
          : 'The request could not be completed.');

  // HttpErrorResponse is read-only, so the extra fields are defined rather than assigned.
  Object.defineProperties(error, {
    message: { value: friendly, writable: true, configurable: true },
    errorCode: { value: envelope?.errorCode ?? null, writable: true, configurable: true },
    validationErrors: { value: envelope?.errors ?? [], writable: true, configurable: true },
    correlationId: { value: envelope?.correlationId ?? null, writable: true, configurable: true },
  });

  return error;
}
