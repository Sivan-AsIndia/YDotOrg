import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthApiService } from '../../../../../Service/auth-api.service';
import { ToastService } from '../../../../../Shared/services/toast.service';
import { AuthSessionService } from '../../../../../Shared/services/auth-session.service';
import { DeviceIdentityService } from '../../../../../Shared/services/device-identity.service';
import { MfaHandoffService } from '../../../../../Shared/services/mfa-handoff.service';
import {
  MFA_METHOD_LABELS,
  MfaChallengeResponse,
  MfaMethodType,
  SignInResponse,
  nextRouteFor,
} from '../../../../../Shared/models/auth.model';
import { MfaMethodOptionResponse } from '../../../../../Shared/models/iam-contract.model';

/**
 * IAM-AUTH-05 — the second-factor step of an ordinary sign-in.
 *
 * WHAT THIS SCREEN IS, AND WHAT IT IS NOT
 * ---------------------------------------
 * This screen only ever *uses* a second factor that already exists. It never sets one up. Setting
 * up an authenticator application — QR code, secret key, first passcode — happens once, during
 * activation, on the invitation stepper. Mixing the two on one screen was the confusion in the
 * previous version: a returning user was shown a QR code to scan every time they signed in, which
 * made no sense and, worse, implied their existing setup had been discarded.
 *
 * So the rule is simple:
 *   • first time  → /auth/invitation, step 3, scan the QR and confirm with a passcode
 *   • every time after that → this screen, type the current code
 *
 * TWO WAYS IN, ONE SCREEN
 * -----------------------
 * • **Code** — six digits. For an authenticator app the phone computes them and nothing is sent;
 *   for e-mail the server sends them. `codeWasSent` from the API tells the two apart, and the
 *   wording, the countdown and the Resend button all follow from it.
 * • **Recovery code** — one of the ten codes issued at activation, for when the phone is gone.
 *   Each works once.
 */
type EntryMode = 'code' | 'recovery';

