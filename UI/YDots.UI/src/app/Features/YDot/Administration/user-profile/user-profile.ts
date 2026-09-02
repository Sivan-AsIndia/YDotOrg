import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router, ActivatedRoute } from '@angular/router';
import { Observable } from 'rxjs';
import { ToastService } from '../../../../Shared/services/toast.service';
import { UserDirectoryApiService } from '../../../../Service/user-directory-api.service';
import { SecurityApiService } from '../../../../Service/security-api.service';
import { LayoutService } from '../../../../Service/layout-service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { UserProfileData, RoleAssignmentItem } from '../../../../Shared/models/user-profile.model';
import { UserDetail } from '../../../../Shared/models/user-directory.model';
import { MfaMethodType, UserSecurityResponse } from '../../../../Shared/models/iam-contract.model';

/** One enrolled factor, as the Security tab lists it. */
interface MfaMethodView {
  id: string;
  name: string;
  icon: string;
  description: string;
  enabled: boolean;
}

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

  /** True when this visit has no :userReference and is therefore the caller's own profile. */
  readonly isSelf = signal(false);

  /**
   * The security payload for whichever record is on screen.
   *
   * Held separately from `data` because it comes from a separate endpoint with its own
   * permission, and because it can legitimately be missing while the profile itself is fine.
   */
  readonly security = signal<UserSecurityResponse | null>(null);
  readonly securityLoading = signal(false);
  readonly securityUnavailable = signal(false);
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

  /**
   * The factors ACTUALLY enrolled on this account.
   *
   * THIS USED TO BE A HARDCODED CATALOGUE of five methods — authenticator, SMS, e-mail,
   * security key, biometric — every one of them written `enabled: false` and never touched
   * again. So the Security tab listed five methods that had nothing to do with the account, all
   * of them "Not set up", and the header read "0 enabled" for a person with an authenticator
   * app in their hand. Two of the five ("Biometric") are not methods the server supports at
   * all, so the screen was offering something that could never appear.
   *
   * It is now whatever `/security` returns, and nothing else. An account with no factor shows
   * an empty list saying so, which is the honest answer and the one worth acting on.
   */
  readonly mfaMethods = computed<MfaMethodView[]>(() =>
    (this.security()?.mfaMethods ?? [])
      // Revoked factors are history, not what protects the account today.
      .filter((method) => method.status !== 'revoked')
      .map((method) => ({
        id: method.id ?? '',
        name: this.mfaMethodLabel(method.methodType),
        icon: this.mfaMethodIcon(method.methodType),
        description: [
          method.maskedDestination,
          method.status === 'pending' ? 'Awaiting confirmation' : 'Active',
          method.verifiedAtUtc
            ? `set up ${new Date(method.verifiedAtUtc).toLocaleDateString()}`
            : '',
        ].filter(Boolean).join(' · '),
        enabled: method.status !== 'pending',
      })));

  // Forgot password state
  showForgotPasswordModal = signal(false);
  forgotPasswordEmail = signal('');
  forgotPasswordSubmitting = signal(false);
  forgotPasswordSent = signal(false);

  /**
   * The edit dialog's fields.
   *
   * DEPARTMENT AND MANAGER ARE GONE, and their absence is a fix rather than a loss. They were
   * free-text boxes, prefilled with the department and manager NAMES, and `saveProfile` never
   * sent either - it could not: the update contract takes `departmentId` and `managerUserId`,
   * which are ids, and a typed name is not one. So the dialog invited an edit, accepted it, said
   * "The changes have been saved", and discarded it. Both are organisation-structure decisions
   * and belong to the screens that own them.
   *
   * The mobile number is split into its country code and the number, because the server
   * validates them as a pair and rejects a number without one.
   */
  editForm = signal({
    displayName: '',
    email: '',
    mobileCountryCode: '',
    mobileNumber: '',
    designation: '',
  });

  // Role assignments shown in the "Assigned roles" table.
  // Falls back to an empty list if the API doesn't provide structured data yet.
  roleAssignments = signal<RoleAssignmentItem[]>([]);

  // Sorting for the "Assigned roles" table — click "Sort by" to pick a
  // field; clicking the same field again flips the direction.
  sortField = signal<'role' | 'scope' | 'term'>('role');
  sortAsc = signal(true);
  showSortMenu = signal(false);

  /** What the "Search assigned roles..." box holds. It was an unbound input until now. */
  roleSearch = signal('');

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

  /**
   * The rows the two tables actually render.
   *
   * BOTH TABLES USED TO ITERATE THE RAW LIST. `sortedRoleAssignments` was computed and never
   * read, so the Sort by menu opened, ticked the field it was told to, closed - and left the
   * order exactly as it was. The search box beside it was an `<input>` with no binding at all,
   * which is worse than absent: it invites somebody to type a role name and then shows them
   * every row regardless, which reads as "this person has no such role".
   */
  visibleRoleAssignments = computed(() => {
    const term = this.roleSearch().trim().toLowerCase();
    const rows = this.sortedRoleAssignments();

    if (!term) {
      return rows;
    }

    return rows.filter((row) =>
      [row.role, row.assignmentType, row.scope, row.term]
        .some((value) => (value ?? '').toString().toLowerCase().includes(term)));
  });

  accessCount = computed(() => this.roleAssignments().length);

  /**
   * The sub-line under the name, and the language/timezone cell.
   *
   * Joined here rather than in the template because an Angular template cannot call `Boolean`,
   * and because fixed separators are what produced " ·  · USR-0007" for a person with no
   * designation and no department.
   */
  readonly headerSummary = computed(() =>
    [this.data()?.designation, this.data()?.department, this.data()?.reference]
      .filter((part) => !!part).join(' · '));

  readonly localeSummary = computed(() =>
    [this.data()?.preferredLanguage, this.data()?.timeZone]
      .filter((part) => !!part).join(' · ') || '—');

  /**
   * The status pill's colour, from the status.
   *
   * IT WAS THE LITERAL `status-active` IN THE TEMPLATE, so a suspended or deactivated account
   * wore the green "everything is fine" pill with the word "Suspended" inside it - on the header
   * of the screen somebody opens precisely to find out whether an account is in trouble.
   */
  readonly accountStatusPill = computed(() => {
    switch (this.detail()?.status) {
      case 'active': return 'status-active';
      case 'suspended':
      case 'deactivated':
      case 'withdrawn':
      case 'expired': return 'status-danger';
      case 'invited': return 'status-warning';
      default: return 'status-neutral';
    }
  });

  // =========================================================================================
  // The Security tab and the Security sidebar, from the security endpoint.
  //
  // EVERY VALUE BELOW USED TO BE A LITERAL IN THE TEMPLATE. The sidebar asserted "Multi-factor
  // — Authenticator app — Enrolled" for an account with no factor at all, "Password — Changed
  // 68 days ago — Rotate soon" for one changed this morning, and an access review "due in 19
  // days, last certified 12 February 2026" for a person who has never been reviewed. A screen
  // that states a security position has to be reporting one, because somebody will act on it.
  // =========================================================================================

  /** How many sessions are live right now. */
  readonly activeSessionCount = computed(() => (this.security()?.activeSessions ?? []).length);

  /** How many distinct devices those sessions are on — the sub-line under the count. */
  readonly activeSessionDevices = computed(() => {
    const devices = new Set(
      (this.security()?.activeSessions ?? [])
        .map((session) => session.deviceName || session.browser || session.clientType || '')
        .filter(Boolean));

    return devices.size;
  });

  readonly trustedDeviceCount = computed(() => (this.security()?.trustedDevices ?? []).length);

  /**
   * Failed sign-ins in the last 24 hours and 7 days, counted from the attempt rows.
   *
   * COUNTED HERE RATHER THAN TAKEN FROM `accessFailedCount`, because that column is the
   * consecutive-failure counter the lockout policy uses and it resets to zero the moment
   * somebody signs in successfully. Reporting it as "failed sign-ins in the last 24 hours"
   * would say nothing happened on precisely the account where six attempts failed and the
   * seventh worked — the one case worth seeing.
   */
  readonly failedSignIns = computed(() => {
    const attempts = (this.security()?.recentAttempts ?? []).filter((attempt) => !attempt.succeeded);
    const now = Date.now();
    const day = 24 * 60 * 60 * 1000;

    const within = (windowMs: number) => attempts.filter((attempt) => {
      if (!attempt.attemptedAtUtc) {
        return false;
      }
      const at = new Date(attempt.attemptedAtUtc).getTime();
      return Number.isFinite(at) && now - at <= windowMs;
    }).length;

    return { last24Hours: within(day), last7Days: within(7 * day), total: attempts.length };
  });

  /** "Enrolled" / "Not enrolled", and what with — the sidebar's multi-factor row. */
  readonly mfaSummary = computed(() => {
    const enrolled = this.mfaMethods().filter((method) => method.enabled);
    const model = this.security();

    if (enrolled.length === 0) {
      return {
        status: 'Not enrolled',
        tone: model?.isMfaEffectivelyRequired ? 'text-danger' : 'text-muted',
        detail: model?.isMfaEffectivelyRequired
          ? 'Required — not yet set up'
          : 'No second factor',
        iconTone: 'tone-muted',
      };
    }

    return {
      status: 'Enrolled',
      tone: 'text-success',
      detail: enrolled.map((method) => method.name).join(', '),
      iconTone: 'tone-success',
    };
  });

  /**
   * When the password was last changed, and whether that is long enough ago to say so.
   *
   * Ninety days is the threshold the platform's own password policy uses. Nothing here forces
   * a change — it reports.
   */
  readonly passwordSummary = computed(() => {
    const model = this.security();
    const changedAt = model?.passwordChangedAtUtc;

    if (model?.mustChangePassword) {
      return {
        detail: 'A change is required at the next sign-in',
        trailing: 'Change required',
        tone: 'text-danger',
        iconTone: 'tone-warning',
      };
    }

    if (!changedAt) {
      return {
        detail: 'Never changed since the account was created',
        trailing: 'Never changed',
        tone: 'text-warning',
        iconTone: 'tone-warning',
      };
    }

    const days = Math.max(
      0, Math.floor((Date.now() - new Date(changedAt).getTime()) / (24 * 60 * 60 * 1000)));

    return {
      detail: days === 0
        ? 'Changed today'
        : `Changed ${days} day${days === 1 ? '' : 's'} ago`,
      trailing: days >= 90 ? 'Rotate soon' : 'Current',
      tone: days >= 90 ? 'text-warning' : 'text-success',
      iconTone: days >= 90 ? 'tone-warning' : 'tone-success',
    };
  });

  /** Whether the account is locked out right now, and why. */
  readonly lockout = computed(() => {
    const model = this.security();

    return model?.isLockedOut === true
      ? {
        locked: true,
        reason: model.lockoutReason || 'Too many failed sign-in attempts',
        until: model.lockoutEndUtc ? new Date(model.lockoutEndUtc).toLocaleString() : '',
      }
      : { locked: false, reason: '', until: '' };
  });

  constructor() {
    // WITH NO :userReference THIS SCREEN IS "MY PROFILE", and that is a different question from
    // "show me this person", not the same question with the answer filled in.
    //
    // IT USED TO BE THE SAME QUESTION, and that was the bug. The reference was read off the
    // session and then handed to the administrative directory search, which is gated on
    // `iam.users.view` and sits on a controller that also demands a resolved Organisation. So
    // the page that exists to show you your own profile refused it for two whole groups of
    // people: every role without that permission — most of the fifteen — and a SuperAdmin who
    // had not yet chosen an Organisation. Both saw the same flat "Could not load that person -
    // You do not have permission to perform this action", which is true of the endpoint and
    // completely misleading about the record.
    //
    // `isSelf` is therefore load-bearing rather than cosmetic: it picks `/my-profile`, which
    // takes no id, needs no permission and needs no Organisation.
    const routeRef = this.route.snapshot.params['userReference'];

    this.isSelf.set(!routeRef);

    // Still recorded for the self case, because the "view security" and "change sign-in"
    // shortcuts navigate by it. It is no longer what the record is FETCHED by.
    this.userReference.set(routeRef || (this.tokens.user()?.code ?? ''));

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

    const request = this.resolveRecordRequest();

    if (!request) {
      this.loading.set(false);
      this.loadError.set(true);
      this.toast.show('No user', 'This page needs a user in the address.', 'error');
      return;
    }

    request.subscribe({
      next: (detail) => {
        this.userId.set(detail.id ?? '');
        this.bindUserDetailToProfile(detail);
        this.loadSecurity(detail.id ?? '');
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.loadError.set(true);
        this.toast.show(
          this.isSelf() ? 'Could not load your profile' : 'Could not load that person',
          error.message,
          'error');
      },
    });
  }

  /** Which endpoint answers for this visit: my own record, or a named one. */
  private resolveRecordRequest(): Observable<UserDetail> | null {
    if (this.isSelf()) {
      return this.api.getMyProfile();
    }

    const ref = this.userReference();

    if (!ref) {
      return null;
    }

    // The route carries either the internal id or the reference people quote off a ticket.
    // Both resolve to the same record.
    return this.isGuid(ref) ? this.api.getUser(ref) : this.api.getUserByReference(ref);
  }

  /**
   * Sessions, devices, factors and failed sign-ins — the numbers the Security tab shows.
   *
   * A SECOND CALL, DELIBERATELY. The user record does not carry them and should not: they
   * belong to the endpoint that owns sessions, and duplicating them onto the profile payload
   * would be two numbers on one screen free to disagree. The old code acknowledged this in a
   * comment, set them all to zero, and then never made the call — so "Active sessions", "Trusted
   * devices" and "Failed sign-ins (24h)" read 0 for everybody, always, and the MFA list was a
   * hardcoded catalogue of five methods that were permanently "Not set up".
   *
   * `/my-security` for my own record and `/users/{id}/security` for somebody else's: the same
   * split as everywhere else, because the second is permission-gated and the first cannot be
   * pointed at anybody.
   *
   * A FAILURE HERE DOES NOT FAIL THE PAGE. The identity and organisation details have already
   * arrived and are worth showing; the security panel says it is unavailable instead. An
   * administrator who may read a profile is not guaranteed to hold `iam.user-security.view`.
   */
  private loadSecurity(userId: string): void {
    if (!this.isSelf() && !userId) {
      return;
    }

    this.securityLoading.set(true);
    this.securityUnavailable.set(false);

    const request = this.isSelf()
      ? this.securityApi.getMySecurity()
      : this.securityApi.getUserSecurity(userId);

    request.subscribe({
      next: (security) => {
        this.security.set(security);
        this.securityLoading.set(false);
      },
      error: () => {
        this.security.set(null);
        this.securityLoading.set(false);
        this.securityUnavailable.set(true);
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
      // WITH ITS COUNTRY CODE. The number was rendered on its own, so a screen whose job is to
      // let somebody ring this person showed a number nobody outside their country can dial.
      mobileNumber: [detail.mobileCountryCode, detail.mobileNumber]
        .filter(Boolean).join(' ').trim(),
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

      // JOINED IS `joinedOn`, NOT THE ACCESS WINDOW. The Organisation card labelled its date
      // "Joined" and read `accessStartsAtUtc`, which is when the account was allowed to sign in
      // - the same day for somebody created on arrival, and years out for anybody whose account
      // was raised later. `joinedOn` is the field that actually answers it; the access window
      // is still shown, under its own name, on the Access tab.
      joinedOn: detail.joinedOn
        ? new Date(detail.joinedOn).toLocaleDateString()
        : detail.accessStartsAtUtc
          ? new Date(detail.accessStartsAtUtc).toLocaleDateString()
          : '',

      mfaStatus: detail.mfaEnabled ? 'Enrolled' : 'Not enrolled',
      mfaStatusClass: detail.mfaEnabled ? 'bg-success' : 'bg-danger',

      // Left at zero HERE and read from the `security` signal in the template instead. These
      // three belong to the security endpoint, which answers a moment later; carrying a stale
      // copy on this object as well would give the screen two places to disagree with itself.
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

  /**
   * Opens the edit dialog on this screen.
   *
   * IT USED TO NAVIGATE TO /administration/access/create-user, carrying the record in the
   * router's `state`. Create-user reads no such state - it has no `editMode`, no `userData`, and
   * no reference to `history.state` anywhere - so Edit profile left the profile and presented an
   * empty CREATE form. Filling it in would have made a second user rather than editing this one.
   *
   * The dialog it should have opened is already in this component's template, already bound to
   * `editForm`, and already saved by `saveProfile()` through PUT /users/{id}. Nothing needed
   * building; the navigation simply had to stop.
   */
  openEditModal(): void {
    const detail = this.detail();
    if (!detail) return;

    this.editForm.set({
      displayName: detail.displayName ?? '',
      email: detail.email ?? '',
      mobileCountryCode: detail.mobileCountryCode ?? '',
      mobileNumber: detail.mobileNumber ?? '',
      designation: detail.designation ?? '',
    });

    this.showEditModal.set(true);
    this.showMoreMenu.set(false);
  }
  closeEditModal(): void { this.showEditModal.set(false); }
  saveProfile(): void {
    const form = this.editForm();
    const detail = this.detail();

    if (!form.displayName.trim()) {
      this.toast.show('Check the form', 'A display name is required.', 'warning');
      return;
    }

    if (form.mobileNumber.trim() && !form.mobileCountryCode.trim()) {
      this.toast.show('Check the form',
        'Give the country code as well - the server rejects a number without one.', 'warning');
      return;
    }

    if (!detail?.id) {
      return;
    }

    this.submitting.set(true);

    const reason = 'Profile edited from the user profile screen.';

    // THE E-MAIL ADDRESS IS NOT SENT. Changing what somebody signs in with is a request with
    // its own verification and approval — see the login-identifier-change screen — precisely so
    // a mis-typed address cannot quietly redirect an account's password resets. The field is
    // shown read-only for the same reason.
    //
    // TWO ENDPOINTS, PICKED BY WHOSE RECORD THIS IS. `PUT /users/{id}` needs `iam.users.edit`
    // AND a resolved Organisation, so it answered "You do not have permission to perform this
    // action" to most roles editing their OWN name, and to every root user who had not yet
    // chosen an Organisation. `PUT /my-profile` takes no id, needs no permission and needs no
    // Organisation — the same split as the read above and as /my-security.
    const request = this.isSelf()
      ? this.api.updateMyProfile({
        expectedVersion: detail.version ?? 0,
        displayName: form.displayName.trim(),
        mobileCountryCode: form.mobileCountryCode?.trim() || null,
        mobileNumber: form.mobileNumber?.trim() || null,
        designation: form.designation?.trim() || null,
        reason,
      })
      : this.api.updateUser(detail.id, {
        expectedVersion: detail.version ?? 0,
        displayName: form.displayName.trim(),
        mobileCountryCode: form.mobileCountryCode?.trim() || null,
        mobileNumber: form.mobileNumber?.trim() || null,
        designation: form.designation?.trim() || null,
        reason,
      });

    request.subscribe({
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
   * code from the device, which is the whole point of it.
   *
   * IT WAS CALLING THE WRONG ENDPOINT. The id in hand is an MFA method id and it was being
   * passed to `DELETE /users/{id}/trusted-devices/{deviceId}` — a different collection
   * entirely. No method id has ever matched a trusted-device id, so the call could only ever
   * 404, and the screen reported "Could not remove it" for a method that was still perfectly
   * removable through the security page.
   *
   * ONLY ON YOUR OWN ACCOUNT. There is no per-method endpoint for somebody else's, and
   * deliberately so — an administrator removing one factor and leaving another is a half-state
   * nobody asked for. The answer for a lost phone is `reset-mfa`, which clears every factor,
   * every session and every remembered device together; the button for that is already on this
   * screen.
   */
  removeMfaMethod(methodId: string): void {
    if (!methodId) {
      return;
    }

    if (!this.isSelf()) {
      this.toast.show(
        'Use Reset two-step verification',
        'One factor cannot be removed from somebody else\'s account on its own. Resetting clears '
        + 'every factor, session and remembered device together.',
        'info');
      return;
    }

    this.submitting.set(true);

    this.securityApi
      .revokeMyMfaMethod(methodId, 'Removed from the profile screen.')
      .subscribe({
        next: (outcome) => {
          this.submitting.set(false);
          this.toast.show('Removed',
            outcome.message ?? 'That verification method has been removed.', 'success');
          this.loadData();
        },
        error: (error: Error) => {
          this.submitting.set(false);

          // The server refuses when it is the last factor and the Organisation requires MFA.
          // That refusal is the useful message, so it is shown as-is.
          this.toast.show('Could not remove it', error.message, 'error');
        },
      });
  }

  getInitials(name: string): string { return name.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2); }

  // =========================================================================================
  // Wording for the factor list. The API answers in the domain's vocabulary
  // (`authenticatorApp`, `securityKey`) and the screen speaks a person's.
  // =========================================================================================

  private mfaMethodLabel(methodType: MfaMethodType | undefined): string {
    switch (methodType) {
      case 'authenticatorApp': return 'Authenticator app';
      case 'sms': return 'Text message';
      case 'email': return 'E-mail';
      case 'securityKey': return 'Security key';
      default: return 'Verification method';
    }
  }

  private mfaMethodIcon(methodType: MfaMethodType | undefined): string {
    switch (methodType) {
      case 'authenticatorApp': return 'ri-smartphone-line';
      case 'sms': return 'ri-message-3-line';
      case 'email': return 'ri-mail-line';
      case 'securityKey': return 'ri-key-2-line';
      default: return 'ri-shield-keyhole-line';
    }
  }
}