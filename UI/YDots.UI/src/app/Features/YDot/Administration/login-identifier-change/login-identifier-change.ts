import {
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import {
  FormBuilder,
  FormsModule,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute } from '@angular/router';
import { ToastService } from '../../../../Shared/services/toast.service';
import { UserDirectoryApiService } from '../../../../Service/user-directory-api.service';
import { SecurityApiService } from '../../../../Service/security-api.service';
import {
  LoginIdentifierChangeApiService,
} from '../../../../Service/login-identifier-change-api.service';
import {
  LoginIdentifierChangeResponse,
  UserDetailResponse,
} from '../../../../Shared/models/iam-contract.model';

@Component({
  selector: 'app-login-identifier-change',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
  ],
  templateUrl: './login-identifier-change.html',
  styleUrl: './login-identifier-change.css',
})
export class LoginIdentifierChangeComponent {

  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly directory = inject(UserDirectoryApiService);
  private readonly security = inject(SecurityApiService);
  private readonly api = inject(LoginIdentifierChangeApiService);

  userReference = signal('');
  userId = signal('');
  passedUser = signal<UserDetailResponse | null>(null);
  currentLoginEmail = signal('');
  currentUsername = signal('');

  // ----------------------------------------------------
  // Data
  // ----------------------------------------------------

  /**
   * The person, and every identifier change ever raised against them.
   *
   * THE HISTORY IS THE POINT, not decoration. A change that was rejected last month, and why,
   * is exactly the context somebody needs before approving the same change today.
   */
  readonly person = signal<UserDetailResponse | null>(null);
  readonly requests = signal<LoginIdentifierChangeResponse[]>([]);

  /** The one still in flight, if there is one. Only ever one at a time per person. */
  readonly openRequest = computed(() =>
    this.requests().find((request) =>
      request.status !== 'applied'
      && request.status !== 'rejected'
      && request.status !== 'cancelled'
      && request.status !== 'expired') ?? null);

  readonly data = computed(() => {
    const model = this.person();

    if (!model) {
      return null;
    }

    return {
      user: {
        reference: model.code ?? '',
        displayName: model.displayName ?? '',
        currentLoginEmail: model.email ?? '',
        currentUsername: model.username ?? '',
      },
    };
  });

  /** The code somebody has been e-mailed, typed back in to prove they asked for this. */
  readonly verificationCode = signal('');

  // ----------------------------------------------------
  // UI Signals
  // ----------------------------------------------------

  loading = signal(true);
  loadError = signal(false);
  submitting = signal(false);
  readonly checking = signal(false);
  validationErrors = signal<string[]>([]);

  // Validation states
  emailValidated = signal(false);
  emailAvailable = signal<'idle' | 'available' | 'unavailable'>('idle');
  usernameValidated = signal(false);
  usernameAvailable = signal<'idle' | 'available' | 'unavailable'>('idle');
  duplicateResultText = signal('No Duplicates Found');
  reservedNameResultText = signal('No Reserved Names');

  // Verification states
  verificationState = signal<'Pending' | 'Sent' | 'Verified' | 'Failed'>('Pending');
  notificationState = signal<'Not Notified' | 'Sent' | 'Delivered'>('Not Notified');
  approver = signal<string>('');
  effectiveTime = signal<string>('');
  sessionRevocation = signal<'Required' | 'Completed'>('Required');

  // Modal
  showConfirmModal = signal(false);
  changeReference = signal('');

  // ----------------------------------------------------
  // Reactive Form
  // ----------------------------------------------------

  identifierForm = this.fb.group({
    newEmail: [
      '',
      [
        Validators.email,
        Validators.maxLength(254),
      ],
    ],
    newUsername: [
      '',
      [
        Validators.minLength(3),
        Validators.maxLength(80),
        Validators.pattern(/^[a-zA-Z0-9_.-]+$/),
      ],
    ],
    changeReason: [
      '',
      [
        Validators.required,
        Validators.minLength(10),
        Validators.maxLength(1000),
      ],
    ],
  });

  // ----------------------------------------------------
  // Constructor
  // ----------------------------------------------------

