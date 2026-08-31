import { Injectable, computed, signal } from '@angular/core';
import {
  AuthenticatedUserResponse,
  SelectTenantResponse,
  SignInResponse,
  TenantContextResponse,
} from '../models/auth.model';

/**
 * The one place the browser keeps anything to do with the signed-in identity.
 *
 * WHERE EACH TOKEN LIVES, AND WHY
 * -------------------------------
 * • **Access token → `sessionStorage`.** Every API call needs it in an `Authorization` header, so
 *   JavaScript has to be able to read it. It expires in about fifteen minutes, which keeps the
 *   damage from a stolen copy small. `sessionStorage` — not `localStorage` — because it is wiped
 *   when the tab closes and is not shared with other tabs, so an abandoned machine does not leave
 *   a usable token behind.
 *
 * • **Refresh token → HttpOnly cookie, which this file never touches.** It lives for days and can
 *   mint new access tokens, so it is the valuable one. The server sets it with `HttpOnly`, which
 *   makes it invisible to `document.cookie` and therefore invisible to any injected script. The
 *   browser attaches it automatically because the interceptor sets `withCredentials: true`. There
 *   is deliberately no code here to read or write it — if JavaScript can read a token, so can an
 *   attacker who gets a script onto the page.
 *
 * THE ORGANISATION IS HELD HERE TOO, AND IT IS A CACHE — NOT A DECISION
 * ---------------------------------------------------------------------
 * `tenant` is what the server said the session is operating in, kept so the shell can render the
 * name and the "acting as" banner without a round trip. It is never sent back as a parameter and
 * never consulted to decide what may be read: the Organisation that governs a request is the one
 * inside the signed token, which the browser cannot alter without invalidating the signature.
 * Editing this value in dev tools changes a label and nothing else.
 */
@Injectable({ providedIn: 'root' })
export class AuthTokenService {
  private static readonly ACCESS_TOKEN_KEY = 'ydot.accessToken';
  private static readonly EXPIRES_AT_KEY = 'ydot.accessTokenExpiresAt';
  private static readonly SESSION_ID_KEY = 'ydot.sessionId';
  private static readonly USER_KEY = 'ydot.user';
  private static readonly TENANT_KEY = 'ydot.tenant';

  /** The signed-in identity, or null. Read this in templates. */
  private readonly userState = signal<AuthenticatedUserResponse | null>(
    this.read<AuthenticatedUserResponse>(AuthTokenService.USER_KEY));
  readonly user = this.userState.asReadonly();

  /** The Organisation the session is currently operating in, or null at global scope. */
  private readonly tenantState = signal<TenantContextResponse | null>(
    this.read<TenantContextResponse>(AuthTokenService.TENANT_KEY));
  readonly tenant = this.tenantState.asReadonly();

  /** True when a non-expired access token is held. */
  readonly isSignedIn = computed(() => this.userState() !== null && !this.isAccessTokenExpired());

  readonly displayName = computed(() => this.userState()?.displayName ?? '');
  readonly email = computed(() => this.userState()?.email ?? '');
  readonly permissions = computed(() => this.userState()?.permissions ?? []);
  readonly roles = computed(() => this.userState()?.roles ?? []);
  readonly isSuperAdmin = computed(() => this.userState()?.isSuperAdmin === true);
  readonly isTenantAdmin = computed(() => this.userState()?.isTenantAdmin === true);
  readonly mustChangePassword = computed(() => this.userState()?.mustChangePassword === true);

  readonly organisationName = computed(() => this.tenantState()?.tenantName ?? '');
  readonly organisationCode = computed(() => this.tenantState()?.tenantCode ?? '');

  /**
   * True when a global caller has stepped into an Organisation.
   *
   * The shell shows the "acting as" banner off this, so a root user is never in any doubt about
   * whose data is on the screen — which is the difference between a deliberate administrative
   * action and an accident.
   */
  readonly isActingInOrganisation = computed(() => this.tenantState()?.isTenantMode === true);

  // =========================================================================================
  // Writing
  // =========================================================================================

  /**
   * Stores everything a successful sign-in produced.
   *
   * `refreshToken` on the response is deliberately ignored: the server already sent the real one
   * as an HttpOnly cookie and blanked the field in the body. Writing it anywhere here would undo
   * that protection.
   */
  storeSignIn(response: SignInResponse): void {
    if (response.accessToken) {
      this.storeAccessToken(
        response.accessToken,
        response.accessTokenExpiresAtUtc ?? null,
        response.sessionId ?? null);
    }

    if (response.user) {
      this.storeUser(response.user);
    }

    if (response.tenant) {
      this.storeTenant(response.tenant);
    }
  }

