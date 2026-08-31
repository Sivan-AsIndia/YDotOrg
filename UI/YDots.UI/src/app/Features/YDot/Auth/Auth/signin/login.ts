import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthApiService } from '../../../../../Service/auth-api.service';
import { ToastService } from '../../../../../Shared/services/toast.service';
import { AuthSessionService } from '../../../../../Shared/services/auth-session.service';
import { AuthTokenService } from '../../../../../Shared/services/auth-token.service';
import { DeviceIdentityService } from '../../../../../Shared/services/device-identity.service';
import { MfaHandoffService } from '../../../../../Shared/services/mfa-handoff.service';
import { MfaChallengeResponse, SignInResponse, nextRouteFor } from '../../../../../Shared/models/auth.model';

/**
 * IAM-AUTH-01 — Sign in.
 *
 * WHAT HAPPENS WHEN YOU PRESS SIGN IN
 * -----------------------------------
 * One call goes to `POST /users/sign-in`. The answer carries a `status`, and this component
 * simply obeys it:
 *
 *   succeeded               → the token is stored, go to the dashboard
 *   mfaRequired             → no access token yet; hand the challenge to /auth/mfa-challenge
 *   tenantSelectionRequired → a root user with no Organisation of their own; go and pick one
 *   passwordChangeRequired  → signed in, but pushed to change the password first
 *
 * The component decides nothing about *why*. All of that lives on the server, which is what stops
 * the two projects disagreeing about what a status means.
 *
 * WHY THERE IS NO ORGANISATION FIELD ON THIS FORM
 * -----------------------------------------------
 * The Organisation comes from the host the browser is on — ten1.ngoplanet.com is TEN001 — and is
 * resolved by the server. A box on the form would mean anybody could aim their sign-in at any
 * Organisation by typing a different value, which is exactly the boundary that must never be
 * client-controlled. The one exception is a root user, who belongs to no Organisation and is sent
 * to the picker by `tenantSelectionRequired` above, having already proved who they are.
 *
 * WRONG PASSWORD AND UNKNOWN USER LOOK IDENTICAL
 * ----------------------------------------------
 * Both come back as a 401 with the same wording, and this screen shows it unchanged. Saying "no
 * such user" would turn the form into a way of discovering who has an account here.
 */
@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, RouterModule],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class LoginComponent implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly authApi = inject(AuthApiService);
  private readonly session = inject(AuthSessionService);
  private readonly tokens = inject(AuthTokenService);
  private readonly device = inject(DeviceIdentityService);
  private readonly handoff = inject(MfaHandoffService);

  email = '';
  password = '';
  showPassword = false;
  loading = false;
  rememberDevice = false;
  isOnline = navigator.onLine;

  /** Shown above the form. Comes straight from the API so the wording stays consistent. */
  readonly errorMessage = signal('');

  /** Where to go after a successful sign-in, if the guard sent us here from somewhere. */
  private returnUrl: string | null = null;

  ngOnInit(): void {
    window.addEventListener('online', this.updateOnlineStatus);
    window.addEventListener('offline', this.updateOnlineStatus);

    this.returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');

    // Arriving here always means "start again", so any half-finished MFA transaction is dropped.
    this.handoff.clear();
  }

  ngOnDestroy(): void {
    window.removeEventListener('online', this.updateOnlineStatus);
    window.removeEventListener('offline', this.updateOnlineStatus);
  }

  togglePasswordVisibility(): void {
    this.showPassword = !this.showPassword;
  }

  onSignIn(): void {
    this.errorMessage.set('');

    if (!this.isOnline) {
      this.errorMessage.set('No internet connection detected. Reconnect and try again.');
      return;
    }

    if (!this.email.trim() || !this.password) {
      this.errorMessage.set('Enter your username or e-mail address, and your password.');
      return;
    }

    this.loading = true;

    this.authApi
      .signIn({
        identifier: this.email.trim(),
        password: this.password,
        clientType: 'web',
        // A stable, anonymous browser id. Only its hash reaches the database, and it is what
        // makes "remember this device" possible without storing anything identifying.
        deviceIdentifier: this.device.getDeviceIdentifier(),
        deviceName: this.device.getDeviceName(),
        rememberMe: this.rememberDevice,
      })
      .subscribe({
        next: (response) => {
          this.loading = false;
          this.password = '';
          this.route4Way(response);
        },
        error: (error: Error) => {
          this.loading = false;
          this.password = '';
          this.errorMessage.set(error.message);
          this.toast.show('Sign-in failed', error.message, 'error');
        },
      });
  }

  /** Sends the person to whichever screen the status calls for. */
  private route4Way(response: SignInResponse): void {
    const destination = nextRouteFor(response);

    switch (response.status) {
      case 'mfaRequired': {
        // No access token exists yet — only a short-lived challenge token proving the password
        // step succeeded. It is held in memory (with a sessionStorage fallback for a page
        // refresh) rather than pushed through the URL, where it would end up in browser history,
        // in the Referer header of the next request, and in server access logs. The challenge
        // token is a credential and deserves the same care the password just had.
        if (!response.challengeToken) {
          this.errorMessage.set('The second-factor step could not be started. Try signing in again.');
          return;
        }

        const challenge: MfaChallengeResponse = {
          challengeToken: response.challengeToken,
          methodType: response.mfaMethodType,
          maskedDestination: response.mfaMaskedDestination ?? null,
        };

        // The returnUrl rides in the handoff, not in the URL — see the note on MfaHandoff.
        // Dropping it here is what used to send every second-factor sign-in to the dashboard
        // regardless of the page they had actually asked for.
        this.handoff.store(challenge, this.rememberDevice, this.returnUrl);
        void this.router.navigate([destination]);
        return;
      }

      case 'tenantSelectionRequired': {
        // A root user, who has no Organisation of their own. They are properly signed in - the
        // token in this response is real - and they go straight to the Organisation directory,
        // which is the screen their job actually starts on. Organisation-scoped screens ask
        // which Organisation at the point they need to know, not before.
        this.session.startSession(response);

        // FORWARDED, because select-organisation already reads a returnUrl and acts on it — it
        // simply never received one, so the branch was unreachable and a root user who followed
        // a deep link lost it at this hop. A query parameter is right here where it was wrong for
        // MFA: this one carries a route, not a credential.
        void this.router.navigate([destination], {
          queryParams: this.returnUrl ? { returnUrl: this.returnUrl } : undefined,
        });
        return;
      }

      case 'passwordChangeRequired': {
        this.session.startSession(response);

        this.toast.show(
          'Change your password',
          response.message ?? 'Your password must be changed before you continue.',
          'warning',
        );

        // The returnUrl is deliberately NOT honoured here: the person is signed in but must
        // deal with the password first, and dropping them on the page they asked for would
        // simply bounce them straight back.
        void this.router.navigate([destination], {
          queryParams: { token: response.passwordResetToken ?? undefined },
        });
        return;
      }

      case 'succeeded':
      default: {
        this.session.startSession(response);

        this.toast.show(
          'Signed in',
          `Welcome back, ${this.tokens.displayName() || 'there'}.`,
          'success',
        );

        void this.router.navigateByUrl(this.returnUrl ?? destination);
      }
    }
  }

  private readonly updateOnlineStatus = (): void => {
    this.isOnline = navigator.onLine;
  };
}
