import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthApiService } from '../../../../../Service/auth-api.service';
import { ToastService } from '../../../../../Shared/services/toast.service';
import { MfaHandoffService } from '../../../../../Shared/services/mfa-handoff.service';
import { AccountRecoveryGuidanceResponse } from '../../../../../Shared/models/auth.model';

/**
 * IAM-AUTH-06 — Account unavailable, and how to get back in.
 *
 * WHEN THIS SCREEN APPEARS
 * ------------------------
 * Only after the password was **correct**. Sign-in returns `status: "AccountUnavailable"` when
 * the credentials were right but the account is suspended, expired, locked or not yet started.
 * That ordering matters: because the person has already proven they know the password, it is safe
 * to tell them the specific reason. A stranger guessing addresses never reaches this page — they
 * get the same generic failure as an unknown username.
 *
 * WHAT "START ACCOUNT RECOVERY" ACTUALLY DOES
 * -------------------------------------------
 * It calls `POST /account-unavailable-and-recovery-guidance/start-recovery`. For a **suspended**
 * account the server e-mails a reactivation link; opening it lifts the suspension and sets a new
 * password in one step. For anything else it sends the ordinary recovery link. Either way the
 * answer on screen is identical, so the button cannot be used to work out an account's state.
 */
@Component({
  selector: 'app-account-unavailable',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './account-unavailable.html',
  styleUrl: './account-unavailable.css',
})
export class AccountUnavailableComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly authApi = inject(AuthApiService);
  private readonly handoff = inject(MfaHandoffService);

  readonly detail = signal<AccountRecoveryGuidanceResponse | null>(null);
  readonly emailOrUsername = signal('');
  readonly loading = signal(true);

  readonly submitting = signal(false);
  readonly successMessage = signal('');
  readonly errorMessage = signal('');
  readonly recoveryStarted = signal(false);

  // ---- Contact support ---------------------------------------------------------------------
  readonly showSupportForm = signal(false);
  readonly supportSent = signal(false);

  /**
   * Signals, not plain fields — and that distinction is the whole bug this replaced.
   *
   * `canSubmitSupport` below is a computed(), and a computed only re-evaluates when a *signal* it
   * reads changes. When these were plain properties, typing notified nothing: the computed kept
   * its first answer of false for ever and the Send request button could never enable, no matter
   * how much text was in the box.
   */
  readonly supportMessage = signal('');
  readonly contactEmail = signal('');

  readonly copied = signal('');

  // =========================================================================================
  // Derived
  // =========================================================================================

  readonly category = computed(() => this.detail()?.title ?? 'Sign-in cannot continue');

  readonly guidance = computed(
    () => this.detail()?.message ?? 'Your sign-in cannot be completed at the moment.');

  readonly supportReference = computed(() => this.detail()?.reason ?? 'IAM-SUPPORT');
  readonly nextEligibleAt = computed(() => this.detail()?.retryAfterUtc ?? null);

  /** The steps to take, written by the server so the wording stays consistent. */
  readonly steps = computed(() => this.detail()?.steps ?? []);

  /**
   * Recovery is offered only when the SERVER says this account can use it.
   *
   * It is an explicit flag rather than a string match against a list of options, because
   * matching on wording means a copy edit silently turns the button off.
   */
  readonly canStartRecovery = computed(() => this.detail()?.canRequestReset === true);

  /** True when waiting it out is genuinely an option, as it is for a temporary lockout. */
  readonly canWaitItOut = computed(() => this.detail()?.canSelfUnlock === true);

  readonly supportEmail = computed(() => this.detail()?.supportEmail ?? null);
  readonly supportPhone = computed(() => this.detail()?.supportPhone ?? null);

  readonly canSubmitSupport = computed(() => this.supportMessage().trim().length >= 10 && !this.submitting());

  readonly icon = computed(() => {
    switch (this.category()) {
      case 'Suspended': return 'ri-forbid-2-line';
      case 'Temporarily locked': return 'ri-lock-line';
      case 'Access period ended': return 'ri-calendar-close-line';
      case 'Not yet active': return 'ri-time-line';
      case 'Not yet eligible': return 'ri-calendar-schedule-line';
      default: return 'ri-error-warning-line';
    }
  });

  readonly badgeClass = computed(() => {
    switch (this.category()) {
      case 'Suspended': return 'bg-danger-subtle text-danger';
      case 'Temporarily locked': return 'bg-warning-subtle text-warning-emphasis';
      case 'Access period ended':
      case 'No longer available': return 'bg-secondary-subtle text-secondary-emphasis';
      default: return 'bg-info-subtle text-info-emphasis';
    }
  });

  // =========================================================================================
  // Lifecycle
  // =========================================================================================

  ngOnInit(): void {
    const stored = this.handoff.unavailable();

    if (stored?.detail) {
      // Sign-in already told us the exact reason, so there is nothing more to fetch.
      this.detail.set(stored.detail);
      this.emailOrUsername.set(stored.emailOrUsername);
      this.loading.set(false);
      return;
    }

    // Landing here directly — a bookmark, a refresh, a typed URL. Fall back to the generic
    // guidance, which deliberately reveals nothing about any particular account.
    this.authApi.getRecoveryGuidance().subscribe({
      next: (view: AccountRecoveryGuidanceResponse) => {
        this.detail.set(view);
        this.emailOrUsername.set(stored?.emailOrUsername ?? '');
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      },
    });
  }

  // =========================================================================================
  // Actions
  // =========================================================================================

  startRecovery(): void {
    const identifier = this.emailOrUsername().trim();

    if (!identifier) {
      this.errorMessage.set('Enter the e-mail address or username on the account.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.authApi.startRecovery(identifier).subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        this.recoveryStarted.set(true);

        // The same non-disclosing wording as forgot-password: it says nothing about whether the
        // account exists, which is the whole point of routing recovery through one path.
        const message = outcome.message
          ?? 'If that account exists, we have sent recovery instructions to its e-mail address.';

        this.successMessage.set(message);
        this.toast.show('Check your inbox', message, 'success');
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.errorMessage.set(error.message);
      },
    });
  }

  onIdentifierInput(value: string): void {
    this.emailOrUsername.set(value);
    this.errorMessage.set('');
  }

  openSupportForm(): void {
    this.showSupportForm.set(true);
    this.errorMessage.set('');
  }

  submitSupportRequest(): void {
    if (!this.canSubmitSupport()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.authApi
      .contactSupport({
        // Only the safe reference goes to the service desk — never the account status or address.
        supportReference: this.supportReference(),
        message: this.supportMessage().trim(),
        contactEmail: this.contactEmail().trim() || undefined,
      })
      .subscribe({
        next: (outcome) => {
          this.submitting.set(false);
          this.supportSent.set(true);

          const message = outcome.message
            ?? 'Your message has been passed to the service desk.';

          this.successMessage.set(message);
          this.toast.show('Request sent', message, 'success');
        },
        error: (error: Error) => {
          this.submitting.set(false);
          this.errorMessage.set(error.message);
        },
      });
  }

  copyReference(): void {
    void navigator.clipboard.writeText(this.supportReference()).then(() => {
      this.copied.set('reference');
      setTimeout(() => this.copied.set(''), 2000);
    });
  }

  returnToSignIn(): void {
    this.handoff.clear();
    void this.router.navigate(['/auth/sign-in']);
  }
}
