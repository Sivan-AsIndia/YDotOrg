import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, OutcomeResponse } from '../Shared/models/api-response.model';
import {
  ChangePasswordRequest,
  ConfirmMfaEnrolmentRequest,
  MfaEnrolmentResponse,
  MfaMethodType,
  RecoveryCodesResponse,
  UserSecurityResponse,
} from '../Shared/models/iam-contract.model';

/**
 * Security: the caller's own, and — separately — somebody else's.
 *
 * THE TWO HALVES ARE DELIBERATELY DIFFERENT SHAPES. Everything under `/my-security` takes no
 * user id at all: the server acts on whoever holds the token, so those calls cannot be aimed at
 * another person however the request is built. Everything under `/users/{id}` takes an id and is
 * permission-gated on the server. Collapsing them into one set of methods with a "self" flag
 * would put both behind one code path, and the flag would become the only thing between a
 * person and everybody else's sessions.
 *
 * NOTHING HERE DECIDES WHETHER AN ACTION IS ALLOWED. The screens hide buttons the caller has no
 * permission for, and that is a courtesy, not a control — the server refuses regardless.
 */
@Injectable({ providedIn: 'root' })
export class SecurityApiService {
  private readonly http = inject(HttpClient);
  private readonly mine = `${environment.apiBaseUrl}/my-security`;
  private readonly users = `${environment.apiBaseUrl}/users`;

  // =========================================================================================
  // My own account
  // =========================================================================================

  /** Sessions, devices, factors and recent sign-in activity for the caller. */
  getMySecurity(): Observable<UserSecurityResponse> {
    return this.http
      .get<ApiResponse<UserSecurityResponse>>(this.mine)
      .pipe(map((response) => response.data!));
  }

  /**
   * Starts enrolling a second factor.
   *
   * The shared secret comes back exactly once, here, so it can be scanned. The factor is
   * created pending and stays unusable until `confirmMfa` proves a code from it works — which
   * is what stops somebody enrolling a factor they cannot actually use and locking themselves
   * out of their own account.
   */
  beginMfaEnrolment(methodType: MfaMethodType, label?: string):
    Observable<MfaEnrolmentResponse> {
    return this.http
      .post<ApiResponse<MfaEnrolmentResponse>>(`${this.mine}/mfa/begin`, { methodType, label })
      .pipe(map((response) => response.data!));
  }

  confirmMfaEnrolment(methodId: string, code: string): Observable<OutcomeResponse> {
    const request: ConfirmMfaEnrolmentRequest = { code };
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.mine}/mfa/${methodId}/confirm`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Removes one of my factors.
   *
   * Refused by the server when it is the last one and the Organisation requires MFA: the
   * request would otherwise leave the account unable to sign in at all.
   */
  revokeMyMfaMethod(methodId: string, reason?: string): Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(`${this.mine}/mfa/${methodId}`, { body: { reason } })
      .pipe(map((response) => response.data!));
  }

  /**
   * Issues a fresh batch of backup codes and invalidates every earlier one.
   *
   * The plaintext appears in this response and nowhere else, ever. A printed sheet from last
   * year stops working the moment a new batch is generated.
   */
  generateRecoveryCodes(): Observable<RecoveryCodesResponse> {
    return this.http
      .post<ApiResponse<RecoveryCodesResponse>>(`${this.mine}/recovery-codes`, {})
      .pipe(map((response) => response.data!));
  }

  /**
   * Ends one of my own sessions.
   *
   * The narrow version of signing out, and the reason the session list shows a device, a place
   * and a last-active time: somebody has to be able to spot the one that is not theirs and end
   * that one, rather than signing themselves out of the page they are working in.
   */
  revokeMySession(sessionId: string, reason?: string): Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(`${this.mine}/sessions/${sessionId}`,
        { body: { reason } })
      .pipe(map((response) => response.data!));
  }

  /** Forgets one of my remembered devices, so it is challenged again next time. */
  revokeMyTrustedDevice(deviceId: string, reason?: string): Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(`${this.mine}/trusted-devices/${deviceId}`,
        { body: { reason } })
      .pipe(map((response) => response.data!));
  }

  /**
   * Changes my own password.
   *
   * The current one is required, and that is not ceremony: without it, anybody who found an
   * unlocked machine could change the password and lock the owner out of their own account.
   */
  changeMyPassword(request: ChangePasswordRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.users}/change-password`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Somebody else's account
  // =========================================================================================