  constructor() {
    this.userReference.set(this.route.snapshot.params['userReference'] ?? '');

    // The record is read from the server rather than taken from navigation state. A row handed
    // over by the previous screen is a snapshot of what that screen last saw, and the current
    // e-mail address is the one thing on this page that must not be out of date — it is where
    // the verification code goes.
    this.loadData();

    effect(() => {
      this.identifierForm.updateValueAndValidity({ emitEvent: false });
      const errors: string[] = [];
      const emailErrors = this.identifierForm.controls.newEmail.errors;
      const usernameErrors = this.identifierForm.controls.newUsername.errors;
      const reasonErrors = this.identifierForm.controls.changeReason.errors;

      if (emailErrors?.['email']) errors.push('New login email must be a valid email address.');
      if (emailErrors?.['maxlength']) errors.push('New login email cannot exceed 254 characters.');
      if (usernameErrors?.['minlength']) errors.push('New username must be at least 3 characters.');
      if (usernameErrors?.['maxlength']) errors.push('New username cannot exceed 80 characters.');
      if (usernameErrors?.['pattern']) errors.push('Username can only contain letters, numbers, dots, hyphens and underscores.');
      if (reasonErrors?.['required']) errors.push('Change reason is required.');
      if (reasonErrors?.['minlength']) errors.push('Change reason must be at least 10 characters.');
      if (reasonErrors?.['maxlength']) errors.push('Change reason cannot exceed 1000 characters.');

      this.validationErrors.set(errors);
    });
  }

  // ----------------------------------------------------
  // Load Data
  // ----------------------------------------------------

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