  /** Stores the re-scoped token issued when a global caller picks an Organisation. */
  storeTenantSelection(response: SelectTenantResponse): void {
    if (response.accessToken) {
      this.storeAccessToken(
        response.accessToken,
        response.accessTokenExpiresAtUtc ?? null,
        response.sessionId ?? null);
    }

    if (response.user) {
      this.storeUser(response.user);
    }

    if (response.tenant) {
      this.storeTenant(response.tenant);
    }
  }

  storeAccessToken(accessToken: string, expiresAtUtc: string | null, sessionId: string | null): void {
    sessionStorage.setItem(AuthTokenService.ACCESS_TOKEN_KEY, accessToken);

    if (expiresAtUtc) {
      sessionStorage.setItem(AuthTokenService.EXPIRES_AT_KEY, expiresAtUtc);
    }

    if (sessionId) {
      sessionStorage.setItem(AuthTokenService.SESSION_ID_KEY, sessionId);
    }
  }

  storeUser(user: AuthenticatedUserResponse): void {
    sessionStorage.setItem(AuthTokenService.USER_KEY, JSON.stringify(user));
    this.userState.set(user);
  }

  storeTenant(tenant: TenantContextResponse | null): void {
    if (tenant) {
      sessionStorage.setItem(AuthTokenService.TENANT_KEY, JSON.stringify(tenant));
    } else {
      sessionStorage.removeItem(AuthTokenService.TENANT_KEY);
    }

    this.tenantState.set(tenant);
  }

  /** Wipes everything this app put in the browser. The cookie is cleared by the server. */
  clear(): void {
    [
      AuthTokenService.ACCESS_TOKEN_KEY,
      AuthTokenService.EXPIRES_AT_KEY,
      AuthTokenService.SESSION_ID_KEY,
      AuthTokenService.USER_KEY,
      AuthTokenService.TENANT_KEY,
    ].forEach((key) => sessionStorage.removeItem(key));

    this.userState.set(null);
    this.tenantState.set(null);

    // Older builds of this app kept tokens in localStorage. Removing them here means an upgrade
    // does not leave a long-lived token sitting on disk from the previous version.
    ['accessToken', 'refreshToken', 'sessionId', 'userData', 'ydotSession', 'trustedDevice']
      .forEach((key) => localStorage.removeItem(key));
  }

  // =========================================================================================
  // Reading
  // =========================================================================================

  getAccessToken(): string | null {
    return sessionStorage.getItem(AuthTokenService.ACCESS_TOKEN_KEY);
  }

  getSessionId(): string | null {
    return sessionStorage.getItem(AuthTokenService.SESSION_ID_KEY);
  }

  /** Milliseconds until the access token expires. Zero when there is none, or it has gone. */
  millisecondsUntilExpiry(): number {
    const raw = sessionStorage.getItem(AuthTokenService.EXPIRES_AT_KEY);
    if (!raw) {
      return 0;
    }

    const expiresAt = Date.parse(raw);
    return Number.isNaN(expiresAt) ? 0 : Math.max(0, expiresAt - Date.now());
  }

  isAccessTokenExpired(): boolean {
    return this.millisecondsUntilExpiry() <= 0;
  }

  /**
   * True when the token is still valid but close enough to expiry that it is worth renewing
   * before the next call, rather than letting a request fail with a 401 first.
   */
  shouldRefreshSoon(leadSeconds: number): boolean {
    const remaining = this.millisecondsUntilExpiry();
    return remaining > 0 && remaining < leadSeconds * 1000;
  }

  /**
   * Whether the caller holds a permission.
   *
   * FOR HIDING BUTTONS, NEVER FOR PROTECTING DATA. The list came from the browser and anybody
   * can edit it; the API re-checks every permission on every request, which is what actually
   * stops an action. Hiding a control the person cannot use is a courtesy, not a control.
   *
   * A SuperAdmin holds everything by virtue of scope rather than by an enumerated list, so the
   * check short-circuits for them exactly as the server's does.
   */
  hasPermission(code: string): boolean {
    return this.isSuperAdmin() || this.permissions().includes(code);
  }

  hasAnyPermission(...codes: string[]): boolean {
    return this.isSuperAdmin() || codes.some((code) => this.permissions().includes(code));
  }

  hasAllPermissions(...codes: string[]): boolean {
    return this.isSuperAdmin() || codes.every((code) => this.permissions().includes(code));
  }

  hasAnyRole(...codes: string[]): boolean {
    const held = this.roles();
    return codes.some((code) => held.includes(code));
  }

  private read<T>(key: string): T | null {
    const raw = sessionStorage.getItem(key);
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw) as T;
    } catch {
      // Corrupt entry: drop it rather than crash the whole app on start-up.
      sessionStorage.removeItem(key);
      return null;
    }
  }
}
