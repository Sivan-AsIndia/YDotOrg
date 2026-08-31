import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router } from '@angular/router';
import { ToastService } from '../../../../Shared/services/toast.service';
import { SecurityApiService } from '../../../../Service/security-api.service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import {
  ClientType,
  MfaMethodType,
  SignInAttemptResponse,
  UserSecurityResponse,
} from '../../../../Shared/models/iam-contract.model';

/** One session, as the page lists it. */
interface SessionView {
  id: string;
  device: string;
  browser: string;
  ipAddress: string;
  lastActive: string;
  isCurrent: boolean;
}

/** One enrolled factor, as the page lists it. */
interface MfaMethodView {
  id: string;
  method: string;
  icon: string;
  status: string;
  enrolledOn: string;
}

/** One remembered device. */
interface TrustedDeviceView {
  id: string;
  device: string;
  type: string;
  trustedOn: string;
  isExpired: boolean;
}

/** One line of the activity feed. */
interface ActivityView {
  event: string;
  details: string;
  dateTime: string;
  icon: string;
}

/**
 * The page somebody looks after their OWN account from.
 *
 * EVERY CALL HERE IS TO `/my-security`, which takes no user id at all — the server acts on
 * whoever holds the token. That is what makes this page safe to show to everybody without a
 * permission check: there is no id to tamper with, so it cannot be pointed at anybody else.
 *
 * The view models below exist because the API answers in the domain's vocabulary
 * (`lastActivityAtUtc`, `methodType`, `outcomeDisplay`) and the page speaks a person's
 * ("Last active", "Authenticator app", "Signed in"). Translating once, here, keeps the
 * translation in one place instead of scattered through the template.
 */
@Component({
  selector: 'app-my-security',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './my-security.html',
  styleUrl: './my-security.css',
})
export class MySecurityComponent {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly api = inject(SecurityApiService);

  /**
   * The signed-in identity, for the address and username on the header.
   *
   * The security payload deliberately carries no e-mail — it is about factors, sessions and
   * devices, and repeating contact details in it would be a second place for them to go stale.
   * They are already held from the sign-in response.
   */
  private readonly auth = inject(AuthTokenService);

  readonly security = signal<UserSecurityResponse | null>(null);
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly busy = signal(false);

  showChangePassword = signal(false);
  currentPassword = signal('');
  newPassword = signal('');
  confirmPassword = signal('');

  /** Sign out of everything else when the password changes. On by default, and it should be. */
  signOutOthers = signal(true);

  /**
   * The freshly generated backup codes.
   *
   * Held only until the dialog closes. They are shown once by the server and never again, so
   * keeping them anywhere longer-lived would be storing the thing that was deliberately not
   * stored.
   */
  readonly recoveryCodes = signal<string[]>([]);
  readonly showRecoveryCodes = signal(false);

  constructor() {
    this.loadData();
  }

  // =========================================================================================
  // What the page renders
  // =========================================================================================

  readonly data = computed(() => {
    const model = this.security();

    if (!model) {
      return null;
    }

    return {
      displayName: model.displayName ?? this.auth.displayName(),
      loginEmail: this.auth.email(),
      username: this.auth.user()?.username ?? '',
      passwordLastChanged: model.passwordChangedAtUtc
        ? this.formatDateTime(model.passwordChangedAtUtc)
        : 'Never changed',
      mfaRequirement: this.mfaRequirementLabel(model),
      recoveryCodesRemaining: model.recoveryCodesRemaining ?? 0,
      isLockedOut: model.isLockedOut === true,
      mustChangePassword: model.mustChangePassword === true,
      mfaMethods: this.mfaMethods(),
      activeSessions: this.activeSessions(),
      trustedDevices: this.trustedDevices(),
      recentActivity: this.recentActivity(),
    };
  });

  readonly activeSessions = computed<SessionView[]>(() =>
    (this.security()?.activeSessions ?? []).map((session) => ({
      id: session.id ?? '',
      device: session.deviceName ?? this.clientTypeLabel(session.clientType),
      browser: [session.browser, session.operatingSystem].filter(Boolean).join(' on ') || '—',
      ipAddress: session.ipAddress ?? '—',
      lastActive: session.lastActivityAtUtc
        ? this.formatDateTime(session.lastActivityAtUtc)
        : '—',
      isCurrent: session.isCurrent === true,
    })));

