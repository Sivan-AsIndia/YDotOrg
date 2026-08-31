import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import QRCode from 'qrcode';
import { AuthApiService } from '../../../../../Service/auth-api.service';
import { ToastService } from '../../../../../Shared/services/toast.service';
import { AuthSessionService } from '../../../../../Shared/services/auth-session.service';
import {
  InvitationPreviewResponse,
  MFA_METHOD_LABELS,
  MfaEnrolmentResponse,
  MfaMethodType,
  nextRouteFor,
} from '../../../../../Shared/models/auth.model';

/**
 * IAM-AUTH-02 — Accept invitation and activate account.
 *
 * THE FOUR STEPS
 * --------------
 *   1. **Your details** — read-only facts pulled from the invitation token, plus
 *      "Cancel and edit" for when something is wrong. Nothing is editable here on purpose: these
 *      are the terms of the access an administrator approved, and letting the invited person
 *      change their own role or scope would defeat the approval entirely.
 *   2. **Password** — with a live checklist so nothing is a surprise at submit time.
 *   3. **Two-step verification** — OPTIONAL. Skipping it activates the account normally; the
 *      person can add a factor later from My Security.
 *   4. **Done** — the recovery codes, shown once and never again.
 *
 * HOW AUTHENTICATOR SETUP ACTUALLY WORKS HERE
 * -------------------------------------------
 *   a. Picking "Authenticator application" calls `begin-mfa-enrolment`. The server creates a
 *      random shared secret, keeps an encrypted copy, and returns the readable copy exactly once.
 *   b. This screen turns the returned `otpauth://` URI into a QR code with the `qrcode` library.
 *      Scanning it copies the secret into the phone. There is nothing to type.
 *   c. The phone then computes a fresh six-digit code every thirty seconds, from the secret and
 *      the clock. Nothing is ever sent between phone and server — which is why it works offline.
 *   d. The person types the current code, and `verify-mfa-method` recomputes the same code
 *      server-side. Matching proves the secret arrived intact, so the method goes Active.
 *
 * That is why the QR code appears **here**, once, and never again on the sign-in screen.
 */
