import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { AuthApiService } from '../../../../../Service/auth-api.service';
import { ToastService } from '../../../../../Shared/services/toast.service';

/**
 * IAM-AUTH-03 — Forgot password.
 *
 * THE E-MAIL IS SENT BY THE SERVER, NOT BY THIS SCREEN
 * ----------------------------------------------------
 * The previous version called the API and then *also* called Brevo directly from the browser,
 * using an API key compiled into the bundle. Two problems: the key was readable by every visitor,
 * and the "reset link" it built used `response.data.reference` — the literal string
 * "RECOVERY-REQUEST" — so the link never worked. There is now exactly one call. The server
 * creates the token, stores only its hash, and sends the message using credentials the browser
 * never sees.
 *
 * WHY THE ANSWER NEVER CHANGES
 * ----------------------------
 * Whether the address exists, does not exist, or belongs to a closed account, the API returns the
 * same confirmation, and this screen shows it unchanged. Saying "no such account" would turn the
 * form into a free tool for checking which e-mail addresses are registered here.
 */
@Component({
  selector: 'app-forgotpassword',
  standalone: true,
  imports: [FormsModule, CommonModule, RouterModule],
  templateUrl: './forgotpassword.html',
  styleUrl: './forgotpassword.css',
})
export class ForgotpasswordComponent {
  private readonly toast = inject(ToastService);
  private readonly authApi = inject(AuthApiService);

  emailOrUsername = '';

  readonly isLoading = signal(false);
  readonly submitted = signal(false);
  readonly errorMessage = signal('');
  readonly confirmation = signal('');

  onSubmit(): void {
    this.errorMessage.set('');

    const value = this.emailOrUsername.trim();

    if (!value) {
      this.errorMessage.set('Enter the e-mail address or username on the account.');
      return;
    }

    if (value.length > 254) {
      this.errorMessage.set('That is longer than an e-mail address can be. Check it and try again.');
      return;
    }

    this.isLoading.set(true);

    this.authApi.forgotPassword({ identifier: value }).subscribe({
      next: (outcome) => {
        this.isLoading.set(false);
        this.submitted.set(true);

        // The wording comes from the server so both sides say the same thing, and so the
        // deliberately non-disclosing phrasing cannot drift out of step here. It says the same
        // whether or not the address is known - anything else would be a free way to find out
        // who has an account.
        const message = outcome.message
          ?? 'If that account exists, we have sent a password reset link to its e-mail address.';

        this.confirmation.set(message);
        this.toast.show('Check your inbox', message, 'success');
      },
      error: (error: Error) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.message);
        this.toast.show('Could not send', error.message, 'error');
      },
    });
  }

  /** Lets somebody who mistyped the address try again without reloading the page. */
  tryAgain(): void {
    this.submitted.set(false);
    this.confirmation.set('');
    this.errorMessage.set('');
  }
}