  readonly mfaMethods = computed<MfaMethodView[]>(() =>
    (this.security()?.mfaMethods ?? [])
      // Revoked factors are history, not a list of what protects the account today.
      .filter((method) => method.status !== 'revoked')
      .map((method) => ({
        id: method.id ?? '',
        method: this.mfaMethodLabel(method.methodType)
          + (method.maskedDestination ? ` · ${method.maskedDestination}` : ''),
        icon: this.mfaMethodIcon(method.methodType),
        status: method.status === 'pending' ? 'Awaiting confirmation' : 'Active',
        enrolledOn: method.verifiedAtUtc ? this.formatDateTime(method.verifiedAtUtc) : '—',
      })));

  readonly trustedDevices = computed<TrustedDeviceView[]>(() =>
    (this.security()?.trustedDevices ?? []).map((device) => ({
      id: device.id ?? '',
      device: device.deviceName ?? this.clientTypeLabel(device.clientType),
      type: [device.browser, device.operatingSystem].filter(Boolean).join(' on ') || '—',
      trustedOn: device.trustedAtUtc ? this.formatDateTime(device.trustedAtUtc) : '—',
      isExpired: device.isExpired === true,
    })));

  /**
   * The activity feed, built from real sign-in attempts.
   *
   * A FAILED ATTEMPT IS THE POINT OF THIS LIST. Somebody checking their own security wants to
   * see the sign-in from a place they have never been, and a feed that showed only successes
   * would hide exactly the entry worth seeing.
   */
  readonly recentActivity = computed<ActivityView[]>(() =>
    (this.security()?.recentAttempts ?? []).map((attempt) => ({
      event: this.attemptTitle(attempt),
      details: [
        attempt.browser && attempt.operatingSystem
          ? `${attempt.browser} on ${attempt.operatingSystem}`
          : attempt.browser ?? '',
        attempt.ipAddress ? `from ${attempt.ipAddress}` : '',
        attempt.location ?? '',
      ].filter(Boolean).join(' · ') || '—',
      dateTime: attempt.attemptedAtUtc ? this.formatDateTime(attempt.attemptedAtUtc) : '—',
      icon: attempt.succeeded ? 'ri-login-circle-line'
        : attempt.triggeredLockout ? 'ri-lock-line'
          : 'ri-error-warning-line',
    })));

  // =========================================================================================
  // Loading
  // =========================================================================================

  private loadData(): void {
    this.loading.set(true);
    this.loadError.set(false);

    this.api.getMySecurity().subscribe({
      next: (model) => {
        this.security.set(model);
        this.loading.set(false);
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.loadError.set(true);
        this.toast.show('Could not load your security details', error.message, 'error');
      },
    });
  }

  retry(): void {
    this.loadData();
  }

  // =========================================================================================
  // Password
  // =========================================================================================

  changePassword(): void {
    if (!this.currentPassword() || !this.newPassword() || !this.confirmPassword()) {
      this.toast.show('Check the form', 'Fill in all three password fields.', 'warning');
      return;
    }

    if (this.newPassword() !== this.confirmPassword()) {
      this.toast.show('Check the form', 'The two new passwords do not match.', 'warning');
      return;
    }

    this.busy.set(true);

    this.api.changeMyPassword({
      currentPassword: this.currentPassword(),
      newPassword: this.newPassword(),
      confirmPassword: this.confirmPassword(),
      signOutOtherSessions: this.signOutOthers(),
    }).subscribe({
      next: (outcome) => {
        this.busy.set(false);
        this.showChangePassword.set(false);
        this.currentPassword.set('');
        this.newPassword.set('');
        this.confirmPassword.set('');
        this.toast.show('Password changed', outcome.message ?? 'Your password has been updated.',
          'success');

        // Re-read rather than patch: signing out other sessions changes the session list, and
        // the page would otherwise still show the ones that have just ended.
        this.loadData();
      },
      error: (error: Error) => {
        this.busy.set(false);
        this.toast.show('Could not change your password', error.message, 'error');
      },
    });
  }

  // =========================================================================================
  // Two-step verification
  // =========================================================================================

  enrolMfa(): void {
    this.router.navigate(['/app/administration/access/my-security/mfa-enrol']);
  }

  removeMfaMethod(method: MfaMethodView): void {
    if (!method.id) {
      return;
    }

    this.busy.set(true);

    this.api.revokeMyMfaMethod(method.id, 'Removed from the security page.').subscribe({
      next: (outcome) => {
        this.busy.set(false);
        this.toast.show('Method removed', outcome.message ?? 'That method has been removed.',
          'success');
        this.loadData();
      },
      error: (error: Error) => {
        this.busy.set(false);

        // The server refuses when this is the last factor and the Organisation requires MFA.
        // That refusal is the useful message, so it is shown as-is.
        this.toast.show('Could not remove that method', error.message, 'error');
      },
    });
  }

