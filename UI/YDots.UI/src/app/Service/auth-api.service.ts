import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, OutcomeResponse } from '../Shared/models/api-response.model';
import {
  AcceptInvitationRequest,
  AcceptInvitationResponse,
  AccountRecoveryGuidanceResponse,
  AuthenticatedUserResponse,
  BeginInvitationMfaEnrolmentRequest,
  ChangePasswordRequest,
  ContactSupportRequest,
  ForgotPasswordRequest,
  ForgotPasswordResponse,
  InvitationPreviewResponse,
  MfaChallengeResponse,
  MfaEnrolmentResponse,
  NavigationResponse,
  PasswordOperationResponse,
  PasswordPolicyResponse,
  ReauthenticateRequest,
  ReauthenticateResponse,
  ReauthenticationViewResponse,
  RedeemRecoveryCodeRequest,
  ResetPasswordRequest,
  ResetPasswordViewResponse,
  SaveProtectedDraftRequest,
  SaveProtectedDraftResponse,
  SelectTenantResponse,
  SessionStatusResponse,
  SignInRequest,
  SignInResponse,
  TenantOptionResponse,
  TenantResolutionResponse,
  VerifyInvitationMfaEnrolmentRequest,
  VerifyMfaRequest,
} from '../Shared/models/auth.model';
import { AuthTokenService } from '../Shared/services/auth-token.service';
import { DeviceIdentityService } from '../Shared/services/device-identity.service';

/**
 * Every authentication call the app makes.
 *
 * TWO THINGS THIS SERVICE DOES THAT ARE EASY TO MISS
 * ---------------------------------------------------
 * **It stores what it receives.** Sign-in, MFA verification, recovery-code redemption, tenant
 * selection and reauthentication all return a token, and each one is written to
 * `AuthTokenService` here rather than in the component. Leaving it to the component means five
 * screens each remembering to do it, and the one that forgets produces a person who appears
 * signed in until their first API call fails.
 *
 * **It never sends an Organisation.** There is no `tenantId` parameter anywhere below, including
 * on sign-in. The Organisation comes from the host name the request arrived on, or from the
 * signed token — never from anything this client could set. The single exception is
 * `selectTenant`, where a global caller names the Organisation they want to step into; the
 * server checks that the caller is genuinely global before honouring it, and the answer is a new
 * signed token rather than a client-side flag.
 */
@Injectable({ providedIn: 'root' })
export class AuthApiService {
  private readonly http = inject(HttpClient);
  private readonly tokens = inject(AuthTokenService);
  private readonly device = inject(DeviceIdentityService);

  private readonly usersUrl = `${environment.apiBaseUrl}/users`;
  private readonly authUrl = `${environment.apiBaseUrl}/auth`;

  // =========================================================================================
  // IAM-AUTH-01 — sign in
  // =========================================================================================

  /**
   * Signs in.
   *
   * The device identifier is attached here rather than asked of every caller: it is how
   * "remember this device" survives a sign-out, and how the security screen can later show
   * somebody the machines their account has been used from.
   */
  signIn(payload: SignInRequest): Observable<SignInResponse> {
    const request: SignInRequest = {
      ...payload,
      deviceIdentifier: payload.deviceIdentifier ?? this.device.getDeviceIdentifier(),
      deviceName: payload.deviceName ?? this.device.getDeviceName(),
    };

    return this.http
      .post<ApiResponse<SignInResponse>>(`${this.usersUrl}/sign-in`, request)
      .pipe(
        map((response) => response.data!),
        tap((result) => this.tokens.storeSignIn(result)),
      );
  }