@Component({
  selector: 'app-mfa-challenge',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './mfa-challenge.html',
  styleUrl: './mfa-challenge.css',
})
export class MfaChallengeComponent implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly authApi = inject(AuthApiService);
  private readonly session = inject(AuthSessionService);
  private readonly device = inject(DeviceIdentityService);
  private readonly handoff = inject(MfaHandoffService);

  // ---- Challenge state -------------------------------------------------------------------
  readonly challenge = signal<MfaChallengeResponse | null>(null);
  readonly mode = signal<EntryMode>('code');
  readonly submitting = signal(false);
  readonly resending = signal(false);
  readonly errorMessage = signal('');
  readonly infoMessage = signal('');

  // ---- Input -----------------------------------------------------------------------------
  /** Six separate boxes read better than one field and make paste and backspace behave. */
  readonly digits = signal<string[]>(['', '', '', '', '', '']);
  /** A signal, so canSubmitRecovery below actually re-evaluates as it is typed. */
  readonly recoveryCode = signal('');
  rememberDevice = false;

  /** Seconds until the challenge expires. Drives the countdown. */
  readonly secondsRemaining = signal(0);
  private countdown: ReturnType<typeof setInterval> | null = null;

  // ---- Derived ----------------------------------------------------------------------------

  /** True for an authenticator application: nothing was sent, so no Resend and no destination. */
  readonly isAuthenticatorApp = computed(() => this.challenge()?.methodType === 'authenticatorApp');

  readonly code = computed(() => this.digits().join(''));
  readonly canSubmitCode = computed(() => this.code().length === 6 && !this.submitting());
  readonly canSubmitRecovery = computed(() => this.recoveryCode().trim().length >= 8 && !this.submitting());

  readonly alternatives = computed<MfaMethodOptionResponse[]>(
    () => this.challenge()?.availableMethods ?? []);
  readonly recoveryAvailable = computed(() => this.challenge()?.recoveryCodeAccepted ?? false);
  readonly attemptsLeft = computed(() => this.challenge()?.attemptsRemaining ?? 0);
  readonly expired = computed(() => this.secondsRemaining() <= 0);

  readonly countdownLabel = computed(() => {
    const total = this.secondsRemaining();
    const minutes = Math.floor(total / 60);
    const seconds = total % 60;
    return `${minutes}:${seconds.toString().padStart(2, '0')}`;
  });

  // =========================================================================================
  // Lifecycle
  // =========================================================================================

  ngOnInit(): void {
    const handoff = this.handoff.mfa();

    // Landing here without a challenge means somebody typed the URL, or the transaction has been
    // cleared. There is nothing to verify, so go back to the start rather than show a dead form.
    if (!handoff) {
      void this.router.navigate(['/auth/sign-in']);
      return;
    }

    this.challenge.set(handoff.challenge);
    this.rememberDevice = handoff.rememberDevice;
    this.infoMessage.set(handoff.challenge.instruction ?? '');
    this.startCountdown(handoff.challenge.expiresAtUtc);
  }

  ngOnDestroy(): void {
    this.stopCountdown();
  }

  // =========================================================================================
  // Code entry
  // =========================================================================================

  onDigitInput(index: number, value: string): void {
    const digit = value.replace(/\D/g, '').slice(-1);
    const next = [...this.digits()];
    next[index] = digit;
    this.digits.set(next);
    this.errorMessage.set('');

    if (digit && index < 5) {
      this.focusBox(index + 1);
    }
  }

  onDigitKeydown(index: number, event: KeyboardEvent): void {
    if (event.key === 'Backspace' && !this.digits()[index] && index > 0) {
      this.focusBox(index - 1);
    } else if (event.key === 'ArrowLeft' && index > 0) {
      this.focusBox(index - 1);
    } else if (event.key === 'ArrowRight' && index < 5) {
      this.focusBox(index + 1);
    }
  }

  /** Pasting the whole code from a password manager or an SMS should just work. */
  onPaste(event: ClipboardEvent): void {
    const pasted = (event.clipboardData?.getData('text') ?? '').replace(/\D/g, '');
    if (pasted.length < 6) {
      return;
    }

    event.preventDefault();
    this.digits.set(pasted.slice(0, 6).split(''));
    this.focusBox(5);
  }

  private focusBox(index: number): void {
    // A microtask delay lets Angular finish rendering before the focus moves.
    setTimeout(() => document.getElementById(`mfa-digit-${index}`)?.focus(), 0);
  }

  // =========================================================================================
  // Actions
  // =========================================================================================

  verifyCode(): void {
    const challenge = this.challenge();
    if (!challenge || !this.canSubmitCode()) {
      return;
    }

    if (this.expired()) {
      this.errorMessage.set('This sign-in has expired. Start again from the sign-in screen.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.authApi
      .verifyMfa({
        challengeToken: challenge.challengeToken,
        code: this.code(),
        trustThisDevice: this.rememberDevice,
        deviceIdentifier: this.device.getDeviceIdentifier(),
        deviceName: this.device.getDeviceName(),
        clientType: 'web',
      })
      .subscribe({
        next: (response: SignInResponse) =>
          this.completeSignIn(response, 'Two-factor authentication successful.'),
        error: (error: Error) => this.handleFailure(error),
      });
  }

  useRecoveryCode(): void {
    const challenge = this.challenge();
    if (!challenge || !this.canSubmitRecovery()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.authApi
      .redeemRecoveryCode({
        challengeToken: challenge.challengeToken,
        recoveryCode: this.recoveryCode().trim(),
      })
      .subscribe({
        next: (response: SignInResponse) => this.completeSignIn(response, 'Recovery code accepted.'),
        error: (error: Error) => this.handleFailure(error),
      });
  }

  /** Only offered for e-mail and SMS: an authenticator app has nothing to resend. */
  resendCode(): void {
    const challenge = this.challenge();
    if (!challenge || this.isAuthenticatorApp()) {
      return;
    }

    this.resending.set(true);
    this.errorMessage.set('');

    this.authApi.resendMfaChallenge(challenge.challengeToken ?? '').subscribe({
      next: (updated: MfaChallengeResponse) => {
        this.resending.set(false);
        this.applyChallenge(updated);
        this.digits.set(['', '', '', '', '', '']);
        this.toast.show('Code sent', updated.instruction ?? 'A new code is on its way.', 'info');
      },
      error: (error: Error) => {
        this.resending.set(false);
        this.errorMessage.set(error.message);
      },
    });
  }

  switchMethod(methodId: string): void {
    const challenge = this.challenge();
    if (!challenge) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.authApi.switchMfaMethod(challenge.challengeToken ?? '', methodId).subscribe({
      next: (updated: MfaChallengeResponse) => {
        this.submitting.set(false);
        this.applyChallenge(updated);
        this.digits.set(['', '', '', '', '', '']);
        this.mode.set('code');
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.errorMessage.set(error.message);
      },
    });
  }

  cancel(): void {
    const challenge = this.challenge();

    const leave = () => {
      this.handoff.clear();
      void this.router.navigate(['/auth/sign-in']);
    };

    if (!challenge) {
      leave();
      return;
    }

    // Telling the server means the half-finished transaction is closed rather than left to rot,
    // and the cancellation shows up in the audit trail.
    this.authApi
      .cancelMfaChallenge(challenge.challengeToken ?? '', 'Cancelled from the verification screen.')
      .subscribe({ next: leave, error: leave });
  }

  /**
   * How a second factor is described when the server did not supply a label.
   *
   * The server names an enrolled method when the person gave it one ("Work phone"); this fills
   * the gap for methods enrolled without a label, so the button never reads "Use ".
   */
  methodLabel(method: MfaMethodType | undefined): string {
    return method ? MFA_METHOD_LABELS[method] : 'another method';
  }

  setMode(mode: EntryMode): void {
    this.mode.set(mode);
    this.errorMessage.set('');
  }

  // =========================================================================================
  // Internals
  // =========================================================================================

  private completeSignIn(response: SignInResponse, message: string): void {
    this.submitting.set(false);

    // READ BEFORE THE CLEAR, which wipes the handoff this is stored in. Sign-in put the page the
    // person originally asked for here rather than in the URL; without carrying it across this
    // last hop, every second-factor sign-in ended on the default landing route and the deep link
    // that started the whole flow was silently discarded.
    const returnUrl = this.handoff.mfa()?.returnUrl ?? null;

    this.handoff.clear();
    this.session.startSession(response);
    this.toast.show('Signed in', message, 'success');

    // A root user finishing MFA still has no Organisation chosen, so nextRouteFor sends them to
    // the picker rather than to a dashboard that would have nothing to show.
    const destination = nextRouteFor(response);

    // STILL NOT SIGNED ALL THE WAY IN. When the next stop is the Organisation picker or a forced
    // password change, going to the returnUrl now would bounce straight back here - so it is
    // handed on as a parameter for that screen to honour once it is done, exactly as sign-in
    // hands it on for the same two cases.
    if (response.status === 'tenantSelectionRequired' || response.status === 'passwordChangeRequired') {
      void this.router.navigate([destination], {
        queryParams: {
          returnUrl: returnUrl ?? undefined,
          token: response.status === 'passwordChangeRequired'
            ? (response.passwordResetToken ?? undefined)
            : undefined,
        },
      });
      return;
    }

    void this.router.navigateByUrl(returnUrl ?? destination);
  }

  private handleFailure(error: Error): void {
    this.submitting.set(false);
    this.digits.set(['', '', '', '', '', '']);
    this.errorMessage.set(error.message);

    // The server counts the attempts, so the number it reports is the number that matters. When
    // it hits zero the transaction is dead and the only way forward is to start again.
    const current = this.challenge();
    if (current) {
      const remaining = Math.max(0, (current.attemptsRemaining ?? 1) - 1);
      this.challenge.set({ ...current, attemptsRemaining: remaining });

      if (remaining === 0) {
        this.toast.show('Too many attempts', 'Start sign in again.', 'error');
        setTimeout(() => {
          this.handoff.clear();
          void this.router.navigate(['/auth/sign-in']);
        }, 1500);
      }
    }
  }

  private applyChallenge(updated: MfaChallengeResponse): void {
    const current = this.challenge();
    const merged: MfaChallengeResponse = {
      ...updated,
      challengeToken: updated.challengeToken || current?.challengeToken || '',
    };

    this.challenge.set(merged);
    this.handoff.updateChallenge(merged);
    this.infoMessage.set(merged.instruction ?? '');
    this.startCountdown(merged.expiresAtUtc);
  }

  private startCountdown(expiresAtUtc: string | undefined): void {
    if (!expiresAtUtc) {
      this.secondsRemaining.set(0);
      return;
    }

    this.stopCountdown();

    const tick = () => {
      const remaining = Math.floor((Date.parse(expiresAtUtc) - Date.now()) / 1000);
      this.secondsRemaining.set(Math.max(0, remaining));

      if (remaining <= 0) {
        this.stopCountdown();
      }
    };

    tick();
    this.countdown = setInterval(tick, 1000);
  }

  private stopCountdown(): void {
    if (this.countdown) {
      clearInterval(this.countdown);
      this.countdown = null;
    }
  }
}
