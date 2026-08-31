/**
 * The authentication vocabulary the screens use.
 *
 * Every type here comes from `iam-contract.model.ts`, which is generated from the API's own
 * OpenAPI document. This file adds nothing to the wire format — it re-exports the pieces the
 * auth screens care about under the names they read best by, and adds the few client-only
 * helpers (route mapping, display labels) that have no server counterpart.
 *
 * WHY RE-EXPORT RATHER THAN REDECLARE
 * -----------------------------------
 * A redeclared interface is a copy, and a copy drifts. When the server renames a field the
 * generated file changes and every screen that used the old name stops compiling — which is the
 * moment you want to hear about it. A hand-written duplicate would keep compiling and quietly
 * read `undefined` for ever.
 */
export type {
  AcceptInvitationRequest,
  AcceptInvitationResponse,
  AccountRecoveryGuidanceResponse,
  AuthenticatedUserResponse,
  BeginInvitationMfaEnrolmentRequest,
  CancelActivationRequest,
  CancelMfaChallengeRequest,
  ChangePasswordRequest,
  ContactSupportRequest,
  ForgotPasswordRequest,
  ForgotPasswordResponse,
  InvitationPreviewResponse,
  MfaChallengeResponse,
  MfaEnrolmentResponse,
  MfaMethodOptionResponse,
  NavigationResponse,
  MenuNode,
  PasswordOperationResponse,
  PasswordPolicyResponse,
  ReauthenticateRequest,
  ReauthenticateResponse,
  ReauthenticationViewResponse,
  RedeemRecoveryCodeRequest,
  RefreshTokenRequest,
  ResendMfaChallengeRequest,
  RequestNewInvitationRequest,
  ResetPasswordRequest,
  ResetPasswordViewResponse,
  SaveProtectedDraftRequest,
  SaveProtectedDraftResponse,
  SelectTenantRequest,
  SelectTenantResponse,
  SessionStatusResponse,
  SignInRequest,
  SignInResponse,
  StartRecoveryRequest,
  TenantContextResponse,
  TenantOptionResponse,
  TenantResolutionResponse,
  VerifyInvitationMfaEnrolmentRequest,
  VerifyMfaRequest,
} from './iam-contract.model';

export type {
  AccessScopeType,
  ClientType,
  MfaMethodType,
  PrivilegeLevel,
  SignInResultStatus,
  TenantStatus,
  UserStatus,
} from './iam-contract.model';

import type { MfaMethodType, SignInResponse, SignInResultStatus } from './iam-contract.model';

// ===========================================================================================
// Client-side helpers
// ===========================================================================================

/**
 * Where a sign-in answer sends the person next.
 *
 * The four outcomes are genuinely different screens, and the mapping lives here — in one
 * place — rather than being re-derived in the sign-in component, the MFA component and the
 * activation component, which is how three copies end up disagreeing.
 */
export const SIGN_IN_ROUTES: Record<SignInResultStatus, string> = {
  succeeded: '/app/dashboard',
  mfaRequired: '/auth/mfa-challenge',

  // STRAIGHT INTO THE ORGANISATION DIRECTORY, not to a chooser.
  //
  // A platform administrator is signed in and entitled to be here; they simply belong to no
  // single Organisation. Stopping them on a "where would you like to work?" page made that
  // sound like an unfinished sign-in, and it is not - the directory IS their home screen, it
  // lists every Organisation, and entering one is a click from there or from the switcher in
  // the top bar whenever they actually need to be inside one.
  //
  // The picker at /auth/select-organisation still exists and is still reached from the
  // organisationContextGuard: somebody who opens an Organisation-scoped screen without having
  // named an Organisation is asked which one, at the point where the answer is needed.
  tenantSelectionRequired: '/app/administration/organisation/directory',

  passwordChangeRequired: '/auth/reset-password',
};

/**
 * The Organisation states in which the only useful screen is the onboarding one.
 *
 * An administrator signing in here has one job: finish the profile, attach the registration
 * documents and submit for approval - or read why it came back and correct it. The dashboard has
 * nothing to show them, because the Organisation has no campaigns, no donors and no money yet.
 */
const ONBOARDING_STATUSES: readonly string[] = [
  'invited', 'invitationAccepted', 'profileIncomplete',
  'submitted', 'underReview', 'rejected', 'resubmitted',
];

/** Where the profile is completed, the documents attached, and the submission made. */
export const ORGANISATION_ONBOARDING_ROUTE = '/app/administration/organisation/details';

export function nextRouteFor(response: SignInResponse): string {
  const destination = SIGN_IN_ROUTES[response.status ?? 'succeeded'] ?? '/app/dashboard';

  // An Organisation still working through onboarding sends its administrator to the profile,
  // whatever the sign-in status was. Landing on an empty dashboard and having to find the right
  // screen is how somebody concludes the platform is broken when it is merely waiting for them.
  const status = response.tenant?.status;

  if (status && ONBOARDING_STATUSES.includes(status)) {
    return ORGANISATION_ONBOARDING_ROUTE;
  }

  return destination;
}

/** How each second-factor method is described to a person. */
export const MFA_METHOD_LABELS: Record<MfaMethodType, string> = {
  authenticatorApp: 'Authenticator application',
  email: 'E-mail one-time code',
  sms: 'Text message',
  securityKey: 'Security key',
};

/**
 * True when the method sends a code somewhere, and therefore has something to resend.
 *
 * An authenticator application generates codes on the device; there is nothing to resend, and
 * offering the button anyway invites people to click it and wonder why nothing arrives.
 */
export function methodSendsCode(method: MfaMethodType | null | undefined): boolean {
  return method === 'email' || method === 'sms';
}
