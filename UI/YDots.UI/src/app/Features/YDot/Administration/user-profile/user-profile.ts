import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router, ActivatedRoute } from '@angular/router';
import { ToastService } from '../../../../Shared/services/toast.service';
import { UserDirectoryApiService } from '../../../../Service/user-directory-api.service';
import { SecurityApiService } from '../../../../Service/security-api.service';
import { LayoutService } from '../../../../Service/layout-service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { UserProfileData, RoleAssignmentItem } from '../../../../Shared/models/user-profile.model';
import { UserDetail } from '../../../../Shared/models/user-directory.model';

@Component({
  selector: 'app-user-profile',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './user-profile.html',
  styleUrl: './user-profile.css',
})
export class UserProfileComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly api = inject(UserDirectoryApiService);
  private readonly securityApi = inject(SecurityApiService);
  private readonly layoutService = inject(LayoutService);
  private readonly tokens = inject(AuthTokenService);

  data = signal<UserProfileData | null>(null);

  /**
   * The record exactly as the server sent it.
   *
   * KEPT ALONGSIDE the display shape above, because every write needs the id and the version
   * and neither belongs on a view model. Without the version, two administrators editing the
   * same person means the second silently undoes the first.
   */
  readonly detail = signal<UserDetail | null>(null);
  readonly userId = signal('');
  loading = signal(true);
  loadError = signal(false);
  userReference = signal('');
  activeTab = signal<'profile' | 'access' | 'security' | 'history'>('profile');
  showEditModal = signal(false);
  showSuspendModal = signal(false);
  suspendReason = signal('');
  submitting = signal(false);
  showMoreMenu = signal(false);

  // MFA toggle state
  mfaEnabled = signal(false);
  showMfaModal = signal(false);
  mfaAction = signal<'enable' | 'disable'>('enable');
  mfaSubmitting = signal(false);
  mfaMethodAction = signal<string>('');

  // MFA methods with individual toggles
  mfaMethods = signal<{ id: string; name: string; icon: string; description: string; enabled: boolean }[]>([
    { id: 'authenticator', name: 'Authenticator App', icon: 'ri-smartphone-line', description: 'Google Authenticator, Authy or Microsoft Authenticator', enabled: false },
    { id: 'sms', name: 'SMS', icon: 'ri-message-3-line', description: 'Receive a one-time code on your mobile phone', enabled: false },
    { id: 'email', name: 'Email OTP', icon: 'ri-mail-line', description: 'Receive a one-time passcode to your email', enabled: false },
    { id: 'security-key', name: 'Security Key', icon: 'ri-key-2-line', description: 'FIDO2 hardware security key (YubiKey, Titan)', enabled: false },
    { id: 'biometric', name: 'Biometric', icon: 'ri-fingerprint-line', description: 'Fingerprint or face recognition', enabled: false }
  ]);

  // Forgot password state
  showForgotPasswordModal = signal(false);
  forgotPasswordEmail = signal('');
  forgotPasswordSubmitting = signal(false);
  forgotPasswordSent = signal(false);

  editForm = signal({ displayName: '', email: '', mobile: '', department: '', designation: '', manager: '' });

  // Role assignments shown in the "Assigned roles" table.
  // Falls back to an empty list if the API doesn't provide structured data yet.
  roleAssignments = signal<RoleAssignmentItem[]>([]);

  // Sorting for the "Assigned roles" table — click "Sort by" to pick a
  // field; clicking the same field again flips the direction.
  sortField = signal<'role' | 'scope' | 'term'>('role');
  sortAsc = signal(true);
  showSortMenu = signal(false);

  sortedRoleAssignments = computed(() => {
    const field = this.sortField();
    const asc = this.sortAsc();
    const items = [...this.roleAssignments()];
    items.sort((a, b) => {
      const av = (a[field] ?? '').toString().toLowerCase();
      const bv = (b[field] ?? '').toString().toLowerCase();
      if (av < bv) return asc ? -1 : 1;
      if (av > bv) return asc ? 1 : -1;
      return 0;
    });
    return items;
  });

  accessCount = computed(() => this.roleAssignments().length);

  constructor() {
    // WITH NO :userReference THIS SCREEN IS "MY PROFILE", so the reference has to come from the
    // signed-in session.
    //
    // It used to read sessionStorage/localStorage under the key 'userData' and, failing that, fall
    // back to the literal 'USR-000184'. AuthTokenService has never written that key - it stores the
    // user under 'ydot.user' - so the lookup ALWAYS missed and every visit asked the API for a
    // reference belonging to nobody. The result was "No user matches the reference USR-000184" on a
    // page whose whole job is to show you your own profile.
    //
    // Reading the service directly also means there is one place that knows where the session is
    // kept, instead of two that can drift apart again.
    const routeRef = this.route.snapshot.params['userReference'];
    const signedInRef = this.tokens.user()?.code ?? '';

    if (routeRef) {
      this.userReference.set(routeRef);
    } else if (signedInRef) {
      this.userReference.set(signedInRef);
    } else {
      // No route parameter and no session: there is nothing to fetch, and guessing a reference is
      // what caused the original bug. Say so plainly instead of calling the API with a made-up id.
      this.loading.set(false);
      this.loadError.set(true);
      this.toast.show(
        'Not signed in',
        'Your session could not be read. Sign in again to see your profile.',
        'error');
      return;
    }

    this.loadData();
  }

  /**
   * Reads the record from the server.
   *
   * ONE PATH, NOT FOUR. This used to try navigation state, then a shared directory cache, then
   * the directory endpoint, then a static JSON file — and each fallback could show a different
   * answer from the one before it. Worse, the cache was written by other screens, so a status
   * changed in one tab and REFUSED by the server still appeared here as though it had worked.
   *
   * The route carries either the internal id or the reference people quote. Both resolve to the
   * same record; nothing else is consulted.
   */
  private loadData(): void {
    this.loading.set(true);
    this.loadError.set(false);

    const ref = this.userReference();

    if (!ref) {
      this.loading.set(false);
      this.loadError.set(true);
      this.toast.show('No user', 'This page needs a user in the address.', 'error');
      return;
    }

    const request = this.isGuid(ref)
      ? this.api.getUser(ref)
      : this.api.getUserByReference(ref);

    request.subscribe({
      next: (detail) => {
        this.userId.set(detail.id ?? '');
        this.bindUserDetailToProfile(detail);
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.loadError.set(true);
        this.toast.show('Could not load that person', error.message, 'error');
      },
    });
  }

  /** True when the value looks like a GUID/UUID — the user id the directory passes. */
  private isGuid(value: string): boolean {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
  }

  /**
   * Maps the API's full user record onto the profile screen's shape.
   *
   * WHERE THE TWO SHAPES DISAGREE, THE API WINS. Several fields the screen once showed do not
   * exist on the server and never did — a preferred name, a work location, a separate invitation
   * status. Rendering them from an invented shape produced blank rows that looked like missing
   * data rather than fields that were never collected, so they are gone.
   *
   * The counts that are genuinely elsewhere — sessions, trusted devices, failed sign-ins — are
   * zero here and filled in by the security tab, which fetches them from the endpoint that owns
   * them. Guessing them on this screen would mean two numbers that disagree.
   */
  private bindUserDetailToProfile(detail: UserDetail): void {
    this.detail.set(detail);
    const roles = detail.roles ?? [];
    const scopes = detail.dataScopes ?? [];

    // Written once and reused for both the summary line and each role's scope column.
    const scopeSummary = scopes
      .map((scope) => scope.displayLabel || scope.scopeValue || scope.scopeType)
      .filter(Boolean)
      .join(', ');

    const profileData: UserProfileData = {
      reference: detail.code ?? '',
      displayName: detail.displayName ?? '',
      loginEmail: detail.email ?? '',
      username: detail.username ?? '',
      mobileNumber: detail.mobileNumber ?? '',
      password: '',
      employeeId: detail.employeeNumber ?? '',
      accountCategory: detail.accountCategory ?? '',
      accountStatus: detail.statusDisplay ?? detail.status ?? '',
      accountStatusClass:
        detail.status === 'active' ? 'bg-success'
        : detail.status === 'suspended' ? 'bg-danger'
        : 'bg-secondary',

      // There is no separate invitation status on the server: the account's own status carries
      // it. `invited` means an outstanding invitation, and `hasPendingInvitation` says whether
      // the link is still live.
      invitationStatus: detail.hasPendingInvitation
        ? 'Invitation outstanding'
        : detail.status === 'invited' ? 'Invitation expired' : 'Accepted',
      invitationStatusClass: detail.hasPendingInvitation ? 'bg-warning' : 'bg-success',

      organisationUnit: detail.organisationUnitName ?? '',
      department: detail.departmentName ?? '',
      designation: detail.designation ?? '',
      manager: detail.managerName ?? '',
      workLocation: '',
      preferredLanguage: detail.preferredCulture ?? '',
      timeZone: detail.timeZone ?? '',

      roleAssignments: roles.map((role) => ({
        role: role.roleName ?? role.roleCode ?? '',
        assignmentType: role.isPrimary ? 'Primary assignment' : 'Direct assignment',
        scope: scopeSummary || 'Whole organisation',

        // The server counts the permissions each role carries, so the screen does not have to
        // load the role to say how much it grants.
        permissions: role.permissionCount ?? 0,
        term: role.effectiveToUtc ? 'Temporary' : 'Permanent',
      })),

      dataScopes: scopeSummary || 'Whole organisation',
      accessStartDate: detail.accessStartsAtUtc
        ? new Date(detail.accessStartsAtUtc).toLocaleDateString()
        : '',
      accessEndDate: detail.accessEndsAtUtc
        ? new Date(detail.accessEndsAtUtc).toLocaleDateString()
        : '',

      // Access reviews are a governance record rather than a field on the person, so this is
      // answered by the access-review screen and left blank here.
      accessReviewDue: '',

      mfaStatus: detail.mfaEnabled ? 'Enrolled' : 'Not enrolled',
      mfaStatusClass: detail.mfaEnabled ? 'bg-success' : 'bg-danger',

      // Filled in by the security tab from the endpoint that owns them. Guessing here would
      // produce two numbers on the same screen that disagree.
      activeSessions: 0,
      activeSessionDevices: 0,
      trustedDevices: 0,

      lastSignIn: detail.lastLoginAtUtc
        ? new Date(detail.lastLoginAtUtc).toLocaleString()
        : 'Never',
      failedSignins: { last24Hours: 0, last7Days: 0, total: detail.accessFailedCount ?? 0 },
      concurrencyVersion: String(detail.version ?? 0),
    };

    this.data.set(profileData);
    this.mfaEnabled.set(detail.mfaEnabled === true);
    this.roleAssignments.set(profileData.roleAssignments ?? []);
    this.loading.set(false);
  }


  /**
   * There is no fallback.
   *
   * This once loaded a static JSON profile when the lookup failed, which meant a failed request
   * rendered as somebody else's details rather than as an error. A person's record either comes
   * from the server or the page says so.
   */

  retry(): void { this.loadData(); }
  goBack(): void { this.router.navigate(['/app/administration/access/user-directory']); }
  setTab(tab: 'profile' | 'access' | 'security' | 'history'): void { this.activeTab.set(tab); }
  toggleMoreMenu(): void { this.showMoreMenu.set(!this.showMoreMenu()); }

  toggleSortMenu(): void { this.showSortMenu.set(!this.showSortMenu()); }
  setSort(field: 'role' | 'scope' | 'term'): void {
    if (this.sortField() === field) {
      this.sortAsc.set(!this.sortAsc());
    } else {
      this.sortField.set(field);
      this.sortAsc.set(true);
    }
    this.showSortMenu.set(false);
  }

  goToDashboard(): void {
    this.router.navigate(['/app/dashboard']);
  }

  /** Opens the theme settings panel (offcanvas design) */
  openThemeSettings(): void {
    this.layoutService.openThemePanel();
    this.showMoreMenu.set(false);
  }

  openEditModal(): void {
    const d = this.data();
    if (!d) return;
    this.editForm.set({ displayName: d.displayName, email: d.loginEmail, mobile: d.mobileNumber, department: d.department, designation: d.designation, manager: d.manager });
    // Navigate to create-user page in edit mode, passing all user details for binding
    this.router.navigate(['/app/administration/access/create-user'], {
      state: { userData: d, editMode: true }
    });
    this.showMoreMenu.set(false);
  }
  closeEditModal(): void { this.showEditModal.set(false); }
  saveProfile(): void {
    const form = this.editForm();
    const detail = this.detail();

    if (!form.displayName.trim() || !form.email.trim()) {
      this.toast.show('Check the form', 'A display name and an e-mail address are required.',
        'warning');
      return;
    }

    if (!detail?.id) {
      return;
    }

    this.submitting.set(true);

    // THE E-MAIL ADDRESS IS NOT SENT. Changing what somebody signs in with is a request with
    // its own verification and approval — see the login-identifier-change screen — precisely so
    // a mis-typed address cannot quietly redirect an account's password resets.
    this.api.updateUser(detail.id, {
      expectedVersion: detail.version ?? 0,
      displayName: form.displayName.trim(),
      mobileNumber: form.mobile?.trim() || null,
      designation: form.designation?.trim() || null,
      reason: 'Profile edited from the user profile screen.',
    }).subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        this.closeEditModal();
        this.toast.show('Profile updated', outcome.message ?? 'The changes have been saved.',
          'success');
        this.loadData();
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.toast.show('Could not save the changes', error.message, 'error');
      },
    });
  }

  openSuspendModal(): void { this.showSuspendModal.set(true); this.suspendReason.set(''); this.showMoreMenu.set(false); }
  closeSuspendModal(): void { this.showSuspendModal.set(false); this.suspendReason.set(''); }
  confirmSuspend(): void {
    const detail = this.detail();
    const reason = this.suspendReason().trim();

    if (reason.length < 10) {
      this.toast.show('Check the form', 'Give a reason of at least 10 characters.', 'warning');
      return;
    }

    if (!detail?.id) {
      return;
    }

    this.submitting.set(true);

    this.api.suspendUser(detail.id, { reason, expectedVersion: detail.version ?? 0 }).subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        this.showSuspendModal.set(false);
        this.suspendReason.set('');
        this.toast.show('Account suspended',
          outcome.message ?? `${detail.displayName} has been suspended and signed out.`,
          'success');
        this.loadData();
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.toast.show('Could not suspend the account', error.message, 'error');
      },
    });
  }

  /**
   * Clears every second factor on the account.
   *
   * Sent straight through rather than confirmed here: the security screen is where this action
   * belongs, with its mandatory reason and its full explanation of what goes with it. This is
   * the shortcut from the profile, so it hands over rather than doing a lesser version.
   */
  resetMfa(): void {
    this.showMoreMenu.set(false);
    this.router.navigate(['/app/administration/users', this.userReference(), 'security']);
  }

  revokeSessions(): void {
    const detail = this.detail();

    if (!detail?.id) {
      return;
    }

    this.submitting.set(true);

    this.api.forceSignOut(detail.id, {
      reason: 'Signed out from the profile screen.',
      expectedVersion: detail.version ?? 0,
    }).subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        this.toast.show('Signed out everywhere',
          outcome.message ?? 'Every session has ended.', 'success');
        this.loadData();
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.toast.show('Could not sign them out', error.message, 'error');
      },
    });
  }
  viewSecurity(): void { this.router.navigate(['/app/administration/users', this.userReference(), 'security']); }
  viewLoginIdentifierChange(): void {
    this.router.navigate(['/app/administration/users', this.userReference(), 'login-identifier-change']);
  }

  // ==================== MFA Enable/Disable ====================

  getMfaStatusLabel(): string {
    return this.mfaEnabled() ? 'Enabled' : 'Not Enabled';
  }

  getMfaStatusClass(): string {
    return this.mfaEnabled() ? 'bg-success' : 'bg-danger';
  }

  openMfaModal(action: 'enable' | 'disable'): void {
    this.mfaAction.set(action);
    this.mfaSubmitting.set(false);
    this.showMfaModal.set(true);
  }

  closeMfaModal(): void {
    this.showMfaModal.set(false);
  }

  toggleMfa(): void {
    const detail = this.detail();
    const action = this.mfaAction();

    if (!detail?.id) {
      return;
    }

    this.mfaSubmitting.set(true);

    const reason = action === 'enable'
      ? 'Two-step verification required from the profile screen.'
      : 'Two-step verification reset from the profile screen.';

    const done = (message: string) => {
      this.mfaSubmitting.set(false);
      this.showMfaModal.set(false);
      this.toast.show('Done', message, 'success');
      this.loadData();
    };

    const failed = (error: Error) => {
      this.mfaSubmitting.set(false);
      this.toast.show('That did not work', error.message, 'error');
    };

    // NOBODY CAN ENROL A FACTOR FOR SOMEBODY ELSE — that needs the device in their hand. So
    // "enable" sets the requirement and the person completes it at their next sign-in, and
    // "disable" clears the factors that exist, which is the lost-phone case.
    if (action === 'enable') {
      this.api.updateUser(detail.id, {
        expectedVersion: detail.version ?? 0,
        mfaRequirement: 'required',
        reason,
      }).subscribe({
        next: () => done(
          `${detail.displayName} will be asked to set up two-step verification at their next sign-in.`),
        error: failed,
      });
      return;
    }

    this.securityApi.resetUserMfa(detail.id, reason).subscribe({
      next: (outcome: { message?: string | null }) =>
        done(outcome.message ?? 'Two-step verification has been reset.'),
      error: failed,
    });
  }

  enableMfa(): void {
    this.openMfaModal('enable');
  }

  disableMfa(): void {
    this.openMfaModal('disable');
  }

  /**
   * Removes one enrolled factor.
   *
   * REMOVING, NOT TOGGLING. A factor cannot be switched back on from here: enrolling needs a
   * code from the device, which is the whole point of it. The old version flipped a boolean in
   * memory and wrote the result into three caches and localStorage, so the account looked
   * protected — or unprotected — while nothing had actually changed on the server.
   */
  removeMfaMethod(methodId: string): void {
    const detail = this.detail();

    if (!detail?.id || !methodId) {
      return;
    }

    this.submitting.set(true);

    this.securityApi
      .revokeUserTrustedDevice(detail.id, methodId, 'Removed from the profile screen.')
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.toast.show('Removed', 'That verification method has been removed.', 'success');
          this.loadData();
        },
        error: (error: Error) => {
          this.submitting.set(false);
          this.toast.show('Could not remove it', error.message, 'error');
        },
      });
  }

  getInitials(name: string): string { return name.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2); }
}