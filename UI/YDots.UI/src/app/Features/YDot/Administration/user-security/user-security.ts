import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ToastService } from '../../../../Shared/services/toast.service';
import { SecurityApiService } from '../../../../Service/security-api.service';
import { UserDirectoryApiService } from '../../../../Service/user-directory-api.service';
import {
  ClientType,
  MfaMethodType,
  UserDetailResponse,
  UserSecurityResponse,
} from '../../../../Shared/models/iam-contract.model';

/** What the confirm dialog is about to do. */
type SecurityAction =
  | 'requirePasswordReset'
  | 'revokeAllSessions'
  | 'revokeSession'
  | 'resetMfa'
  | 'unlockAccount'
  | 'removeTrustedDevice'
  | 'exportSecurityEvidence';

interface SessionRow {
  id: string;
  device: string;
  deviceIcon: string;
  browser: string;
  ipAddress: string;
  lastActive: string;
  isCurrent: boolean;
}

interface TrustedDeviceRow {
  id: string;
  device: string;
  deviceIcon: string;
  type: string;
  trustedOn: string;
  expiry: string;
}

/**
 * Somebody else's security, and what an administrator can do about it.
 *
 * EVERY ACTION HERE IS PERMISSION-GATED ON THE SERVER. The buttons are hidden when the caller
 * lacks the permission, but that is a courtesy — the endpoints refuse regardless, so a hidden
 * button is never the thing standing between a person and somebody else's sessions.
 *
 * A REASON IS REQUIRED FOR ALL OF THEM. Each of these actions interrupts somebody's working
 * day, and "who reset my MFA, and why" is a question that gets asked. The reason goes into the
 * audit row, so it can be answered.
 *
 * ACTIONS ARE AIMED AT A ROW, NOT AT A SCREEN. Revoking a session takes the session, and
 * forgetting a device takes the device: the earlier version had one button per action with no
 * subject at all, which could only ever have meant "all of them".
 */
@Component({
  selector: 'app-user-security',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './user-security.html',
  styleUrl: './user-security.css',
})
export class UserSecurityComponent {
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly api = inject(SecurityApiService);
  private readonly directory = inject(UserDirectoryApiService);

  readonly userReference = signal('');
  readonly userId = signal('');

  /** The record, for the name, reference and contact details on the header. */
  readonly profile = signal<UserDetailResponse | null>(null);
  readonly security = signal<UserSecurityResponse | null>(null);

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly validationErrors = signal<string[]>([]);

  readonly showSecurityModal = signal(false);
  readonly currentAction = signal<SecurityAction | ''>('');
  readonly modalTitle = signal('');
  readonly modalMessage = signal('');
  readonly actionProcessing = signal(false);

  /** The row the open dialog is about, when the action needs one. */
  private readonly targetSession = signal<SessionRow | null>(null);
  private readonly targetDevice = signal<TrustedDeviceRow | null>(null);