type Step = 'details' | 'password' | 'security' | 'complete';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './register.html',
  styleUrl: './register.css',
})
export class RegisterComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly authApi = inject(AuthApiService);
  private readonly session = inject(AuthSessionService);

  // ---- Screen state --------------------------------------------------------------------------
  readonly step = signal<Step>('details');
  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal('');
  readonly infoMessage = signal('');
  readonly token = signal('');
  readonly invitation = signal<InvitationPreviewResponse | null>(null);

  // ---- Step 2: password -----------------------------------------------------------------------
  password = '';
  confirmPassword = '';
  readonly passwordValue = signal('');
  readonly confirmValue = signal('');
  readonly showPassword = signal(false);
  readonly showConfirm = signal(false);
  readonly termsAccepted = signal(false);

  // ---- Step 3: second factor --------------------------------------------------------------------
  readonly enrolMfa = signal(false);
  readonly selectedMethod = signal<MfaMethodType>('authenticatorApp');
  readonly setup = signal<MfaEnrolmentResponse | null>(null);
  readonly qrDataUrl = signal('');
  readonly qrFailed = signal(false);
  readonly settingUp = signal(false);
  readonly methodVerified = signal(false);
  readonly secretVisible = signal(false);
  readonly secretCopied = signal(false);
  /** A signal, so canVerifyMethod re-evaluates as the passcode is typed. */
  readonly verificationCode = signal('');
  mobileCountryCode = '+91';
  mobileNumber = '';

  // ---- Step 4: recovery codes ---------------------------------------------------------------------
  readonly recoveryCodes = signal<string[]>([]);
  readonly recoveryNotice = signal('');
  readonly recoveryAcknowledged = signal(false);
  readonly mfaEnrolledResult = signal(false);
  readonly userCode = signal('');

  // ---- "Cancel and edit" dialog ---------------------------------------------------------------------
  readonly showCancelDialog = signal(false);
  cancelReason = '';

  /**
   * Dialling prefixes from the country catalogue, deduplicated and in country sort order.
   *
   * Was ten literals, which meant a country added on the Masters screen could be selected on
   * every other form in the platform but not dialled from this one.
   *
   * THEY COME FROM THE INVITATION PREVIEW, NOT FROM `GeoMasterService`. This screen is
   * anonymous — that is the whole point of it — and the lookup endpoint is gated on
   * ActiveUserOnly. Calling it here answered 401, the interceptor tried to renew a session that
   * had never existed, the renewal failed, and the person who had just followed an invitation
   * link was redirected to a bare sign-in form. The preview call is already made below, is
   * anonymous by design, and now carries these.
   */
  readonly countryCodes = signal<readonly string[]>([]);

  // =========================================================================================
  // Password rules
  // =========================================================================================

  readonly minimumLength = computed(() => this.invitation()?.passwordMinimumLength ?? 12);

  // Each rule is checked only when the SERVER says it applies. Hard-coding all five would mean
  // the checklist demands a symbol on an Organisation whose policy does not, and the person
  // would be told their perfectly acceptable password is wrong.
  readonly hasLength = computed(() => this.passwordValue().length >= this.minimumLength());

  readonly hasUpper = computed(
    () => !this.requires('passwordRequireUppercase') || /[A-Z]/.test(this.passwordValue()));

  readonly hasLower = computed(
    () => !this.requires('passwordRequireLowercase') || /[a-z]/.test(this.passwordValue()));

  readonly hasNumber = computed(
    () => !this.requires('passwordRequireDigit') || /\d/.test(this.passwordValue()));

  readonly hasSymbol = computed(
    () => !this.requires('passwordRequireNonAlphanumeric') || /[^A-Za-z0-9]/.test(this.passwordValue()));

  readonly rulesMet = computed(
    () => [this.hasLength(), this.hasUpper(), this.hasLower(), this.hasNumber(), this.hasSymbol()].filter(Boolean).length,
  );

  /** Whether the Organisation policy insists on one of the optional character classes. */
  private requires(
    rule: 'passwordRequireUppercase' | 'passwordRequireLowercase'
      | 'passwordRequireDigit' | 'passwordRequireNonAlphanumeric',
  ): boolean {
    // Defaults to true when the invitation has not loaded, so the checklist starts strict and
    // relaxes once the real policy arrives - never the other way round.
    return this.invitation()?.[rule] ?? true;
  }

  readonly strengthPercent = computed(() => this.rulesMet() * 20);
  readonly strengthLabel = computed(
    () => ['Very weak', 'Very weak', 'Weak', 'Fair', 'Good', 'Strong'][this.rulesMet()] ?? 'Very weak',
  );
  readonly strengthClass = computed(() => {
    const met = this.rulesMet();
    return met <= 2 ? 'bg-danger' : met === 3 ? 'bg-warning' : met === 4 ? 'bg-info' : 'bg-success';
  });

  readonly passwordsMatch = computed(
    () => this.confirmValue().length > 0 && this.confirmValue() === this.passwordValue(),
  );

  readonly canLeavePasswordStep = computed(
    () => this.rulesMet() === 5 && this.passwordsMatch() && this.termsAccepted(),
  );

  // =========================================================================================
  // Second-factor rules
  // =========================================================================================

  readonly isAuthenticatorSelected = computed(() => this.selectedMethod() === 'authenticatorApp');
  readonly needsMobileNumber = computed(() => this.selectedMethod() === 'sms');
  readonly mfaMandatory = computed(() => this.invitation()?.mfaMandatory ?? false);

  /** The factors this Organisation permits, in the order the server wants them offered. */
  readonly allowedMethods = computed<MfaMethodType[]>(
    () => this.invitation()?.allowedMfaMethods ?? ['authenticatorApp', 'email', 'sms']);

  readonly canVerifyMethod = computed(
    () => this.setup() !== null && this.verificationCode().trim().length >= 6 && !this.submitting(),
  );

  /** Activation is allowed when MFA was skipped, or when a chosen factor is confirmed. */
  readonly canActivate = computed(() => {
    if (!this.enrolMfa()) {
      return !this.mfaMandatory() && !this.submitting();
    }

    return this.methodVerified() && !this.submitting();
  });

  readonly stepNumber = computed(() => {
    switch (this.step()) {
      case 'details': return 1;
      case 'password': return 2;
      case 'security': return 3;
      default: return 4;
    }
  });

  readonly canCancelActivation = computed(() => this.cancelReason.trim().length >= 3);

  // =========================================================================================
  // Lifecycle
  // =========================================================================================

  ngOnInit(): void {
    // The e-mail sends ?token=…; /auth/invitation/:token stays supported for links already out.
    const token = this.route.snapshot.queryParamMap.get('token') ?? this.route.snapshot.paramMap.get('token') ?? '';
    this.token.set(token);

    if (!token) {
      this.loading.set(false);
      this.errorMessage.set('This activation link is incomplete. Open the link from your invitation e-mail again.');
      return;
    }

    this.authApi.previewInvitation(token).subscribe({
      next: (view) => {
        this.loading.set(false);
        this.invitation.set(view);

        // Never empty in practice, but an empty list leaves the prefix picker blank rather than
        // breaking the step — the number itself is still typed and still accepted.
        this.countryCodes.set(view.dialingCodes ?? []);

        if (!view.isValid) {
          this.errorMessage.set(view.message ?? 'That invitation link is not valid.');
          return;
        }

        // Start on whichever factor the Organisation lists first, rather than assuming an
        // authenticator is available.
        const [first] = view.allowedMfaMethods ?? [];
        if (first) {
          this.selectedMethod.set(first);
        }

        // When the organisation insists on a second factor, the toggle starts on and cannot be
        // turned off. Otherwise it starts off and adding one is genuinely the person's choice.
        this.enrolMfa.set(view.mfaMandatory === true);
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.errorMessage.set(error.message);
      },
    });
  }

  // =========================================================================================
  // Navigation
  // =========================================================================================

  goToPassword(): void {
    this.step.set('password');
    this.errorMessage.set('');
  }

  goToSecurity(): void {
    if (!this.canLeavePasswordStep()) {
      return;
    }

    this.step.set('security');
    this.errorMessage.set('');
  }

  backToDetails(): void {
    this.step.set('details');
    this.errorMessage.set('');
  }

  backToPassword(): void {
    this.step.set('password');
    this.errorMessage.set('');
  }

  // =========================================================================================
  // Step 2 inputs
  // =========================================================================================

  onPasswordInput(value: string): void {
    this.password = value;
    this.passwordValue.set(value);
  }

  onConfirmInput(value: string): void {
    this.confirmPassword = value;
    this.confirmValue.set(value);
  }

  togglePassword(): void {
    this.showPassword.update((shown) => !shown);
  }

  toggleConfirm(): void {
    this.showConfirm.update((shown) => !shown);
  }

  toggleTerms(checked: boolean): void {
    this.termsAccepted.set(checked);
  }

  // =========================================================================================
  // Step 3 — second factor
  // =========================================================================================

  /** Turning the toggle off discards any half-finished setup, so nothing dangles. */
  toggleEnrolMfa(enabled: boolean): void {
    if (this.mfaMandatory() && !enabled) {
      return;
    }

    this.enrolMfa.set(enabled);
    this.errorMessage.set('');

    if (!enabled) {
      this.setup.set(null);
      this.qrDataUrl.set('');
      this.methodVerified.set(false);
      this.verificationCode.set('');
    }
  }

  chooseMethod(method: MfaMethodType): void {
    if (this.selectedMethod() === method) {
      return;
    }

    // Switching method invalidates whatever was set up for the previous one.
    this.selectedMethod.set(method);
    this.setup.set(null);
    this.qrDataUrl.set('');
    this.methodVerified.set(false);
    this.verificationCode.set('');
    this.errorMessage.set('');
  }

  /** How each factor is described on the picker. Kept beside the hint so the two stay in step. */
  methodLabel(method: MfaMethodType): string {
    return MFA_METHOD_LABELS[method];
  }

  /** One line of plain explanation under each option, so the choice is an informed one. */
  methodHint(method: MfaMethodType): string {
    switch (method) {
      case 'authenticatorApp':
        return 'Codes are generated on your phone and work without a signal.';
      case 'email':
        return 'A code is e-mailed to you each time you sign in.';
      case 'sms':
        return 'A code is sent by text message each time you sign in.';
      default:
        return '';
    }
  }

  /** Starts the setup: creates the secret (or sends a code) and draws the QR. */
  startSetup(): void {
    if (this.needsMobileNumber() && !this.mobileNumber.trim()) {
      this.errorMessage.set('Enter your mobile number so the code can be sent to it.');
      return;
    }

    this.settingUp.set(true);
    this.errorMessage.set('');
    this.qrFailed.set(false);

    this.authApi
      .beginInvitationMfaEnrolment({
        token: this.token(),
        methodType: this.selectedMethod(),
        mobileCountryCode: this.needsMobileNumber() ? this.mobileCountryCode : undefined,
        mobileNumber: this.needsMobileNumber() ? this.mobileNumber.trim() : undefined,
      })
      .subscribe({
        next: (setup: MfaEnrolmentResponse) => {
          this.settingUp.set(false);
          this.setup.set(setup);
          this.infoMessage.set(setup.message ?? '');

          if (setup.provisioningUri) {
            void this.renderQrCode(setup.provisioningUri);
          }
        },
        error: (error: Error) => {
          this.settingUp.set(false);
          this.errorMessage.set(error.message);
        },
      });
  }

  /**
   * Draws the otpauth URI as a QR code.
   *
   * A data URL on an <img> is used rather than a <canvas>, because a canvas only exists once
   * Angular has rendered that branch of the template — which was the cause of the old "Generating
   * QR code…" spinner that sometimes never went away. An image has no such timing problem.
   */
  private async renderQrCode(otpAuthUri: string): Promise<void> {
    try {
      const dataUrl = await QRCode.toDataURL(otpAuthUri, {
        width: 220,
        margin: 1,
        errorCorrectionLevel: 'M',
        color: { dark: '#1F2430', light: '#FFFFFF' },
      });

      this.qrDataUrl.set(dataUrl);
      this.qrFailed.set(false);
    } catch {
      // Not fatal: the typeable secret below the QR is an equally valid way to enrol.
      this.qrFailed.set(true);
      this.secretVisible.set(true);
    }
  }

  toggleSecret(): void {
    this.secretVisible.update((shown) => !shown);
  }

  copySecret(): void {
    const secret = this.setup()?.sharedSecret;
    if (!secret) {
      return;
    }

    void navigator.clipboard.writeText(secret.replace(/\s/g, '')).then(() => {
      this.secretCopied.set(true);
      setTimeout(() => this.secretCopied.set(false), 2000);
    });
  }

  /** Confirms the setup with the first passcode, before the account is activated. */
  verifyMethod(): void {
    const setup = this.setup();
    if (!setup || !this.canVerifyMethod()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.authApi
      .verifyInvitationMfaMethod({
        token: this.token(),
        methodId: setup.methodId!,
        code: this.verificationCode().trim(),
      })
      .subscribe({
        next: (outcome) => {
          this.submitting.set(false);
          this.methodVerified.set(true);
          this.infoMessage.set(outcome.message ?? '');
          this.toast.show('Verified', 'Your second factor is confirmed.', 'success');
        },
        error: (error: Error) => {
          this.submitting.set(false);
          this.verificationCode.set('');
          this.errorMessage.set(error.message);
        },
      });
  }

  // =========================================================================================
  // Step 4 — activate
  // =========================================================================================

  activateAccount(): void {
    if (!this.canActivate()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.authApi
      .acceptInvitation({
        token: this.token(),
        password: this.password,
        confirmPassword: this.confirmPassword,
        acceptTerms: this.termsAccepted(),
        mobileCountryCode: this.needsMobileNumber() ? this.mobileCountryCode : undefined,
        mobileNumber: this.needsMobileNumber() ? this.mobileNumber.trim() : undefined,
        clientType: 'web',
      })
      .subscribe({
        next: (activation) => {
          this.submitting.set(false);

          this.recoveryCodes.set(activation.recoveryCodes ?? []);
          this.recoveryNotice.set(activation.recoveryCodeNotice ?? '');
          this.mfaEnrolledResult.set(activation.mfaEnrolled === true);
          this.userCode.set(activation.user?.code ?? '');

          // Activation signs the person in, so the token is stored right away. They still have to
          // acknowledge the recovery codes before the app moves on - this screen is the only
          // chance they will ever get to read them.
          this.session.startSession(activation);

          this.step.set('complete');
          this.password = '';
          this.confirmPassword = '';

          this.toast.show(
            'Account activated', activation.message ?? 'Your account is active.', 'success');
        },
        error: (error: Error) => {
          this.submitting.set(false);
          this.errorMessage.set(error.message);
        },
      });
  }

  // =========================================================================================
  // Recovery codes
  // =========================================================================================

  copyRecoveryCodes(): void {
    void navigator.clipboard.writeText(this.recoveryCodes().join('\n')).then(() => {
      this.toast.show('Copied', 'Your recovery codes are on the clipboard.', 'success');
    });
  }

  /** Offers the codes as a plain text file, which is easier to store safely than a screenshot. */
  downloadRecoveryCodes(): void {
    const content = [
      'YDot recovery codes',
      `Account: ${this.userCode()}`,
      `Generated: ${new Date().toISOString()}`,
      '',
      'Each code signs you in once if you lose access to your second factor.',
      'Keep this file somewhere safe and offline.',
      '',
      ...this.recoveryCodes(),
    ].join('\n');

    const blob = new Blob([content], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);

    const link = document.createElement('a');
    link.href = url;
    link.download = `ydot-recovery-codes-${this.userCode() || 'account'}.txt`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  }

  acknowledgeRecoveryCodes(checked: boolean): void {
    this.recoveryAcknowledged.set(checked);
  }

  goToDashboard(): void {
    void this.router.navigate(['/app/dashboard']);
  }

  // =========================================================================================
  // Cancel and edit
  // =========================================================================================

  openCancelDialog(): void {
    this.showCancelDialog.set(true);
    this.cancelReason = '';
  }

  closeCancelDialog(): void {
    this.showCancelDialog.set(false);
    this.cancelReason = '';
  }

  /**
   * "Cancel and edit" — for when the details on step 1 are wrong.
   *
   * The reason is recorded against the invitation so an administrator can see what needs fixing.
   * The invitation itself stays valid: the same link works again once the record is corrected,
   * which is far kinder than voiding it and making somebody request a whole new one.
   */
  confirmCancel(): void {
    if (!this.canCancelActivation()) {
      return;
    }

    this.submitting.set(true);

    this.authApi.cancelActivation(this.token(), this.cancelReason.trim()).subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        this.showCancelDialog.set(false);
        this.toast.show(
          'Saved for later',
          outcome.message ?? 'You can return to your invitation link whenever you are ready.',
          'info');
        void this.router.navigate(['/auth/sign-in']);
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.errorMessage.set(error.message);
        this.showCancelDialog.set(false);
      },
    });
  }

  /** Asks for a replacement when the invitation has expired. */
  requestNewInvitation(): void {
    this.submitting.set(true);
    this.errorMessage.set('');

    this.authApi.requestNewInvitation(this.token()).subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        const message = outcome.message
          ?? 'If that invitation is still open, a new link is on its way.';

        this.infoMessage.set(message);
        this.toast.show('New invitation sent', message, 'success');
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.errorMessage.set(error.message);
      },
    });
  }

  goToSignIn(): void {
    void this.router.navigate(['/auth/sign-in']);
  }
}