  /** The security position of one account. Permission-gated on the server. */
  getUserSecurity(userId: string): Observable<UserSecurityResponse> {
    return this.http
      .get<ApiResponse<UserSecurityResponse>>(`${this.users}/${userId}/security`)
      .pipe(map((response) => response.data!));
  }

  /**
   * Ends ONE of somebody's sessions.
   *
   * Signing a person out of everything is the right answer to a compromised account and the
   * wrong one to a laptop left at an airport. Both exist so the response can match what
   * actually happened.
   */
  revokeUserSession(userId: string, sessionId: string, reason: string):
    Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(`${this.users}/${userId}/sessions/${sessionId}`,
        { body: { reason } })
      .pipe(map((response) => response.data!));
  }

  /** Ends every session a person has. */
  forceSignOut(userId: string, reason: string): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.users}/${userId}/force-sign-out`, { reason })
      .pipe(map((response) => response.data!));
  }

  revokeUserTrustedDevice(userId: string, deviceId: string, reason: string):
    Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(
        `${this.users}/${userId}/trusted-devices/${deviceId}`, { body: { reason } })
      .pipe(map((response) => response.data!));
  }

  /**
   * Clears every second factor so the person enrols again.
   *
   * For the lost phone with the authenticator on it: they cannot complete MFA, and they cannot
   * remove the factor themselves because removing it needs a code from it. Sessions, remembered
   * devices and backup codes go with the factors — leaving any live would leave a way round it.
   */
  resetUserMfa(userId: string, reason: string): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.users}/${userId}/reset-mfa`, { reason })
      .pipe(map((response) => response.data!));
  }

  unlockUser(userId: string, expectedVersion: number, reason: string):
    Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.users}/${userId}/unlock`,
        { expectedVersion, reason })
      .pipe(map((response) => response.data!));
  }

  /**
   * An administrator resetting somebody's password.
   *
   * The link is the default and the temporary password is not, because a temporary password has
   * to be read out over some channel and in practice that is the same channel it was meant to
   * protect.
   */
  resetUserPassword(
    userId: string,
    expectedVersion: number,
    options: { sendResetLink?: boolean; requireChangeOnNextSignIn?: boolean;
               signOutAllSessions?: boolean } = {},
  ): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.users}/${userId}/reset-password`, {
        expectedVersion,
        sendResetLink: options.sendResetLink ?? true,
        requireChangeOnNextSignIn: options.requireChangeOnNextSignIn ?? true,
        signOutAllSessions: options.signOutAllSessions ?? false,
      })
      .pipe(map((response) => response.data!));
  }

  /**
   * The security position of one account as a file.
   *
   * Fetched as a blob rather than JSON: it is a CSV the server builds and audits, with a
   * reference on the response header, so a spreadsheet found on somebody's desktop months later
   * traces back to who produced it.
   */
  exportUserSecurity(userId: string): Observable<Blob> {
    return this.http.get(`${this.users}/${userId}/security/export`, { responseType: 'blob' });
  }

  /**
   * A person's password-reset link, sent to them by e-mail.
   *
   * ANONYMOUS AND ALWAYS THE SAME ANSWER. The server does not say whether the address is known,
   * because an endpoint that does is a way to find out who holds an account.
   */
  requestPasswordReset(email: string): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.users}/forgot-password`, { email })
      .pipe(map((response) => response.data!));
  }

  /** The password rules, so the form states them rather than guessing. */
  getPasswordPolicy(): Observable<Record<string, unknown>> {
    return this.http
      .get<ApiResponse<Record<string, unknown>>>(`${this.users}/password-policy`)
      .pipe(map((response) => response.data ?? {}));
  }

  /** Is this e-mail or username free? Used before asking for an identifier change. */
  checkIdentity(email?: string, username?: string, excludeUserId?: string):
    Observable<{ isAvailable?: boolean; emailAvailable?: boolean; usernameAvailable?: boolean;
                 message?: string | null; suggestions?: string[] | null }> {
    return this.http
      .post<ApiResponse<{ isAvailable?: boolean; emailAvailable?: boolean;
                          usernameAvailable?: boolean; message?: string | null;
                          suggestions?: string[] | null }>>(
        `${this.users}/check-identity`, { email, username, excludeUserId })
      .pipe(map((response) => response.data!));
  }

  private params(values: Record<string, string | number | boolean | undefined>): HttpParams {
    let params = new HttpParams();

    Object.entries(values).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    });

    return params;
  }
}