  /**
   * Issues a fresh batch of backup codes.
   *
   * Every earlier code stops working, which is worth saying plainly before it happens rather
   * than discovering it with a printed sheet that no longer works.
   */
  generateRecoveryCodes(): void {
    this.busy.set(true);

    this.api.generateRecoveryCodes().subscribe({
      next: (result) => {
        this.busy.set(false);
        this.recoveryCodes.set(result.codes ?? []);
        this.showRecoveryCodes.set(true);
        this.loadData();
      },
      error: (error: Error) => {
        this.busy.set(false);
        this.toast.show('Could not generate codes', error.message, 'error');
      },
    });
  }

  closeRecoveryCodes(): void {
    this.showRecoveryCodes.set(false);

    // Dropped as the dialog closes. The server showed them once and stores only hashes, so
    // holding them here any longer would be keeping what was deliberately not kept.
    this.recoveryCodes.set([]);
  }

  copyRecoveryCodes(): void {
    void navigator.clipboard.writeText(this.recoveryCodes().join('\n')).then(() => {
      this.toast.show('Copied', 'The codes are on your clipboard. Save them somewhere safe.',
        'success');
    });
  }

  // =========================================================================================
  // Sessions and devices
  // =========================================================================================

  revokeSession(session: SessionView): void {
    if (!session.id) {
      return;
    }

    // Ending your own current session would sign you out of the page you are standing on. The
    // server would allow it; refusing here is the kinder answer.
    if (session.isCurrent) {
      this.toast.show('That is this session',
        'Use Sign out to end the session you are using right now.', 'info');
      return;
    }

    this.busy.set(true);

    this.api.revokeMySession(session.id, 'Ended from the security page.').subscribe({
      next: (outcome) => {
        this.busy.set(false);
        this.toast.show('Session ended',
          outcome.message ?? `The session on ${session.device} has ended.`, 'success');
        this.loadData();
      },
      error: (error: Error) => {
        this.busy.set(false);
        this.toast.show('Could not end that session', error.message, 'error');
      },
    });
  }

  revokeTrustedDevice(device: TrustedDeviceView): void {
    if (!device.id) {
      return;
    }

    this.busy.set(true);

    this.api.revokeMyTrustedDevice(device.id, 'Forgotten from the security page.').subscribe({
      next: (outcome) => {
        this.busy.set(false);
        this.toast.show('Device forgotten',
          outcome.message ?? `${device.device} will be asked to verify next time.`, 'success');
        this.loadData();
      },
      error: (error: Error) => {
        this.busy.set(false);
        this.toast.show('Could not forget that device', error.message, 'error');
      },
    });
  }

  // =========================================================================================
  // Wording
  // =========================================================================================

  private mfaRequirementLabel(model: UserSecurityResponse): string {
    if (model.isMfaEffectivelyRequired) {
      return model.mfaEnabled ? 'Required · enrolled' : 'Required · not yet set up';
    }

    return model.mfaEnabled ? 'Optional · enrolled' : 'Optional · not set up';
  }

  private mfaMethodLabel(methodType: MfaMethodType | undefined): string {
    switch (methodType) {
      case 'authenticatorApp': return 'Authenticator app';
      case 'sms': return 'Text message';
      case 'email': return 'E-mail';
      case 'securityKey': return 'Security key';
      default: return 'Verification method';
    }
  }

  private mfaMethodIcon(methodType: MfaMethodType | undefined): string {
    switch (methodType) {
      case 'authenticatorApp': return 'ri-smartphone-line';
      case 'sms': return 'ri-message-2-line';
      case 'email': return 'ri-mail-line';
      case 'securityKey': return 'ri-key-2-line';
      default: return 'ri-shield-keyhole-line';
    }
  }

  private clientTypeLabel(clientType: ClientType | undefined): string {
    switch (clientType) {
      case 'web': return 'Browser';
      case 'mobile': return 'Mobile app';
      case 'desktop': return 'Desktop app';
      case 'api': return 'API client';
      default: return 'Unknown device';
    }
  }

  private attemptTitle(attempt: SignInAttemptResponse): string {
    if (attempt.succeeded) {
      return 'Signed in';
    }

    if (attempt.triggeredLockout) {
      return 'Account locked after too many attempts';
    }

    return attempt.outcomeDisplay ?? 'Sign-in failed';
  }

  private formatDateTime(value: string): string {
    try {
      return new Date(value).toLocaleString('en-IN',
        { dateStyle: 'medium', timeStyle: 'short' });
    } catch {
      return value;
    }
  }

  goBack(): void {
    this.router.navigate(['/app/administration/access/user-directory']);
  }
}