  /** Ends this session, or every session. Clears the refresh cookie server-side. */
  signOut(allDevices = false): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.usersUrl}/sign-out`, { allDevices })
      .pipe(
        map((response) => response.data!),
        tap(() => this.tokens.clear()),
      );
  }

  /**
   * Renews the access token.
   *
   * The refresh token is not passed: it travels as an HttpOnly cookie the browser attaches by
   * itself. That is the whole point of the cookie, and it is why this body is empty.
   */
  refreshToken(): Observable<SignInResponse> {
    return this.http
      .post<ApiResponse<SignInResponse>>(`${this.usersUrl}/tokens/refresh`, {})
      .pipe(
        map((response) => response.data!),
        tap((result) => this.tokens.storeSignIn(result)),
      );
  }

  /** The live session: idle countdown, absolute expiry, whether a step-up is due. */
  getSession(): Observable<SessionStatusResponse> {
    return this.http
      .get<ApiResponse<SessionStatusResponse>>(`${this.usersUrl}/session`)
      .pipe(map((response) => response.data!));
  }

  /** Who the caller is, plus the Organisation the session is operating in. */
  getCurrentUser(): Observable<SessionStatusResponse> {
    return this.http
      .get<ApiResponse<SessionStatusResponse>>(`${this.authUrl}/me`)
      .pipe(
        map((response) => response.data!),
        tap((state) => {
          if (state.user) {
            this.tokens.storeUser(state.user as AuthenticatedUserResponse);
          }
          if (state.tenant) {
            this.tokens.storeTenant(state.tenant);
          }
        }),
      );
  }

  /** The password rules to show beside a password box, straight from the server's policy. */
  getPasswordPolicy(): Observable<PasswordPolicyResponse> {
    return this.http
      .get<ApiResponse<PasswordPolicyResponse>>(`${this.usersUrl}/password-policy`)
      .pipe(map((response) => response.data!));
  }

  /**
   * Which Organisation this host belongs to, for the branding on the sign-in screen.
   *
   * Anonymous, and deliberately thin: it returns a name and a logo, never a hint about who has
   * an account here.
   */
  resolveTenant(hostName?: string): Observable<TenantResolutionResponse> {
    const params = hostName ? new HttpParams().set('hostName', hostName) : undefined;

    return this.http
      .get<ApiResponse<TenantResolutionResponse>>(`${this.authUrl}/resolve-tenant`, { params })
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Organisation selection — the SuperAdmin switcher
  // =========================================================================================

  /** The Organisations a global caller may step into. */
  getSelectableOrganisations(): Observable<TenantOptionResponse[]> {
    return this.http
      .get<ApiResponse<TenantOptionResponse[]>>(`${this.authUrl}/selectable-tenants`)
      .pipe(map((response) => response.data ?? []));
  }

  /**
   * Steps into an Organisation.
   *
   * The answer is a NEW ACCESS TOKEN scoped to that Organisation, on the same session. Nothing
   * about the caller's own user record changes — a root user has no Organisation of their own
   * and never acquires one by looking at somebody's data.
   */
  selectOrganisation(tenantId: string): Observable<SelectTenantResponse> {
    return this.http
      .post<ApiResponse<SelectTenantResponse>>(`${this.authUrl}/select-tenant`, { tenantId })
      .pipe(
        map((response) => response.data!),
        tap((result) => this.tokens.storeTenantSelection(result)),
      );
  }

  /**
   * Leaves the current Organisation and returns to platform scope.
   *
   * The counterpart to `selectOrganisation`. Without it, stepping into an Organisation lasted the
   * rest of the session: the token kept naming it, and since that id stamps writes and labels
   * audit rows, "just navigate somewhere else" was not the same as leaving.
   *
   * Same session, new token, and the stored context is replaced rather than merged — the point is
   * that the Organisation is gone from it.
   */
  exitOrganisation(): Observable<SelectTenantResponse> {
    return this.http
      .post<ApiResponse<SelectTenantResponse>>(`${this.authUrl}/exit-tenant`, {})
      .pipe(
        map((response) => response.data!),
        tap((result) => this.tokens.storeTenantSelection(result)),
      );
  }

  /** The navigation tree for the caller, already filtered by the server. */
  getNavigation(): Observable<NavigationResponse> {
    return this.http
      .get<ApiResponse<NavigationResponse>>(`${environment.apiBaseUrl}/navigation`)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // IAM-AUTH-02 — invitation and activation
  // =========================================================================================

  /**
   * What the activation screen shows before a password is typed.
   *
   * Anonymous by necessity — the person has no account yet — and thin by design: somebody
   * holding a link should learn only enough to decide whether to continue.
   */
  previewInvitation(token: string): Observable<InvitationPreviewResponse> {
    return this.http
      .get<ApiResponse<InvitationPreviewResponse>>(
        `${this.usersUrl}/accept-invitation-and-activate-account`,
        { params: new HttpParams().set('token', token) })
      .pipe(map((response) => response.data!));
  }

  /**
   * Starts enrolling a second factor from the activation screen.
   *
   * Authorised by the invitation token rather than a session, because there is no session yet.
   * The shared secret comes back once, here, and the factor stays unusable until
   * `verifyInvitationMfaMethod` proves a code from it works.
   */
  beginInvitationMfaEnrolment(payload: BeginInvitationMfaEnrolmentRequest):
    Observable<MfaEnrolmentResponse> {
    return this.http
      .post<ApiResponse<MfaEnrolmentResponse>>(
        `${this.usersUrl}/accept-invitation-and-activate-account/begin-mfa-enrolment`, payload)
      .pipe(map((response) => response.data!));
  }

  /** Confirms the factor enrolled during activation, before the account is activated. */
  verifyInvitationMfaMethod(payload: VerifyInvitationMfaEnrolmentRequest):
    Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(
        `${this.usersUrl}/accept-invitation-and-activate-account/verify-mfa-method`, payload)
      .pipe(map((response) => response.data!));
  }

  /** Asks for a replacement invitation. The current link stops working immediately. */
  requestNewInvitation(token: string): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(
        `${this.usersUrl}/accept-invitation-and-activate-account/request-new-invitation`, { token })
      .pipe(map((response) => response.data!));
  }

  /**
   * Leaves the activation flow without completing it.
   *
   * The invitation stays usable, so somebody who backs out to check a detail can return to the
   * same link rather than needing a new one.
   */
  cancelActivation(token: string, reason?: string): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(
        `${this.usersUrl}/accept-invitation-and-activate-account/cancel`, { token, reason })
      .pipe(map((response) => response.data!));
  }

  /** Activates the account and signs the person straight in. */
  acceptInvitation(payload: AcceptInvitationRequest): Observable<AcceptInvitationResponse> {
    const request: AcceptInvitationRequest = {
      ...payload,
      deviceIdentifier: payload.deviceIdentifier ?? this.device.getDeviceIdentifier(),
    };

    return this.http
      .post<ApiResponse<AcceptInvitationResponse>>(
        `${this.usersUrl}/accept-invitation-and-activate-account`, request)
      .pipe(
        map((response) => response.data!),
        tap((result) => {
          if (result.accessToken) {
            this.tokens.storeAccessToken(
              result.accessToken,
              result.accessTokenExpiresAtUtc ?? null,
              result.sessionId ?? null);
          }
          if (result.user) {
            this.tokens.storeUser(result.user as AuthenticatedUserResponse);
          }
          if (result.tenant) {
            this.tokens.storeTenant(result.tenant);
          }
        }),
      );
  }

  // =========================================================================================
  // IAM-AUTH-03 / 04 — password recovery
  // =========================================================================================

  /**
   * Starts recovery.
   *
   * The answer is the same whether or not the address is known here. That is not vagueness for
   * its own sake: a different answer for a known address turns this endpoint into a way to test
   * whether somebody has an account with this Organisation.
   */
  forgotPassword(payload: ForgotPasswordRequest): Observable<ForgotPasswordResponse> {
    return this.http
      .post<ApiResponse<ForgotPasswordResponse>>(`${this.usersUrl}/forgot-password`, payload)
      .pipe(map((response) => response.data!));
  }

  /**
   * Whether a recovery link is still usable, and the rules the new password must satisfy.
   *
   * Called BEFORE the form is drawn. Without it, somebody carefully chooses a password, presses
   * Save, and only then learns the link expired an hour ago.
   */
  getResetPasswordView(token: string): Observable<ResetPasswordViewResponse> {
    return this.http
      .get<ApiResponse<ResetPasswordViewResponse>>(`${this.usersUrl}/reset-password`, {
        params: new HttpParams().set('token', token),
      })
      .pipe(map((response) => response.data!));
  }

  /** Asks for a fresh recovery link when the current one has lapsed. */
  requestNewRecoveryLink(identifier: string): Observable<ForgotPasswordResponse> {
    return this.http
      .post<ApiResponse<ForgotPasswordResponse>>(
        `${this.usersUrl}/reset-password/request-new-link`, { identifier })
      .pipe(map((response) => response.data!));
  }

  /** Sets a new password from a recovery link. Every existing session is signed out. */
  resetPassword(payload: ResetPasswordRequest): Observable<PasswordOperationResponse> {
    return this.http
      .post<ApiResponse<PasswordOperationResponse>>(`${this.usersUrl}/reset-password`, payload)
      .pipe(map((response) => response.data!));
  }

  /** Changes the password of somebody already signed in. Requires the current one. */
  changePassword(payload: ChangePasswordRequest): Observable<PasswordOperationResponse> {
    return this.http
      .post<ApiResponse<PasswordOperationResponse>>(`${this.usersUrl}/change-password`, payload)
      .pipe(map((response) => response.data!));
  }

  /** Confirms an e-mail address from the link sent to it. */
  confirmEmail(token: string): Observable<PasswordOperationResponse> {
    return this.http
      .post<ApiResponse<PasswordOperationResponse>>(`${this.usersUrl}/confirm-email`, { token })
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // IAM-AUTH-05 — MFA challenge
  // =========================================================================================

  /** Verifies the second factor and finishes signing in. */
  verifyMfa(payload: VerifyMfaRequest): Observable<SignInResponse> {
    const request: VerifyMfaRequest = {
      ...payload,
      deviceIdentifier: payload.deviceIdentifier ?? this.device.getDeviceIdentifier(),
      deviceName: payload.deviceName ?? this.device.getDeviceName(),
    };

    return this.http
      .post<ApiResponse<SignInResponse>>(`${this.usersUrl}/mfa-challenge/verify`, request)
      .pipe(
        map((response) => response.data!),
        tap((result) => this.tokens.storeSignIn(result)),
      );
  }

  /**
   * Sends the code again.
   *
   * Only meaningful for e-mail and text methods — an authenticator application generates codes
   * on the device and there is nothing to resend. `methodSendsCode()` in the auth model is what
   * the screen uses to decide whether to offer the button at all.
   */
  resendMfaChallenge(challengeToken: string): Observable<MfaChallengeResponse> {
    return this.http
      .post<ApiResponse<MfaChallengeResponse>>(
        `${this.usersUrl}/mfa-challenge/resend`, { challengeToken })
      .pipe(map((response) => response.data!));
  }

  /**
   * Switches to a different enrolled factor without starting sign-in again.
   *
   * This is the same endpoint as a resend, with the method named. That is not a shortcut: from
   * the server's point of view both mean "retire the outstanding challenge and issue a fresh one
   * against this method", and the person's password step stays proved either way.
   */
  switchMfaMethod(challengeToken: string, mfaMethodId: string): Observable<MfaChallengeResponse> {
    return this.http
      .post<ApiResponse<MfaChallengeResponse>>(
        `${this.usersUrl}/mfa-challenge/resend`, { challengeToken, mfaMethodId })
      .pipe(map((response) => response.data!));
  }

  /**
   * Abandons a half-finished sign-in.
   *
   * The challenge is retired straight away rather than left to expire, so a code already sitting
   * in an inbox or on a phone stops working the moment the person backs out.
   */
  cancelMfaChallenge(challengeToken: string, reason?: string): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(
        `${this.usersUrl}/mfa-challenge/cancel`, { challengeToken, reason })
      .pipe(map((response) => response.data!));
  }

  /** Signs in with a one-time recovery code instead of the second factor. */
  redeemRecoveryCode(payload: RedeemRecoveryCodeRequest): Observable<SignInResponse> {
    return this.http
      .post<ApiResponse<SignInResponse>>(`${this.usersUrl}/mfa-challenge/recovery-code`, payload)
      .pipe(
        map((response) => response.data!),
        tap((result) => this.tokens.storeSignIn(result)),
      );
  }

  // =========================================================================================
  // IAM-AUTH-06 — account unavailable
  // =========================================================================================

  /**
   * Safe guidance for somebody who cannot get in.
   *
   * Deliberately non-disclosing: it explains what to do next without confirming whether the
   * account exists, is locked, or was never here at all.
   */
  getRecoveryGuidance(): Observable<AccountRecoveryGuidanceResponse> {
    return this.http
      .get<ApiResponse<AccountRecoveryGuidanceResponse>>(
        `${this.usersUrl}/account-unavailable-and-recovery-guidance`)
      .pipe(map((response) => response.data!));
  }

  /**
   * Starts recovery from the account-unavailable screen.
   *
   * A suspended account is sent a link that lifts the hold and sets a new password in one step;
   * anybody else gets the ordinary recovery link. Which of the two was sent is not reported
   * back, for the same reason forgot-password says nothing.
   */
  startRecovery(identifier: string): Observable<ForgotPasswordResponse> {
    return this.http
      .post<ApiResponse<ForgotPasswordResponse>>(
        `${this.usersUrl}/account-unavailable-and-recovery-guidance/start-recovery`, { identifier })
      .pipe(map((response) => response.data!));
  }

  /** Sends a message to the service desk from somebody who cannot get in. */
  contactSupport(payload: ContactSupportRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(
        `${this.usersUrl}/account-unavailable-and-recovery-guidance/contact-support`, payload)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // IAM-AUTH-07 — reauthentication
  // =========================================================================================

  /** What the step-up screen shows: why it is asking, and how long is left. */
  getReauthenticationView(protectedActionSummary?: string, draftToken?: string):
    Observable<ReauthenticationViewResponse> {
    let params = new HttpParams();

    if (protectedActionSummary) {
      params = params.set('protectedActionSummary', protectedActionSummary);
    }

    if (draftToken) {
      params = params.set('draftToken', draftToken);
    }

    return this.http
      .get<ApiResponse<ReauthenticationViewResponse>>(
        `${this.usersUrl}/session-timeout-and-reauthentication`, { params })
      .pipe(map((response) => response.data!));
  }

  /**
   * Parks a half-filled form before sending somebody to confirm their identity.
   *
   * Without this, a step-up costs whatever the person had typed, and people learn to avoid the
   * protected screens entirely. The payload is form state, never credentials.
   */
  saveProtectedDraft(payload: SaveProtectedDraftRequest): Observable<SaveProtectedDraftResponse> {
    return this.http
      .post<ApiResponse<SaveProtectedDraftResponse>>(
        `${this.usersUrl}/session-timeout-and-reauthentication/save-draft`, payload)
      .pipe(map((response) => response.data!));
  }

  /** Proves identity again after an idle timeout, or before a sensitive action. */
  reauthenticate(payload: ReauthenticateRequest): Observable<ReauthenticateResponse> {
    return this.http
      .post<ApiResponse<ReauthenticateResponse>>(
        `${this.usersUrl}/session-timeout-and-reauthentication`, payload)
      .pipe(map((response) => response.data!));
  }
}