  readonly securityActionForm = this.fb.group({
    reason: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(1000)]],
  });

  /**
   * What this caller may do here.
   *
   * Read from the record the server sends rather than from the caller's own permission list:
   * the server has already worked out what applies to THIS person — a system account cannot be
   * suspended however many permissions the caller holds — and re-deriving it here would be a
   * second opinion that can disagree with the one that matters.
   */
  readonly permittedActions = computed(() => this.profile()?.permittedActions ?? []);

  readonly canResetPassword = computed(() => this.permittedActions().includes('ResetPassword'));
  readonly canForceSignOut = computed(() => this.permittedActions().includes('ForceSignOut'));
  readonly canUnlock = computed(() =>
    this.permittedActions().includes('Unlock') && this.security()?.isLockedOut === true);

  constructor() {
    this.userReference.set(this.route.snapshot.params['userReference'] ?? '');
    this.loadData();

    effect(() => {
      this.securityActionForm.updateValueAndValidity({ emitEvent: false });
      const errors: string[] = [];
      const reasonErrors = this.securityActionForm.controls.reason.errors;

      if (reasonErrors?.['required']) errors.push('A reason is required.');
      if (reasonErrors?.['minlength']) errors.push('The reason must be at least 10 characters.');
      if (reasonErrors?.['maxlength']) errors.push('The reason cannot exceed 1000 characters.');

      this.validationErrors.set(errors);
    });
  }

  // =========================================================================================
  // What the page renders
  // =========================================================================================

  readonly data = computed(() => {
    const model = this.security();
    const person = this.profile();

    if (!model) {
      return null;
    }

    return {
      user: {
        reference: person?.code ?? this.userReference(),
        displayName: model.displayName ?? person?.displayName ?? '',
        passwordLastChanged: model.passwordChangedAtUtc
          ? this.formatDateTime(model.passwordChangedAtUtc)
          : 'Never changed',
        mfaRequirement: this.mfaRequirementLabel(model),
        recoveryCodeStatus: `${model.recoveryCodesRemaining ?? 0} remaining`,
        riskFlags: this.riskSummary(model),
      },
      verifiedContacts: this.verifiedContacts(),
      mfaMethods: this.mfaMethods(),
      activeSessions: this.activeSessions(),
      trustedDevices: this.trustedDevices(),
      failedSignins: this.failedSignins(),
      recentEvents: this.recentEvents(),
    };
  });

  readonly verifiedContacts = computed(() => {
    const person = this.profile();

    if (!person) {
      return [];
    }

    const contacts = [{
      icon: 'uil-envelope',
      value: person.email ?? '—',
      status: person.emailConfirmed ? 'Verified' : 'Not verified',
    }];

    // Only shown when there is one. A blank row saying "Not verified" against no number at all
    // reads as a failure rather than as an absence.
    if (person.mobileNumber) {
      contacts.push({
        icon: 'uil-mobile-android',
        value: `${person.mobileCountryCode ?? ''} ${person.mobileNumber}`.trim(),
        status: person.mobileConfirmed ? 'Verified' : 'Not verified',
      });
    }

    return contacts;
  });

  readonly mfaMethods = computed(() =>
    (this.security()?.mfaMethods ?? [])
      .filter((method) => method.status !== 'revoked')
      .map((method) => ({
        method: this.mfaMethodLabel(method.methodType)
          + (method.maskedDestination ? ` · ${method.maskedDestination}` : ''),
        icon: this.mfaMethodIcon(method.methodType),
        iconColor: method.status === 'active' ? 'sec-icon--good' : 'sec-icon--warn',
        status: method.status === 'pending' ? 'Awaiting confirmation' : 'Active',
        enrolledOn: method.verifiedAtUtc ? this.formatDate(method.verifiedAtUtc) : '—',
        lastUsed: method.lastUsedAtUtc ? this.formatDateTime(method.lastUsedAtUtc) : 'Never',
      })));

  readonly activeSessions = computed<SessionRow[]>(() =>
    (this.security()?.activeSessions ?? []).map((session) => ({
      id: session.id ?? '',
      device: session.deviceName ?? this.clientTypeLabel(session.clientType),
      deviceIcon: this.clientTypeIcon(session.clientType),
      browser: [session.browser, session.operatingSystem].filter(Boolean).join(' on ') || '—',
      ipAddress: session.ipAddress ?? '—',
      lastActive: session.lastActivityAtUtc
        ? this.formatDateTime(session.lastActivityAtUtc) : '—',
      isCurrent: session.isCurrent === true,
    })));

  readonly trustedDevices = computed<TrustedDeviceRow[]>(() =>
    (this.security()?.trustedDevices ?? []).map((device) => ({
      id: device.id ?? '',
      device: device.deviceName ?? this.clientTypeLabel(device.clientType),
      deviceIcon: this.clientTypeIcon(device.clientType),
      type: [device.browser, device.operatingSystem].filter(Boolean).join(' on ') || '—',
      trustedOn: device.trustedAtUtc ? this.formatDate(device.trustedAtUtc) : '—',
      expiry: device.isExpired
        ? 'Expired'
        : device.expiresAtUtc ? this.formatDate(device.expiresAtUtc) : '—',
    })));

  /**
   * Failed sign-ins over three windows.
   *
   * COUNTED FROM THE ATTEMPTS THE SERVER RETURNS, which is the recent history rather than all
   * time — so the "total" is the total in that window, and the tile says so. Calling a
   * window-limited count "total" without qualification is how a quiet number gets read as an
   * all-clear.
   */
  readonly failedSignins = computed(() => {
    const attempts = (this.security()?.recentAttempts ?? []).filter((a) => !a.succeeded);
    const now = Date.now();
    const within = (hours: number) => attempts.filter((attempt) => {
      if (!attempt.attemptedAtUtc) {
        return false;
      }
      return now - new Date(attempt.attemptedAtUtc).getTime() <= hours * 3600_000;
    }).length;

    return {
      last24Hours: within(24),
      last7Days: within(24 * 7),
      total: attempts.length,
    };
  });

  readonly recentEvents = computed(() =>
    (this.security()?.recentAttempts ?? []).map((attempt) => ({
      event: attempt.succeeded ? 'Signed in'
        : attempt.triggeredLockout ? 'Locked out after too many attempts'
          : attempt.outcomeDisplay ?? 'Sign-in failed',
      icon: attempt.succeeded ? 'uil-signin'
        : attempt.triggeredLockout ? 'uil-lock' : 'uil-exclamation-triangle',
      iconColor: attempt.succeeded ? 'sec-icon--good' : 'sec-icon--warn',
      details: [
        attempt.browser && attempt.operatingSystem
          ? `${attempt.browser} on ${attempt.operatingSystem}` : attempt.browser ?? '',
        attempt.ipAddress ? `from ${attempt.ipAddress}` : '',
        attempt.location ?? '',
      ].filter(Boolean).join(' · ') || '—',
      dateTime: attempt.attemptedAtUtc ? this.formatDateTime(attempt.attemptedAtUtc) : '—',
      status: attempt.succeeded ? 'Succeeded' : 'Failed',
    })));

  // =========================================================================================
  // Loading
  // =========================================================================================

  private loadData(): void {
    this.loading.set(true);
    this.loadError.set(false);

    const reference = this.userReference();

    if (!reference) {
      this.loading.set(false);
      this.loadError.set(true);
      this.toast.show('No user', 'This page needs a user reference in the address.', 'error');
      return;
    }

    // The reference resolves to a record first; the security payload is keyed by the internal
    // id, and both are needed — the profile for name and contact details, the security payload
    // for factors, sessions and devices.
    this.directory.getUserByReference(reference).subscribe({
      next: (person) => {
        this.profile.set(person);
        this.userId.set(person.id ?? '');
        this.loadSecurity();
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.loadError.set(true);
        this.toast.show('Could not find that person', error.message, 'error');
      },
    });
  }

  private loadSecurity(): void {
    this.api.getUserSecurity(this.userId()).subscribe({
      next: (model) => {
        this.security.set(model);
        this.loading.set(false);
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.loadError.set(true);
        this.toast.show('Could not load their security details', error.message, 'error');
      },
    });
  }

  /** Re-reads after a write, so the page shows what is stored rather than what was assumed. */
  private refresh(): void {
    forkJoin({
      person: this.directory.getUser(this.userId()),
      model: this.api.getUserSecurity(this.userId()),
    }).subscribe({
      next: ({ person, model }) => {
        this.profile.set(person);
        this.security.set(model);
      },
      error: (error: Error) => {
        this.toast.show('Could not refresh the page', error.message, 'warning');
      },
    });
  }

  retry(): void {
    this.loadData();
  }

  // =========================================================================================
  // Confirming
  // =========================================================================================

  confirmAction(action: SecurityAction, subject?: SessionRow | TrustedDeviceRow): void {
    this.currentAction.set(action);
    this.securityActionForm.reset();
    this.targetSession.set(null);
    this.targetDevice.set(null);

    if (action === 'revokeSession') {
      const session = subject as SessionRow | undefined;

      if (!session?.id) {
        this.toast.show('Pick a session', 'Choose the session to end from the list.', 'warning');
        return;
      }

      this.targetSession.set(session);
    }

    if (action === 'removeTrustedDevice') {
      const device = subject as TrustedDeviceRow | undefined;

      if (!device?.id) {
        this.toast.show('Pick a device', 'Choose the device to forget from the list.', 'warning');
        return;
      }

      this.targetDevice.set(device);
    }

    const name = this.data()?.user.displayName ?? 'this person';

    const copy: Record<SecurityAction, { title: string; message: string }> = {
      requirePasswordReset: {
        title: 'Send a password reset',
        message: `${name} will be e-mailed a reset link and asked to choose a new password at their next sign-in. Their current password stops working.`,
      },
      revokeAllSessions: {
        title: 'Sign out everywhere',
        message: `${name} will be signed out on every device immediately. Anything they have not saved will be lost.`,
      },
      revokeSession: {
        title: 'End this session',
        message: `The session on ${this.targetSession()?.device ?? 'that device'} will end immediately. Their other sessions are untouched.`,
      },
      resetMfa: {
        title: 'Reset two-step verification',
        message: `Every verification method, backup code and remembered device for ${name} will be removed, and they will be signed out. They will set two-step verification up again at their next sign-in.`,
      },
      unlockAccount: {
        title: 'Unlock this account',
        message: `${name} will be able to sign in again straight away, and the failed-attempt count is cleared.`,
      },
      removeTrustedDevice: {
        title: 'Forget this device',
        message: `${this.targetDevice()?.device ?? 'That device'} will be asked to verify again next time it is used.`,
      },
      exportSecurityEvidence: {
        title: 'Export security evidence',
        message: `A file describing ${name}'s factors, sessions, devices and recent sign-in attempts. It contains no passwords, codes or tokens. The export is recorded against your name.`,
      },
    };

    const config = copy[action];
    this.modalTitle.set(config.title);
    this.modalMessage.set(config.message);
    this.showSecurityModal.set(true);
  }

  /**
   * Sends somebody to the devices table.
   *
   * Forgetting a device needs a device, and the list is where they are. The alternative — a
   * page-level button that forgets ALL of them — is a different action wearing this one's
   * label, which is how somebody ends up revoking twelve devices meaning to revoke one.
   */
  scrollToDevices(): void {
    document.getElementById('trusted-devices')?.scrollIntoView({ behavior: 'smooth' });
  }

  closeModal(): void {
    this.showSecurityModal.set(false);
    this.actionProcessing.set(false);
    this.securityActionForm.reset();
    this.targetSession.set(null);
    this.targetDevice.set(null);
  }

  // =========================================================================================
  // Doing
  // =========================================================================================

  executeAction(): void {
    this.securityActionForm.markAllAsTouched();

    if (this.securityActionForm.invalid) {
      this.toast.show('Check the form', 'Give a reason of at least 10 characters.', 'warning');
      return;
    }

    const reason = this.securityActionForm.value.reason ?? '';
    const action = this.currentAction();
    const id = this.userId();
    const version = this.profile()?.version ?? 0;

    if (!action || !id) {
      return;
    }

    this.actionProcessing.set(true);

    const done = (message: string) => {
      this.actionProcessing.set(false);
      this.closeModal();
      this.toast.show('Done', message, 'success');
      this.refresh();
    };

    const failed = (error: Error) => {
      this.actionProcessing.set(false);
      this.toast.show('That did not work', error.message, 'error');
    };

    switch (action) {
      case 'requirePasswordReset':
        this.api.resetUserPassword(id, version, {
          sendResetLink: true,
          requireChangeOnNextSignIn: true,
        }).subscribe({
          next: (outcome) => done(outcome.message ?? 'A reset link has been sent.'),
          error: failed,
        });
        break;

      case 'revokeAllSessions':
        this.api.forceSignOut(id, reason).subscribe({
          next: (outcome) => done(outcome.message ?? 'Signed out everywhere.'),
          error: failed,
        });
        break;

      case 'revokeSession': {
        const session = this.targetSession();

        if (!session) {
          this.actionProcessing.set(false);
          return;
        }

        this.api.revokeUserSession(id, session.id, reason).subscribe({
          next: (outcome) => done(outcome.message ?? 'That session has ended.'),
          error: failed,
        });
        break;
      }

      case 'resetMfa':
        this.api.resetUserMfa(id, reason).subscribe({
          next: (outcome) => done(outcome.message ?? 'Two-step verification has been reset.'),
          error: failed,
        });
        break;

      case 'unlockAccount':
        this.api.unlockUser(id, version, reason).subscribe({
          next: (outcome) => done(outcome.message ?? 'The account is unlocked.'),
          error: failed,
        });
        break;

      case 'removeTrustedDevice': {
        const device = this.targetDevice();

        if (!device) {
          this.actionProcessing.set(false);
          return;
        }

        this.api.revokeUserTrustedDevice(id, device.id, reason).subscribe({
          next: (outcome) => done(outcome.message ?? 'That device has been forgotten.'),
          error: failed,
        });
        break;
      }

      case 'exportSecurityEvidence':
        this.api.exportUserSecurity(id).subscribe({
          next: (blob) => {
            this.directory.saveBlob(
              blob, `security-${this.data()?.user.reference ?? id}.csv`);
            done('The file has been downloaded.');
          },
          error: failed,
        });
        break;
    }
  }

  // =========================================================================================
  // Wording
  // =========================================================================================

  private mfaRequirementLabel(model: UserSecurityResponse): string {
    if (model.isMfaEffectivelyRequired) {
      return model.mfaEnabled ? 'Required · enrolled' : 'Required · not set up';
    }

    return model.mfaEnabled ? 'Optional · enrolled' : 'Optional · not set up';
  }

  /**
   * The one-line risk summary on the header.
   *
   * Ordered worst first, because the tile shows one line and the worst thing is the one
   * somebody needs to see.
   */
  private riskSummary(model: UserSecurityResponse): string {
    if (model.isLockedOut) {
      return 'Locked out';
    }

    if (model.mustChangePassword) {
      return 'Must change password';
    }

    if (!model.mfaEnabled && model.isMfaEffectivelyRequired) {
      return 'Two-step verification required but not set up';
    }

    if ((model.accessFailedCount ?? 0) > 0) {
      return `${model.accessFailedCount} failed attempt(s)`;
    }

    return 'Nothing outstanding';
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
      case 'authenticatorApp': return 'uil-mobile-android';
      case 'sms': return 'uil-comment-alt-message';
      case 'email': return 'uil-envelope';
      case 'securityKey': return 'uil-key-skeleton';
      default: return 'uil-shield-check';
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

  private clientTypeIcon(clientType: ClientType | undefined): string {
    switch (clientType) {
      case 'web': return 'uil-desktop';
      case 'mobile': return 'uil-mobile-android';
      case 'desktop': return 'uil-laptop';
      case 'api': return 'uil-server-network';
      default: return 'uil-question-circle';
    }
  }

  private formatDate(value: string): string {
    try {
      return new Date(value).toLocaleDateString('en-IN',
        { day: '2-digit', month: 'short', year: 'numeric' });
    } catch {
      return value;
    }
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
    if (this.userReference()) {
      this.router.navigate(
        ['/app/administration/access/user-profile-and-access', this.userReference()]);
    } else {
      this.router.navigate(['/app/administration/access/user-directory']);
    }
  }
}
