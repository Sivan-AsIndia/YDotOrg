import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthApiService } from '../../../../../Service/auth-api.service';
import { ToastService } from '../../../../../Shared/services/toast.service';
import { AuthSessionService } from '../../../../../Shared/services/auth-session.service';
import { AuthTokenService } from '../../../../../Shared/services/auth-token.service';
import { ReauthenticationViewResponse } from '../../../../../Shared/models/auth.model';

/**
 * IAM-AUTH-07 — Confirm it is still you.
 *
 * WHEN THIS SCREEN APPEARS
 * ------------------------
 * Two moments, both about the human rather than the token:
 *
 *   • **Idle timeout.** Nobody has touched the keyboard for longer than the configured window.
 *     The tokens may still be perfectly valid — the question is whether the same person is still
 *     at the screen. An unattended laptop in a shared office is exactly this risk.
 *
 *   • **A protected action.** Something high-consequence is about to happen and the app wants a
 *     fresh confirmation before it does, even in an active session.
 *
 * WHY IT IS NOT A SIGN-IN
 * -----------------------
 * The session is not over. The refresh cookie is still valid and the identity is still known, so
 * the screen asks only for the password (plus a code where a second factor is enrolled) and
 * mints a fresh token pair. Signing out and back in would lose whatever the person was doing,
 * which is why "Save my work as a draft" sits on this screen too.
 *
 * The password is checked by the API — there is no client-side comparison anywhere here. The
 * previous version fetched a mock profile and compared strings in the browser, which meant the
 * expected password was sitting in the page and the "second factor" was the constant 123456.
 */
@Component({
  selector: 'app-reauthenticate',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './reauthenticate.html',
  styleUrl: './reauthenticate.css',
})
export class ReauthenticateComponent implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly authApi = inject(AuthApiService);
  private readonly session = inject(AuthSessionService);
  private readonly tokens = inject(AuthTokenService);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal('');
  readonly view = signal<ReauthenticationViewResponse | null>(null);

  /** Signals, so canSubmit re-evaluates as they are typed. Plain fields left it frozen. */
  readonly password = signal('');
  readonly verificationCode = signal('');
  readonly showPassword = signal(false);

  /** Saving unsaved work before the session ends. */
  readonly savingDraft = signal(false);
  readonly draftSaved = signal(false);

  private returnUrl: string | null = null;

  // =========================================================================================
  // Derived
  // =========================================================================================

  readonly displayName = computed(() => this.tokens.displayName());
  readonly email = computed(() => this.tokens.user()?.email ?? '');

  /** Only true when the account actually has a second factor enrolled. */
  readonly needsCode = computed(() => this.view()?.verificationCodeRequired ?? false);

  readonly canSubmit = computed(() => {
    if (this.submitting() || !this.password().trim()) {
      return false;
    }

    return !this.needsCode() || this.verificationCode().trim().length >= 6;
  });

  readonly summary = computed(
    () => this.view()?.protectedActionSummary ?? 'Your session has been idle. Confirm it is still you to carry on.',
  );

  // =========================================================================================
  // Lifecycle
  // =========================================================================================

  ngOnInit(): void {
    this.returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');

    // Nothing to reauthenticate without an identity: that is a full sign-in, not a top-up.
    if (!this.tokens.user()) {
      void this.router.navigate(['/auth/sign-in']);
      return;
    }

    // Somebody who arrived here by accident, with a healthy session, is sent back to work.
    if (!this.session.isReauthRequired() && !this.session.isIdleTimedOut()) {
      void this.router.navigate([this.returnUrl ?? '/app/dashboard']);
      return;
    }

    this.authApi
      .getReauthenticationView(
        this.route.snapshot.queryParamMap.get('action') ?? undefined,
        this.route.snapshot.queryParamMap.get('draftToken') ?? undefined,
      )
      .subscribe({
      next: (view: ReauthenticationViewResponse) => {
        this.loading.set(false);
        this.view.set(view);

        // Held so the confirm call can hand it back and get the parked payload returned with
        // the step-up token, rather than the person retyping what the timeout interrupted.
        this.draftToken.set(view.draftToken ?? null);
      },
      error: (error: Error) => {
        this.loading.set(false);
        // A 401 here means the refresh cookie is gone too, so there is genuinely nothing left to
        // top up. The interceptor has already redirected; this only covers other failures.
        this.errorMessage.set(error.message);
      },
    });
  }

  /** The handle to parked work, from the view call or from saving a draft here. */
  readonly draftToken = signal<string | null>(null);

  /** Whatever was parked, handed back on a successful step-up. */
  readonly restoredDraft = signal<string | null>(null);

  ngOnDestroy(): void {
    this.password.set('');
    this.verificationCode.set('');
  }

  // =========================================================================================
  // Actions
  // =========================================================================================

  togglePassword(): void {
    this.showPassword.update((shown) => !shown);
  }

  confirm(): void {
    if (!this.canSubmit()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.authApi
      .reauthenticate({
        password: this.password(),
        mfaCode: this.needsCode() ? this.verificationCode().trim() : null,
        draftToken: this.draftToken(),
      })
      .subscribe({
        next: (result) => {
          this.submitting.set(false);
          this.password.set('');
          this.verificationCode.set('');

          // WHAT COMES BACK IS A STEP-UP TOKEN, NOT A NEW ACCESS TOKEN. The session was never
          // lost - only its recency - so there is nothing to re-store. The step-up token is what
          // the protected action will present, and the server tracks its short validity itself.
          this.restoredDraft.set(result.draftPayload ?? null);
          this.session.resumeSession();

          void this.router.navigateByUrl(this.returnUrl ?? '/app/dashboard');
        },
        error: (error: Error) => {
          this.submitting.set(false);
          this.password.set('');
          this.errorMessage.set(error.message);
        },
      });
  }

  /**
   * Stores whatever is in progress on the server before the session ends, so a timeout at a bad
   * moment costs nothing. What gets captured is deliberately small — the route the person was on.
   */
  saveDraft(): void {
    this.savingDraft.set(true);

    this.authApi
      .saveProtectedDraft({
        actionCode: 'session.resume',
        payload: JSON.stringify({
          returnUrl: this.returnUrl,
          savedAt: new Date().toISOString(),
        }),
      })
      .subscribe({
        next: (saved) => {
          this.savingDraft.set(false);
          this.draftSaved.set(true);

          // Kept so confirming hands it straight back: the point of parking the work is that it
          // returns by itself, not that the person has to go and find it again.
          this.draftToken.set(saved.draftToken ?? null);
          this.toast.show('Draft saved', 'Your work is safe. Open it again after you sign in.', 'success');
        },
        error: (error: Error) => {
          this.savingDraft.set(false);
          this.errorMessage.set(error.message);
        },
      });
  }

  signOut(): void {
    this.session.endSession();
  }
}