    this.directory.getUserByReference(reference).subscribe({
      next: (model) => {
        this.person.set(model);
        this.userId.set(model.id ?? '');
        this.currentLoginEmail.set(model.email ?? '');
        this.currentUsername.set(model.username ?? '');
        this.loadRequests();
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.loadError.set(true);
        this.toast.show('Could not find that person', error.message, 'error');
      },
    });
  }

  retry(): void {
    this.loadData();
  }

  // ----------------------------------------------------
  // Validate Identifier
  // ----------------------------------------------------

  /**
   * Is the new identifier free?
   *
   * ASKED OF THE SERVER, because only the server knows. The old version decided availability by
   * looking for the substring "taken" in what was typed, which meant every real address came
   * back available and the first genuine collision surfaced as a failed submit.
   *
   * The current user is excluded from the check: somebody re-typing their own address should be
   * told it is theirs already, not that it is taken.
   */
  validateNewIdentifier(type: 'email' | 'username'): void {
    const control = type === 'email'
      ? this.identifierForm.controls.newEmail
      : this.identifierForm.controls.newUsername;

    const value = (control.value ?? '').trim();

    if (!value || control.invalid) {
      this.toast.show('Check the field',
        type === 'email'
          ? 'Enter a valid e-mail address first.'
          : 'Enter a valid username first (3-80 letters, numbers, dots, hyphens or underscores).',
        'warning');
      return;
    }

    if (type === 'email' && value.toLowerCase() === this.currentLoginEmail().toLowerCase()) {
      this.emailAvailable.set('unavailable');
      this.emailValidated.set(false);
      this.duplicateResultText.set('That is already the address on this account.');
      return;
    }

    if (type === 'username' && value.toLowerCase() === this.currentUsername().toLowerCase()) {
      this.usernameAvailable.set('unavailable');
      this.usernameValidated.set(false);
      this.reservedNameResultText.set('That is already the username on this account.');
      return;
    }

    this.checking.set(true);

    this.security
      .checkIdentity(
        type === 'email' ? value : undefined,
        type === 'username' ? value : undefined,
        this.userId())
      .subscribe({
        next: (result) => {
          this.checking.set(false);

          const available = type === 'email'
            ? result.emailAvailable !== false
            : result.usernameAvailable !== false;

          const message = result.message
            ?? (available ? 'That is available.' : 'That is already in use.');

          if (type === 'email') {
            this.emailAvailable.set(available ? 'available' : 'unavailable');
            this.emailValidated.set(available);
            this.duplicateResultText.set(message);
          } else {
            this.usernameAvailable.set(available ? 'available' : 'unavailable');
            this.usernameValidated.set(available);

            // The server offers alternatives when a username is taken. Showing them saves a
            // round of guessing.
            const suggestions = result.suggestions ?? [];
            this.reservedNameResultText.set(
              available || suggestions.length === 0
                ? message
                : `${message} Try: ${suggestions.join(', ')}`);
          }

          this.toast.show(available ? 'Available' : 'Already in use', message,
            available ? 'success' : 'warning');
        },
        error: (error: Error) => {
          this.checking.set(false);
          this.toast.show('Could not check that', error.message, 'error');
        },
      });
  }

  // =========================================================================================
  // Raising the request
  // =========================================================================================

  submitIdentifierChange(): void {
    this.identifierForm.markAllAsTouched();

    if (this.identifierForm.invalid) {
      this.toast.show('Check the form', 'Fix the errors before submitting.', 'warning');
      return;
    }

    const email = (this.identifierForm.controls.newEmail.value ?? '').trim();
    const username = (this.identifierForm.controls.newUsername.value ?? '').trim();

    if (!email && !username) {
      this.toast.show('Nothing to change',
        'Enter a new e-mail address or a new username.', 'warning');
      return;
    }

    // ONE AT A TIME. An e-mail change and a username change are verified differently and
    // approved separately, so bundling them would let a single approval cover two decisions.
    if (email && username) {
      this.toast.show('One at a time',
        'Change the e-mail address or the username, not both in one request.', 'warning');
      return;
    }

    if (email && !this.emailValidated()) {
      this.toast.show('Check it first', 'Check the new address is available first.', 'warning');
      return;
    }

    if (username && !this.usernameValidated()) {
      this.toast.show('Check it first', 'Check the new username is available first.', 'warning');
      return;
    }

    this.showConfirmModal.set(true);
  }

  confirmAndSubmit(): void {
    this.showConfirmModal.set(false);

    const email = (this.identifierForm.controls.newEmail.value ?? '').trim();
    const username = (this.identifierForm.controls.newUsername.value ?? '').trim();
    const reason = (this.identifierForm.controls.changeReason.value ?? '').trim();
    const isEmailChange = email.length > 0;

    this.submitting.set(true);

    this.api
      .request(this.userId(), isEmailChange, isEmailChange ? email : username, reason)
      .subscribe({
        next: (outcome) => {
          this.submitting.set(false);
          this.toast.show('Request raised',
            outcome.message ?? 'The change has been raised and is waiting to be verified.',
            'success');
          this.identifierForm.reset();
          this.resetStates();
          this.loadRequests();
        },
        error: (error: Error) => {
          this.submitting.set(false);
          this.toast.show('Could not raise the request', error.message, 'error');
        },
      });
  }

  // =========================================================================================
  // Moving it along
  // =========================================================================================

  /**
   * Proves the current owner asked for this.
   *
   * The code goes to the identifier ALREADY ON FILE, never the new one. A code sent to the new
   * address would only prove that whoever typed it can read it — which is exactly what somebody
   * changing an address to their own can also do.
   */
  sendVerification(): void {
    const request = this.openRequest();

    if (!request?.id) {
      this.toast.show('Nothing to verify', 'Raise a change first.', 'info');
      return;
    }

    const code = this.verificationCode().trim();

    if (!code) {
      this.toast.show('Enter the code',
        'Type the code sent to the address already on the account.', 'warning');
      return;
    }

    this.submitting.set(true);

    this.api.verify(request.id, code).subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        this.verificationCode.set('');
        this.toast.show('Verified', outcome.message ?? 'The request has been verified.',
          'success');
        this.loadRequests();
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.toast.show('That code did not work', error.message, 'error');
      },
    });
  }

  /** A second person agreeing, or turning it down with a reason. */
  decide(approved: boolean): void {
    const request = this.openRequest();

    if (!request?.id) {
      return;
    }

    const reason = (this.identifierForm.controls.changeReason.value ?? '').trim()
      || (approved ? 'Approved.' : 'Rejected.');

    this.submitting.set(true);

    this.api.decide(request.id, approved, reason).subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        this.toast.show(approved ? 'Approved' : 'Rejected',
          outcome.message ?? 'The decision has been recorded.', approved ? 'success' : 'info');
        this.loadRequests();
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.toast.show('Could not record that', error.message, 'error');
      },
    });
  }

  /**
   * The moment the account actually changes.
   *
   * Every session ends here, on the server. The old identifier stops working, and a live
   * session still holding it would be an authenticated connection nobody could account for.
   */
  applyChange(): void {
    const request = this.openRequest();

    if (!request?.id) {
      return;
    }

    this.submitting.set(true);

    this.api.apply(request.id).subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        this.toast.show('Applied',
          outcome.message ?? 'The account has been updated and every session ended.', 'success');
        this.loadData();
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.toast.show('Could not apply the change', error.message, 'error');
      },
    });
  }

  cancelRequest(): void {
    const request = this.openRequest();

    if (!request?.id) {
      this.toast.show('Nothing to cancel', 'There is no request in flight.', 'info');
      return;
    }

    const reason = (this.identifierForm.controls.changeReason.value ?? '').trim()
      || 'Cancelled before it was applied.';

    this.submitting.set(true);

    this.api.cancel(request.id, reason).subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        this.toast.show('Cancelled', outcome.message ?? 'The request has been cancelled.',
          'info');
        this.identifierForm.reset();
        this.resetStates();
        this.loadRequests();
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.toast.show('Could not cancel it', error.message, 'error');
      },
    });
  }

  /** Re-reads the request list, which is what drives every state shown on the page. */
  private loadRequests(): void {
    this.api.getForUser(this.userId()).subscribe({
      next: (requests) => {
        this.requests.set(requests);
        this.applyRequestState();
        this.loading.set(false);
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.toast.show('Could not load the change history', error.message, 'warning');
      },
    });
  }

  /**
   * Reflects the open request into the state tiles.
   *
   * READ FROM THE SERVER'S STATUS rather than set optimistically as buttons are pressed. The
   * old version moved every tile to "Verified / Delivered / Completed" the moment submit was
   * clicked, so the page claimed the change had gone through before anything had happened.
   */
  private applyRequestState(): void {
    const request = this.openRequest()
      ?? this.requests()[0]
      ?? null;

    if (!request) {
      this.resetStates();
      return;
    }

    this.changeReference.set(request.id ?? '');

    this.verificationState.set(
      request.verifiedAtUtc ? 'Verified'
        : request.status === 'pendingVerification' ? 'Sent'
          : request.status === 'rejected' ? 'Failed' : 'Pending');

    this.notificationState.set(
      request.previousOwnerNotifiedAtUtc ? 'Delivered'
        : request.status === 'draft' ? 'Not Notified' : 'Sent');

    this.approver.set(
      request.approvedByName
        ?? (request.requiresApproval
          ? request.status === 'pendingApproval' ? 'Waiting for approval' : ''
          : 'No approval required'));

    this.effectiveTime.set(
      request.appliedAtUtc ? this.formatDateTime(request.appliedAtUtc) : '');

    // Sessions are ended by the server when the change is applied, not before.
    this.sessionRevocation.set(request.appliedAtUtc ? 'Completed' : 'Required');
  }

  private formatDateTime(value: string): string {
    try {
      return new Date(value).toLocaleString('en-IN',
        { dateStyle: 'medium', timeStyle: 'short' });
    } catch {
      return value;
    }
  }

  private resetStates(): void {
    this.emailValidated.set(false);
    this.emailAvailable.set('idle');
    this.usernameValidated.set(false);
    this.usernameAvailable.set('idle');
    this.verificationState.set('Pending');
    this.notificationState.set('Not Notified');
    this.approver.set('');
    this.effectiveTime.set('');
    this.sessionRevocation.set('Required');
    this.changeReference.set('');
    this.duplicateResultText.set('Not checked yet');
    this.reservedNameResultText.set('Not checked yet');
  }

  closeModal(): void {
    this.showConfirmModal.set(false);
  }

  goBack(): void {
    if (this.userReference()) {
      this.router.navigate(['/app/administration/access/user-profile-and-access', this.userReference()]);
    } else {
      this.router.navigate(['/app/administration/access/user-directory']);
    }
  }
}