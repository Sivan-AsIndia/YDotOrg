import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router, ActivatedRoute } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ToastService } from '../../../../Shared/services/toast.service';
import { UserDirectoryApiService } from '../../../../Service/user-directory-api.service';
import { SecurityApiService } from '../../../../Service/security-api.service';
import {
  UserAccessPreviewResponse,
  UserDetailResponse,
  UserSecurityResponse,
} from '../../../../Shared/models/iam-contract.model';

/** One role the person holds, as the table lists it. */
interface RoleAssignmentRow {
  role: string;
  assignmentType: string;
  scope: string;
  permissions: number;
  term: string;
}

/**
 * One person's record, read-only.
 *
 * READ FROM THREE ENDPOINTS, because the page asks three different questions: who they are
 * (`/users/{id}`), what they can reach (`/users/{id}/access`), and how well protected the
 * account is (`/users/{id}/security`). Folding those into one payload would mean every visit
 * paid for all three even when only the first was wanted.
 *
 * NOTHING IS CACHED ACROSS SCREENS. An earlier version read a directory cache so a status
 * change "flowed across screens", which meant this page could show a status that had been
 * changed in another tab and rejected by the server. Re-reading is cheap and it is right.
 */
@Component({
  selector: 'app-user-details',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './user-details.html',
  styleUrl: './user-details.css',
})
export class UserDetailsComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly api = inject(UserDirectoryApiService);
  private readonly securityApi = inject(SecurityApiService);

  readonly userReference = signal('');
  readonly userId = signal('');

  readonly detail = signal<UserDetailResponse | null>(null);
  readonly access = signal<UserAccessPreviewResponse | null>(null);
  readonly security = signal<UserSecurityResponse | null>(null);

  readonly loading = signal(true);
  readonly loadError = signal(false);

  readonly mfaEnabled = computed(() => this.security()?.mfaEnabled === true);
  readonly showMfaModal = signal(false);
  readonly mfaAction = signal<'enable' | 'disable'>('enable');
  readonly mfaSubmitting = signal(false);
  readonly mfaReason = signal('');

  readonly showForgotPasswordModal = signal(false);
  readonly forgotPasswordEmail = signal('');
  readonly forgotPasswordSubmitting = signal(false);
  readonly forgotPasswordSent = signal(false);

  constructor() {
    this.userReference.set(this.route.snapshot.params['userReference'] ?? '');
    this.loadData();
  }

  // =========================================================================================
  // What the page renders
  // =========================================================================================

  readonly user = computed(() => {
    const person = this.detail();

    if (!person) {
      return null;
    }

    const sessions = this.security()?.activeSessions ?? [];

    return {
      reference: person.code ?? '',
      displayName: person.displayName ?? '',
      loginEmail: person.email ?? '',
      username: person.username ?? '',
      mobileNumber: person.mobileNumber
        ? `${person.mobileCountryCode ?? ''} ${person.mobileNumber}`.trim()
        : '—',
      employeeId: person.employeeNumber ?? '—',
      accountCategory: person.accountCategory ?? '—',
      organisationUnit: person.organisationUnitName ?? '—',
      department: person.departmentName ?? '—',
      designation: person.designation ?? '—',
      manager: person.managerName ?? '—',

      // There is no work-location column on a person here. The organisation unit is the
      // nearest fact the system actually holds, and saying so beats an empty cell that reads
      // as data that failed to load.
      workLocation: person.organisationUnitName ?? '—',

      preferredLanguage: person.preferredCulture ?? '—',
      timeZone: person.timeZone ?? '—',
      accountStatus: person.statusDisplay ?? person.status ?? '',
      accessStartDate: person.accessStartsAtUtc
        ? this.formatDate(person.accessStartsAtUtc) : '—',
      accessEndDate: person.accessEndsAtUtc
        ? this.formatDate(person.accessEndsAtUtc) : 'No end date',

      // Access review is governed by campaigns, not by a date on the person. The end of their
      // access window is the date this page can honestly show.
      accessReviewDue: person.accessEndsAtUtc
        ? this.formatDate(person.accessEndsAtUtc) : 'Set by review campaign',

      lastSuccessfulSignIn: person.lastLoginAtUtc
        ? this.formatDateTime(person.lastLoginAtUtc) : 'Never',
      activeSessions: sessions.length,
      activeSessionDevices: new Set(
        sessions.map((session) => session.deviceName ?? session.clientType)).size,
      roleAssignments: this.roleAssignments(),
    };
  });

  readonly roleAssignments = computed<RoleAssignmentRow[]>(() => {
    const roles = this.access()?.roles ?? this.detail()?.roles ?? [];
    const scopes = this.access()?.dataScopes ?? this.detail()?.dataScopes ?? [];

    // The scope line is the same for every role: data scopes are granted to the person, not
    // per-role, so repeating it on each row is honest rather than duplicated detail.
    const scopeLabel = scopes.length > 0
      ? scopes.map((scope) => scope.displayLabel ?? scope.scopeValue).filter(Boolean).join(', ')
      : 'Whole organisation';

    return roles.map((role) => ({
      role: role.roleName ?? role.roleCode ?? '',
      assignmentType: role.isPrimary ? 'Primary' : 'Additional',
      scope: scopeLabel,
      permissions: role.permissionCount ?? 0,
      term: role.effectiveToUtc
        ? `Until ${this.formatDate(role.effectiveToUtc)}`
        : 'No end date',
    }));
  });

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

    this.api.getUserByReference(reference).subscribe({
      next: (person) => {
        this.detail.set(person);
        this.userId.set(person.id ?? '');
        this.loadRelated();
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.loadError.set(true);
        this.toast.show('Could not find that person', error.message, 'error');
      },
    });
  }

  /**
   * The access and security views.
   *
   * Requested together because the page shows both at once, and separately from the record
   * because either can be refused on its own: a caller may read somebody's profile without
   * holding the permission to see what that person can reach.
   */
  private loadRelated(): void {
    const id = this.userId();

    this.api.getUserAccess(id).subscribe({
      next: (access) => this.access.set(access),
      error: () => this.access.set(null),
    });

    this.securityApi.getUserSecurity(id).subscribe({
      next: (model) => {
        this.security.set(model);
        this.loading.set(false);
      },
      error: () => {
        // Not fatal. The identity half of the page is already loaded and worth showing; the
        // MFA tile falls back to what the record itself says.
        this.security.set(null);
        this.loading.set(false);
      },
    });
  }

  private refresh(): void {
    const id = this.userId();

    forkJoin({
      person: this.api.getUser(id),
      model: this.securityApi.getUserSecurity(id),
    }).subscribe({
      next: ({ person, model }) => {
        this.detail.set(person);
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
  // Two-step verification
  // =========================================================================================

  getMfaStatusLabel(): string {
    const model = this.security();

    if (!model) {
      return this.detail()?.mfaEnabled ? 'Enrolled' : 'Not enrolled';
    }

    if (model.mfaEnabled) {
      return model.isMfaEffectivelyRequired ? 'Enrolled · required' : 'Enrolled';
    }

    return model.isMfaEffectivelyRequired ? 'Required · not set up' : 'Not enrolled';
  }

  getMfaStatusClass(): string {
    return this.mfaEnabled() ? 'bg-success' : 'bg-danger';
  }

  /**
   * Opens the two-step verification dialog.
   *
   * ENABLE AND DISABLE MEAN DIFFERENT THINGS HERE, and neither is a switch. An administrator
   * cannot enrol a factor on somebody's behalf — enrolling needs the device in their hand — so
   * "enable" REQUIRES it, and the person sets it up at their next sign-in. "Disable" clears
   * the factors that exist, which is the lost-phone case.
   */
  openMfaModal(action: 'enable' | 'disable'): void {
    this.mfaAction.set(action);
    this.mfaReason.set('');
    this.mfaSubmitting.set(false);
    this.showMfaModal.set(true);
  }

  closeMfaModal(): void {
    this.showMfaModal.set(false);
  }

  toggleMfa(): void {
    const person = this.detail();
    const action = this.mfaAction();
    const reason = this.mfaReason().trim();

    if (!person?.id) {
      return;
    }

    if (reason.length < 10) {
      this.toast.show('Check the form', 'Give a reason of at least 10 characters.', 'warning');
      return;
    }

    this.mfaSubmitting.set(true);

    const done = (message: string) => {
      this.mfaSubmitting.set(false);
      this.showMfaModal.set(false);
      this.toast.show('Done', message, 'success');
      this.refresh();
    };

    const failed = (error: Error) => {
      this.mfaSubmitting.set(false);
      this.toast.show('That did not work', error.message, 'error');
    };

    if (action === 'enable') {
      // Requiring it is what an administrator can actually do. The person enrols themselves.
      this.api.updateUser(person.id, {
        expectedVersion: person.version ?? 0,
        mfaRequirement: 'required',
        reason,
      }).subscribe({
        next: () => done(
          `${person.displayName} will be asked to set up two-step verification at their next sign-in.`),
        error: failed,
      });
      return;
    }

    this.securityApi.resetUserMfa(person.id, reason).subscribe({
      next: (outcome) => done(outcome.message ?? 'Two-step verification has been reset.'),
      error: failed,
    });
  }

  enableMfa(): void {
    this.openMfaModal('enable');
  }

  disableMfa(): void {
    this.openMfaModal('disable');
  }

  // =========================================================================================
  // Password reset
  // =========================================================================================

  openForgotPasswordModal(): void {
    const person = this.detail();

    if (!person) {
      return;
    }

    this.forgotPasswordEmail.set(person.email ?? '');
    this.forgotPasswordSubmitting.set(false);
    this.forgotPasswordSent.set(false);
    this.showForgotPasswordModal.set(true);
  }

  closeForgotPasswordModal(): void {
    this.showForgotPasswordModal.set(false);
  }

  /**
   * Sends the person a reset link.
   *
   * ADDRESSED TO THE ACCOUNT, NOT TO A TYPED ADDRESS. The field is shown so somebody can check
   * where the link is going, and it is read-only for a reason: a reset sent to an address
   * typed on this screen would be a way to take over an account by editing one text box.
   */
  sendForgotPassword(): void {
    const person = this.detail();

    if (!person?.id) {
      return;
    }

    this.forgotPasswordSubmitting.set(true);

    this.securityApi.resetUserPassword(person.id, person.version ?? 0, {
      sendResetLink: true,
      requireChangeOnNextSignIn: true,
    }).subscribe({
      next: (outcome) => {
        this.forgotPasswordSubmitting.set(false);
        this.forgotPasswordSent.set(true);
        this.toast.show('Reset link sent',
          outcome.message ?? `A reset link has been sent to ${person.email}.`, 'success');
        this.refresh();
      },
      error: (error: Error) => {
        this.forgotPasswordSubmitting.set(false);
        this.toast.show('Could not send the link', error.message, 'error');
      },
    });
  }

  getInitials(name: string): string {
    return (name ?? '')
      .split(' ')
      .filter(Boolean)
      .map((part) => part[0])
      .join('')
      .toUpperCase()
      .slice(0, 2) || '?';
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
    this.router.navigate(['/app/administration/access/user-directory']);
  }

  goToDashboard(): void {
    this.router.navigate(['/app/dashboard']);
  }
}
