import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthApiService } from '../../../../../Service/auth-api.service';
import { ToastService } from '../../../../../Shared/services/toast.service';
import { AuthTokenService } from '../../../../../Shared/services/auth-token.service';
import { ResetPasswordViewResponse } from '../../../../../Shared/models/auth.model';

/**
 * IAM-AUTH-04 — Set a new password from a recovery link.
 *
 * ONE SCREEN, TWO ERRANDS
 * -----------------------
 * The same screen serves both links the API sends:
 *
 *   • **Forgot password** — set a new password.
 *   • **Reactivate account** (`?mode=reactivate`) — sent when a suspended person clicks
 *     "Start account recovery". Setting the password also lifts the suspension, in one step,
 *     because bouncing them back to "account unavailable" afterwards would be pointless.
 *
 * The server decides which of the two a token actually is; `mode` in the URL only changes the
 * wording, so a tampered query string cannot turn a password reset into a reactivation.
 *
 * THE TOKEN IS CHECKED BEFORE THE FORM IS DRAWN
 * ---------------------------------------------
 * `GET /users/reset-password?token=…` runs first. An expired or already-used link therefore fails
 * immediately, with a "request a new link" button, instead of letting somebody type a password
 * twice and only then discover the link was dead.
 */
@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './reset-password.html',
  styleUrl: './reset-password.css',
})
export class ResetPasswordComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly authApi = inject(AuthApiService);
  private readonly tokens = inject(AuthTokenService);

  // ---- Screen state ------------------------------------------------------------------------
  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly completed = signal(false);
  readonly errorMessage = signal('');
  readonly successMessage = signal('');
  readonly view = signal<ResetPasswordViewResponse | null>(null);
  readonly token = signal('');
  readonly isReactivation = signal(false);

  // ---- Form --------------------------------------------------------------------------------
  password = '';
  confirmPassword = '';
  readonly passwordValue = signal('');
  readonly confirmValue = signal('');
  readonly showPassword = signal(false);
  readonly showConfirm = signal(false);

  // ---- Asking for a replacement link ----------------------------------------------------------
  readonly showRequestNew = signal(false);
  emailOrUsername = '';
  readonly requestingNew = signal(false);

  // ---- Countdown on the link's validity ---------------------------------------------------------
  readonly secondsRemaining = signal(0);
  private countdown: ReturnType<typeof setInterval> | null = null;

  // =========================================================================================
  // Password rules, evaluated live so the checklist ticks as the person types
  // =========================================================================================

  readonly minimumLength = computed(() => this.view()?.passwordMinimumLength ?? 12);

  readonly hasLength = computed(() => this.passwordValue().length >= this.minimumLength());
  readonly hasUpper = computed(() => /[A-Z]/.test(this.passwordValue()));
  readonly hasLower = computed(() => /[a-z]/.test(this.passwordValue()));
  readonly hasNumber = computed(() => /\d/.test(this.passwordValue()));
  readonly hasSymbol = computed(() => /[^A-Za-z0-9]/.test(this.passwordValue()));

  readonly rulesMet = computed(
    () => [this.hasLength(), this.hasUpper(), this.hasLower(), this.hasNumber(), this.hasSymbol()].filter(Boolean).length,
  );

  readonly strengthPercent = computed(() => this.rulesMet() * 20);

  readonly strengthLabel = computed(() => {
    const met = this.rulesMet();
    return ['Very weak', 'Very weak', 'Weak', 'Fair', 'Good', 'Strong'][met] ?? 'Very weak';
  });

  readonly strengthClass = computed(() => {
    const met = this.rulesMet();
    return met <= 2 ? 'bg-danger' : met === 3 ? 'bg-warning' : met === 4 ? 'bg-info' : 'bg-success';
  });

  readonly passwordsMatch = computed(
    () => this.confirmValue().length > 0 && this.confirmValue() === this.passwordValue(),
  );

  readonly canSubmit = computed(
    () => this.rulesMet() === 5 && this.passwordsMatch() && !this.submitting() && !this.linkExpired(),
  );

  readonly linkExpired = computed(() => this.view() !== null && !this.view()!.isTokenValid);

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
    // The token arrives as ?token=… from the e-mail; /auth/reset-password/:token is kept working
    // so older links that were already sent out still open.
    const fromQuery = this.route.snapshot.queryParamMap.get('token');
    const fromPath = this.route.snapshot.paramMap.get('token');
    const token = fromQuery ?? fromPath ?? '';

    this.token.set(token);
    this.isReactivation.set(this.route.snapshot.queryParamMap.get('mode') === 'reactivate');

    if (!token) {
      this.loading.set(false);
      this.errorMessage.set('This link is incomplete. Open the link from the e-mail again, or ask for a new one.');
      this.showRequestNew.set(true);
      return;
    }

    // Check the token before drawing the form, so a dead link fails now and not after typing.
    this.authApi.getResetPasswordView(token).subscribe({
      next: (view) => {
        this.loading.set(false);
        this.view.set(view);

        if (!view.isTokenValid) {
          this.errorMessage.set(
            view.message ?? 'That link has expired or has already been used. Ask for a new one.');
          this.showRequestNew.set(true);
          return;
        }

        if (view.tokenExpiresAtUtc) {
          this.startCountdown(view.tokenExpiresAtUtc);
        }
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.errorMessage.set(error.message);
        this.showRequestNew.set(true);
      },
    });
  }

  ngOnDestroy(): void {
    this.stopCountdown();
  }

  // =========================================================================================
  // Actions
  // =========================================================================================

  onPasswordInput(value: string): void {
    this.password = value;
    this.passwordValue.set(value);
    this.errorMessage.set('');
  }

  onConfirmInput(value: string): void {
    this.confirmPassword = value;
    this.confirmValue.set(value);
    this.errorMessage.set('');
  }

  togglePassword(): void {
    this.showPassword.update((shown) => !shown);
  }

  toggleConfirm(): void {
    this.showConfirm.update((shown) => !shown);
  }

  submit(): void {
    if (!this.canSubmit()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.authApi
      .resetPassword({
        token: this.token(),
        password: this.password,
        confirmPassword: this.confirmPassword,
      })
      .subscribe({
        next: (outcome) => {
          this.submitting.set(false);
          this.completed.set(true);
          this.successMessage.set(outcome.message ?? 'Your password has been changed.');
          this.stopCountdown();

          // Every session was revoked server-side, including any this browser was holding, so
          // the local copy is cleared too rather than left to fail on the next call.
          this.tokens.clear();

          this.toast.show(
            'Password updated',
            outcome.message ?? 'Your password has been changed.',
            'success');
        },
        error: (error: Error) => {
          this.submitting.set(false);
          this.errorMessage.set(error.message);

          // A dead token cannot be recovered by trying again, so offer the way forward instead.
          this.showRequestNew.set(true);
        },
      });
  }

  requestNewLink(): void {
    const value = this.emailOrUsername.trim();

    if (!value) {
      this.errorMessage.set('Enter the e-mail address or username on the account.');
      return;
    }

    this.requestingNew.set(true);

    this.authApi.requestNewRecoveryLink(value).subscribe({
      next: (outcome) => {
        this.requestingNew.set(false);

        const message = outcome.message
          ?? 'If that account exists, we have sent a password reset link to its e-mail address.';

        this.successMessage.set(message);
        this.errorMessage.set('');
        this.toast.show('Check your inbox', message, 'success');
      },
      error: (error: Error) => {
        this.requestingNew.set(false);
        this.errorMessage.set(error.message);
      },
    });
  }

  goToSignIn(): void {
    void this.router.navigate(['/auth/sign-in']);
  }

  // =========================================================================================
  // Internals
  // =========================================================================================

  private startCountdown(expiresAtUtc: string): void {
    this.stopCountdown();

    const tick = () => {
      const remaining = Math.floor((Date.parse(expiresAtUtc) - Date.now()) / 1000);
      this.secondsRemaining.set(Math.max(0, remaining));

      if (remaining <= 0) {
        this.stopCountdown();
        const current = this.view();
        if (current) {
          this.view.set({ ...current, isTokenValid: false });
        }
        this.errorMessage.set('This link has expired. Ask for a new one below.');
        this.showRequestNew.set(true);
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
