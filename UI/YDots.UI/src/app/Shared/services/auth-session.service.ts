import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthApiService } from '../../Service/auth-api.service';
import { AuthTokenService } from './auth-token.service';
import { ToastService } from './toast.service';
import { SignInResponse } from '../models/auth.model';

/**
 * Owns the *lifetime* of a signed-in session in the browser.
 *
 * TWO CLOCKS, NOT ONE
 * -------------------
 * • **The access token's clock** is the server's. It expires in about fifteen minutes and the
 *   interceptor renews it silently. The person never sees it.
 *
 * • **The idle clock** is this service's, and it is about the human, not the token. If nobody
 *   has touched the keyboard or mouse for `sessionIdleMinutes`, the app stops trusting that the
 *   right person is still at the screen and sends them to /auth/reauthenticate. An unattended
 *   laptop in a shared office is exactly the risk this covers.
 *
 * The idle clock is a courtesy, not a security boundary — anyone can edit browser memory. The
 * real enforcement is the server's short token lifetime and its own session rules. This layer
 * exists so the app behaves sensibly, not so it is safe.
 */
@Injectable({ providedIn: 'root' })
export class AuthSessionService {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly tokens = inject(AuthTokenService);
  private readonly authApi = inject(AuthApiService);

  private static readonly LAST_ACTIVITY_KEY = 'ydot.lastActivityAt';
  private static readonly ACTIVITY_EVENTS = ['click', 'keydown', 'mousemove', 'scroll', 'touchstart'];

  /** Set while the app is waiting for the person to confirm it is still them. */
  private readonly reauthRequiredState = signal(false);
  readonly reauthRequired = this.reauthRequiredState.asReadonly();

  private idleTimer: ReturnType<typeof setInterval> | null = null;
  private listenersAttached = false;

  // =========================================================================================
  // Starting and ending
  // =========================================================================================

  /**
   * Stores the tokens and identity from a successful sign-in, MFA verification or activation,
   * and starts the idle clock. Call this once, from whichever screen finished the sign-in.
   */
  startSession(response: SignInResponse): void {
    // storeSignIn takes the whole response: the token, the identity and the Organisation the
    // session is operating in all arrive together and are stored together, so a screen can
    // never end up with an identity but no Organisation to render beside it.
    this.tokens.storeSignIn(response);

    this.reauthRequiredState.set(false);
    this.markActivity();
    this.attachActivityListeners();
    this.startIdleTimer();
  }

  /** Called after a successful reauthentication: new tokens, clock reset, back to work. */
  resumeSession(): void {
    this.reauthRequiredState.set(false);
    this.markActivity();
    this.startIdleTimer();
    this.toast.show('Welcome back', 'Your session has been confirmed.', 'success');
  }

  /**
   * Signs out. The server call revokes the session and clears the HttpOnly cookie; the local
   * clear-out happens either way, so a network failure can never strand somebody signed in.
   */
  endSession(signOutEverywhere = false): void {
    const finish = () => {
      this.tokens.clear();
      localStorage.removeItem(AuthSessionService.LAST_ACTIVITY_KEY);
      this.reauthRequiredState.set(false);
      this.stopIdleTimer();
      void this.router.navigate(['/auth/sign-in']);
    };

    if (this.tokens.getAccessToken()) {
      this.authApi.signOut(signOutEverywhere).subscribe({ next: finish, error: finish });
      return;
    }

    finish();
  }

  // =========================================================================================
  // Queries used by the guard and the shell
  // =========================================================================================

  isAuthenticated(): boolean {
    // A stale access token is fine on its own: the interceptor renews it from the cookie on the
    // next call. What matters here is whether an identity is held at all.
    return this.tokens.user() !== null && !this.reauthRequiredState();
  }

  isReauthRequired(): boolean {
    return this.reauthRequiredState();
  }

  /** True when nobody has interacted for longer than the configured idle window. */
  isIdleTimedOut(): boolean {
    const last = Number(localStorage.getItem(AuthSessionService.LAST_ACTIVITY_KEY) ?? 0);
    if (!last) {
      return false;
    }

    return Date.now() - last > environment.sessionIdleMinutes * 60_000;
  }

  /** Milliseconds left before the idle window closes. Drives the countdown on the shell. */
  get remainingIdleMs(): number {
    const last = Number(localStorage.getItem(AuthSessionService.LAST_ACTIVITY_KEY) ?? 0);
    if (!last) {
      return environment.sessionIdleMinutes * 60_000;
    }

    return Math.max(0, last + environment.sessionIdleMinutes * 60_000 - Date.now());
  }

  /** Marks the session as needing a fresh confirmation of identity. */
  requireReauth(reason = 'Your session has been idle.'): void {
    if (this.reauthRequiredState()) {
      return;
    }

    this.reauthRequiredState.set(true);
    this.stopIdleTimer();
    this.toast.show('Confirm it is you', reason, 'warning');
    void this.router.navigate(['/auth/reauthenticate']);
  }

  // =========================================================================================
  // Idle tracking
  // =========================================================================================

  private attachActivityListeners(): void {
    if (this.listenersAttached) {
      return;
    }

    AuthSessionService.ACTIVITY_EVENTS.forEach((event) => {
      window.addEventListener(event, this.onUserActivity, { passive: true });
    });

    this.listenersAttached = true;
  }

  private readonly onUserActivity = (): void => {
    if (!this.tokens.user() || this.reauthRequiredState()) {
      return;
    }

    // Mouse movement fires constantly, so the timestamp is only rewritten every thirty seconds.
    // Without that throttle this would be one localStorage write per animation frame.
    const last = Number(localStorage.getItem(AuthSessionService.LAST_ACTIVITY_KEY) ?? 0);
    if (Date.now() - last > 30_000) {
      this.markActivity();
    }
  };

  private markActivity(): void {
    localStorage.setItem(AuthSessionService.LAST_ACTIVITY_KEY, String(Date.now()));
  }

  private startIdleTimer(): void {
    this.stopIdleTimer();

    // Once a minute is precise enough for a window measured in tens of minutes, and costs
    // nothing. A per-second timer would keep the tab awake for no benefit.
    this.idleTimer = setInterval(() => {
      if (!this.tokens.user() || this.reauthRequiredState()) {
        return;
      }

      if (this.isIdleTimedOut()) {
        this.requireReauth('Your session has been idle for a while.');
      }
    }, 60_000);
  }

  private stopIdleTimer(): void {
    if (this.idleTimer) {
      clearInterval(this.idleTimer);
      this.idleTimer = null;
    }
  }
}
