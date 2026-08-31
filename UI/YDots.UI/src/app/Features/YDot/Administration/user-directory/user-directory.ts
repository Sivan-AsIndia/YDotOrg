import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { Observable, Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';
import { ToastService } from '../../../../Shared/services/toast.service';
import { UserDirectoryApiService } from '../../../../Service/user-directory-api.service';
import {
  UserDetail,
  UserDirectoryResponse,
  UserListItem,
  UserSearchFilter,
} from '../../../../Shared/models/user-directory.model';
import { LookupItem } from '../../../../Shared/models/api-response.model';
import { UserStatus } from '../../../../Shared/models/iam-contract.model';

type DialogKind = 'none' | 'view' | 'edit' | 'suspend' | 'reactivate' | 'delete' | 'invite';


@Component({
  selector: 'app-user-directory',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './user-directory.html',
  styleUrl: './user-directory.css',
})
export class UserDirectoryComponent implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly api = inject(UserDirectoryApiService);

  private readonly destroy$ = new Subject<void>();
  private readonly searchInput$ = new Subject<string>();

  // ---- Data ---------------------------------------------------------------------------------
  readonly data = signal<UserDirectoryResponse | null>(null);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly busy = signal(false);
  readonly errorMessage = signal('');

  // ---- Filters ------------------------------------------------------------------------------
  searchText = '';
  readonly filter = signal<UserSearchFilter>({ pageIndex: 1, pageSize: 10 });

  // ---- Dialogs -------------------------------------------------------------------------------
  readonly dialog = signal<DialogKind>('none');
  readonly selected = signal<UserListItem | null>(null);
  readonly detail = signal<UserDetail | null>(null);
  readonly detailLoading = signal(false);

  // ---- Bulk selection ------------------------------------------------------------------------
  /** User ids checked in the table for a bulk action. */
  readonly selectedIds = signal<Set<string>>(new Set());
  readonly bulkSelectionCount = computed(() => this.selectedIds().size);
  readonly allOnPageSelected = computed(() => {
    const page = this.users();
    return page.length > 0 && page.every((u) => this.selectedIds().has((u.id ?? '')));
  });

  reason = '';
  readonly deleteConfirmation = signal('');
  welcomeMessage = '';

  readonly editForm = signal({
    title: '',
    firstName: '',
    middleName: '',
    lastName: '',
    displayName: '',
    preferredName: '',
    mobileCountryCode: '',
    mobileNumber: '',
    employeeNumber: '',
    designation: '',
    workLocation: '',
    preferredLanguage: 'en-GB',
    timeZoneId: 'UTC',
  });

  readonly copiedField = signal('');

  // =========================================================================================
  // Derived
  // =========================================================================================

  readonly users = computed(() => this.data()?.users.items ?? []);
  readonly totalCount = computed(() => this.data()?.users.totalCount ?? 0);
  readonly pageIndex = computed(() => this.data()?.users.page ?? 1);
  readonly pageSize = computed(() => this.data()?.users.pageSize ?? 10);
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));

  readonly statusOptions = computed<LookupItem[]>(() => this.data()?.statusOptions ?? []);
  readonly categoryOptions = computed<LookupItem[]>(() => this.data()?.accountCategoryOptions ?? []);
  readonly unitOptions = computed<LookupItem[]>(() => this.data()?.organisationUnitOptions ?? []);
  readonly roleOptions = computed<LookupItem[]>(() => this.data()?.roleOptions ?? []);

  /** What this caller may do, decided by the server from their permissions — not guessed here. */
  readonly permittedActions = computed(() => this.data()?.permittedActions ?? []);

  /**
   * These stay visible until the server has actually said "no".
   *
   * Gating purely on `permittedActions.includes(…)` hides the button whenever `data()` is null —
   * which is also true while the page is loading and after a failed load. The result is a missing
   * Create User button that looks like a permissions problem when it is really a network one.
   * Showing it until we know otherwise is the kinder failure: the server still refuses an
   * unauthorised create, so nothing is actually exposed by an optimistic button.
   */
  private readonly loaded = computed(() => this.data() !== null);

  readonly canCreate = computed(() => !this.loaded() || this.permittedActions().includes('Invite'));
  readonly canExport = computed(() => !this.loaded() || this.permittedActions().some((action) => action.startsWith('Export')));

  /** Row-level actions are only drawn once the row exists, so plain gating is right here. */
  readonly canSuspend = computed(() => this.permittedActions().includes('Suspend'));

  readonly filterSummary = computed(() => this.data()?.activeFilterSummary ?? '');
  readonly dataScopeSummary = computed(() => this.data()?.dataScopeSummary ?? '');

  readonly hasActiveFilters = computed(() => {
    const current = this.filter();
    return Boolean(current.search || current.status || current.accountCategory || current.organisationUnitId || current.roleId);
  });

  /** Page numbers around the current one, so the pager stays short on a large directory. */
  readonly pageNumbers = computed(() => {
    const total = this.totalPages();
    const current = this.pageIndex();
    const from = Math.max(1, current - 2);
    const to = Math.min(total, current + 2);

    return Array.from({ length: Math.max(0, to - from + 1) }, (_, index) => from + index);
  });

  /**
   * A draft that was never invited can be erased; anything else is deactivated.
   * Driving the dialog from this keeps the wording honest about what will happen.
   */
  /**
   * True when the record can simply be removed rather than deactivated.
   *
   * Only a DRAFT qualifies: nobody has ever signed in as it, so there is no history to preserve
   * and nothing to attribute. Everything else is deactivated, because a person's actions have to
   * remain traceable to somebody long after they have left.
   */
  readonly isHardDelete = computed(() => this.selected()?.status === 'draft');

  readonly canConfirmDelete = computed(
    () => this.deleteConfirmation().trim().toLowerCase() === (this.selected()?.displayName ?? '').toLowerCase(),
  );

  // =========================================================================================
  // Lifecycle
  // =========================================================================================

  ngOnInit(): void {
    // Typing fires a request per keystroke without this. 350 ms is long enough to finish a word
    // and short enough that the list still feels live; distinctUntilChanged drops the repeat
    // that arrow keys and Ctrl produce.
    this.searchInput$
      .pipe(debounceTime(350), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe((text) => {
        this.filter.update((current) => ({ ...current, search: text || undefined, pageIndex: 1 }));
        this.load();
      });

    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // =========================================================================================
  // Loading
  // =========================================================================================

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);

    this.api.getDirectory(this.filter()).subscribe({
      next: (response) => {
        this.data.set(response);
        this.loading.set(false);
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.loadFailed.set(true);
        this.errorMessage.set(error.message);
      },
    });
  }

  retry(): void {
    this.load();
  }

  // =========================================================================================
  // Filters and paging
  // =========================================================================================

  onSearchChange(value: string): void {
    this.searchText = value;
    this.searchInput$.next(value.trim());
  }

  setFilter(key: keyof UserSearchFilter, value: string): void {
    this.filter.update((current) => ({
      ...current,
      [key]: value || undefined,
      // Any filter change invalidates the current page number: page 7 of the old result set is
      // meaningless in the new one.
      pageIndex: 1,
    }));

    this.load();
  }

  clearFilters(): void {
    this.searchText = '';
    this.filter.set({ pageIndex: 1, pageSize: this.pageSize() });
    this.load();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.pageIndex()) {
      return;
    }

    this.filter.update((current) => ({ ...current, pageIndex: page }));
    this.load();
  }

  changePageSize(size: number): void {
    this.filter.update((current) => ({ ...current, pageSize: size, pageIndex: 1 }));
    this.load();
  }

  // =========================================================================================
  // Navigation
  // =========================================================================================

  openProfile(user: UserListItem): void {
    // Pass the row along in router state so the profile page can render the selected
    // user immediately, and still has data to show if the detail API call fails.
    void this.router.navigate(['/app/administration/access/user-profile-and-access', (user.id ?? '')], {
      state: { userData: user },
    });
  }

  createUser(): void {
    void this.router.navigate(['/app/administration/access/create-user']);
  }

  // =========================================================================================
  // Bulk selection
  // =========================================================================================

  toggleRowSelection(user: UserListItem): void {
    this.selectedIds.update((current) => {
      const next = new Set(current);
      if (next.has((user.id ?? ''))) {
        next.delete((user.id ?? ''));
      } else {
        next.add((user.id ?? ''));
      }
      return next;
    });
  }

  toggleAllOnPage(): void {
    this.selectedIds.update((current) => {
      const next = new Set(current);
      const page = this.users();
      if (this.allOnPageSelected()) {
        page.forEach((u) => next.delete((u.id ?? '')));
      } else {
        page.forEach((u) => next.add((u.id ?? '')));
      }
      return next;
    });
  }

  isRowSelected(user: UserListItem): boolean {
    return this.selectedIds().has((user.id ?? ''));
  }

  clearSelection(): void {
    this.selectedIds.set(new Set());
  }

  goToBulkActions(): void {
    const selected = this.users().filter((u) => this.selectedIds().has((u.id ?? '')));
    if (selected.length === 0) {
      this.toast.show('No Selection', 'Select at least one user to run a bulk action.', 'warning');
      return;
    }

    // Pass the API rows (with the GUID `id`) so the bulk page can send real user ids to the
    // bulk API — the server rejects reference strings like USR-000104 for `userIds`.
    void this.router.navigate(['/app/administration/users/bulk-actions'], {
      state: { selectedUsers: selected },
    });
  }

  // =========================================================================================
  // Dialogs
  // =========================================================================================

  openView(user: UserListItem): void {
    this.selected.set(user);
    this.dialog.set('view');
    this.loadDetail((user.id ?? ''));
  }

  openEdit(user: UserListItem): void {
    this.selected.set(user);
    this.dialog.set('edit');
    this.errorMessage.set('');
    this.reason = '';

    // The list row does not carry every editable field, so the full record is fetched. Editing
    // from a partial row would blank out whatever the row happened not to include.
    this.loadDetail((user.id ?? ''), (detail) => {
      this.editForm.set({
        title: '',
        firstName: detail.firstName ?? '',
        middleName: detail.middleName ?? '',
        lastName: detail.lastName ?? '',
        displayName: detail.displayName ?? '',
        preferredName: '',
        mobileCountryCode: detail.mobileCountryCode ?? '',
        mobileNumber: detail.mobileNumber ?? '',
        employeeNumber: detail.employeeNumber ?? '',
        designation: detail.designation ?? '',
        workLocation: '',
        preferredLanguage: detail.preferredCulture ?? 'en-GB',
        timeZoneId: detail.timeZone ?? 'UTC',
      });
    });
  }

  openSuspend(user: UserListItem): void {
    this.selected.set(user);
    this.reason = '';
    this.errorMessage.set('');
    this.dialog.set('suspend');
  }

  openReactivate(user: UserListItem): void {
    this.selected.set(user);
    this.reason = '';
    this.errorMessage.set('');
    this.dialog.set('reactivate');
  }

  openDelete(user: UserListItem): void {
    this.selected.set(user);
    this.reason = '';
    this.deleteConfirmation.set('');
    this.errorMessage.set('');
    this.dialog.set('delete');
  }

  openInvite(user: UserListItem): void {
    this.selected.set(user);
    this.welcomeMessage = '';
    this.errorMessage.set('');
    this.dialog.set('invite');
  }

  closeDialog(): void {
    this.dialog.set('none');
    this.selected.set(null);
    this.detail.set(null);
    this.reason = '';
    this.deleteConfirmation.set('');
    this.welcomeMessage = '';
    this.errorMessage.set('');
  }

  private loadDetail(id: string, then?: (detail: UserDetail) => void): void {
    this.detailLoading.set(true);
    this.detail.set(null);

    this.api.getUser(id).subscribe({
      next: (detail) => {
        this.detailLoading.set(false);
        this.detail.set(detail);
        then?.(detail);
      },
      error: (error: Error) => {
        this.detailLoading.set(false);
        this.errorMessage.set(error.message);
      },
    });
  }

  // =========================================================================================
  // Actions
  // =========================================================================================

  saveEdit(): void {
    const detail = this.detail();
    const form = this.editForm();

    if (!detail) {
      return;
    }

    if (!form.firstName.trim() || !form.lastName.trim() || !form.displayName.trim()) {
      this.errorMessage.set('First name, last name and display name are all required.');
      return;
    }

    if (this.reason.trim().length < 10) {
      this.errorMessage.set('Give a reason of at least 10 characters. It is recorded in the audit trail.');
      return;
    }

    this.busy.set(true);
    this.errorMessage.set('');

    this.api
      .updateUser((detail.id ?? ''), {
        firstName: form.firstName.trim(),
        middleName: form.middleName || null,
        lastName: form.lastName.trim(),
        displayName: form.displayName.trim(),
        mobileCountryCode: form.mobileCountryCode || null,
        mobileNumber: form.mobileNumber || null,
        employeeNumber: form.employeeNumber || null,
        // Sent back unchanged: this dialog does not move people between units or departments.
        // Doing that needs its own approval, which is why neither is a field on this screen.
        organisationUnitId: detail.organisationUnitId ?? null,
        departmentId: detail.departmentId ?? null,
        designation: form.designation || null,
        managerUserId: detail.managerUserId ?? null,
        preferredCulture: form.preferredLanguage,
        timeZone: form.timeZoneId,
        reason: this.reason.trim(),
        // Carrying the version back is what lets the server refuse the write if somebody else
        // saved in the meantime, rather than silently discarding their change.
        expectedVersion: (detail.version ?? 0),
      })
      .subscribe({
        next: () => this.finish(`${form.displayName.trim()} was updated.`),
        error: (error: Error) => this.fail(error),
      });
  }

  confirmSuspend(): void {
    const user = this.selected();

    if (!user) {
      return;
    }

    if (this.reason.trim().length < 10) {
      this.errorMessage.set('Give a reason of at least 10 characters. It is recorded in the audit trail.');
      return;
    }

    this.busy.set(true);
    this.errorMessage.set('');

    this.api.suspendUser((user.id ?? ''), { reason: this.reason.trim(), expectedVersion: (user.version ?? 0) }).subscribe({
      next: () => this.finish(`${user.displayName ?? 'That person'} has been suspended and signed out of every device.`),
      error: (error: Error) => this.fail(error),
    });
  }

  confirmReactivate(): void {
    const user = this.selected();

    if (!user) {
      return;
    }

    if (this.reason.trim().length < 10) {
      this.errorMessage.set('Give a reason of at least 10 characters. It is recorded in the audit trail.');
      return;
    }

    this.busy.set(true);
    this.errorMessage.set('');

    this.api.reactivateUser((user.id ?? ''), { reason: this.reason.trim(), expectedVersion: (user.version ?? 0) }).subscribe({
      next: () => this.finish(`${user.displayName ?? 'That person'} can sign in again.`),
      error: (error: Error) => this.fail(error),
    });
  }

  /**
   * True when the invitation has not been accepted, so withdrawing applies rather than
   * deactivating.
   *
   * The user's own status carries this: `invited` means an outstanding invitation and an account
   * nobody has ever used. Withdrawing kills the link as well as the account, which deactivating
   * would not.
   */
  isPendingInvite(user: UserListItem): boolean {
    return user.status === 'invited';
  }

  confirmDelete(): void {
    const user = this.selected();

    if (!user || !this.canConfirmDelete()) {
      return;
    }

    if (this.reason.trim().length < 10) {
      this.errorMessage.set('Give a reason of at least 10 characters. It is recorded in the audit trail.');
      return;
    }

    this.busy.set(true);
    this.errorMessage.set('');

    const request = { reason: this.reason.trim(), expectedVersion: (user.version ?? 0) };

    // What "delete" means depends on how far the account has been used:
    //   • a draft never invited        → erased with delete-unused-draft
    //   • an invitation still open     → withdrawn with withdraw-invitation (that is what the
    //                                     server's "Withdraw invitation" permission allows here)
    //   • an account with history      → deactivated so the audit trail survives
    // The server enforces the same rules, so this wording is about behaviour, not permission.
    //
    // The calls return different payloads (OutcomeResponse and UserDetail), and none is used
    // here — the list is re-read from the server afterwards either way. Widening to
    // Observable<unknown> lets one subscribe cover all three without inventing a shared type.
    // A draft and an unaccepted invitation are both WITHDRAWN: nobody ever signed in as
    // either, so there is no history to keep and the invitation link has to stop working.
    // Anything else is deactivated, never deleted, because a person's past actions must stay
    // attributable to somebody.
    const call: Observable<unknown> = this.isHardDelete() || this.isPendingInvite(user)
      ? this.api.withdrawUser((user.id ?? ''), request)
      : this.api.deactivateUser((user.id ?? ''), request);

    call.subscribe({
      next: () =>
        this.finish(
          this.isHardDelete()
            ? `The draft for ${user.displayName ?? 'that person'} was withdrawn.`
            : this.isPendingInvite(user)
              ? `The invitation for ${user.displayName ?? 'That person'} was withdrawn.`
              : `${user.displayName ?? 'That person'} was deactivated. The record and its history are retained.`,
        ),
      error: (error: Error) => this.fail(error),
    });
  }

  confirmInvite(): void {
    const user = this.selected();

    if (!user) {
      return;
    }

    this.busy.set(true);
    this.errorMessage.set('');

    this.api.resendInvitation((user.id ?? ''), this.welcomeMessage.trim() || undefined).subscribe({
      next: (outcome) => this.finish(outcome.message ?? 'The invitation has been re-sent.'),
      error: (error: Error) => this.fail(error),
    });
  }

  // =========================================================================================
  // Export
  // =========================================================================================

  exportCsv(): void {
    this.busy.set(true);

    this.api.exportDirectory(this.filter()).subscribe({
      next: (blob) => {
        this.busy.set(false);

        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `ydot-user-directory-${new Date().toISOString().slice(0, 10)}.csv`;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);

        this.toast.show('Export ready', 'The directory was downloaded as CSV.', 'success');
      },
      error: (error: Error) => {
        this.busy.set(false);
        this.toast.show('Export failed', error.message, 'error');
      },
    });
  }

  // =========================================================================================
  // Presentation helpers
  // =========================================================================================

  /**
   * The badge colour for a status.
   *
   * Keyed off the stored value rather than the display text: `statusDisplay` is what a person
   * reads and is free to be reworded, and a badge that turned grey because somebody changed
   * "Active" to "In use" would be a very quiet bug.
   */
  statusClass(status: UserStatus | undefined): string {
    switch (status) {
      case 'active': return 'ud-badge-good';
      case 'suspended': return 'ud-badge-danger';
      case 'invited': return 'ud-badge-blue';
      case 'draft': return 'ud-badge-muted';
      case 'expired':
      case 'withdrawn':
      case 'deactivated': return 'ud-badge-muted';
      default: return 'ud-badge-muted';
    }
  }

  /** Enrolled once a second factor has been confirmed. */
  mfaClass(enrolled: boolean | undefined): string {
    return enrolled ? 'ud-badge-good' : 'ud-badge-muted';
  }

  mfaLabel(enrolled: boolean | undefined): string {
    return enrolled ? 'Enrolled' : 'Not enrolled';
  }

  /**
   * The roles somebody holds, as one line.
   *
   * Joined here rather than in the template so the empty case reads as an answer — nobody has
   * given this person a role yet — rather than as a blank cell that could equally mean the
   * column failed to load.
   */
  roleSummary(user: UserListItem): string {
    const roles = user.roleNames ?? [];
    return roles.length > 0 ? roles.join(', ') : 'No roles';
  }

  /**
   * Whether an invitation can be sent to this row.
   *
   * A draft has never been invited; an invited account has an outstanding link that can be sent
   * again; an expired one needs a fresh link. An active account does not get invited — it is
   * already in use, and re-inviting would be a password reset by another name.
   */
  canInvite(user: UserListItem): boolean {
    return user.status === 'draft' || user.status === 'invited' || user.status === 'expired';
  }

  /** What the profile panel says about an outstanding invitation. */
  invitationLabel(detail: UserDetail): string {
    if (detail.hasPendingInvitation) {
      return detail.invitationExpiresAtUtc
        ? `Outstanding — expires ${new Date(detail.invitationExpiresAtUtc).toLocaleDateString()}`
        : 'Outstanding';
    }

    return detail.status === 'draft' ? 'Not sent' : 'Accepted';
  }

  /** How somebody signs in with a second factor, in the words the screen uses. */
  mfaRequirementLabel(detail: UserDetail): string {
    if (!detail.mfaEnabled) {
      return detail.mfaRequirement === 'required'
        ? 'Required — not yet enrolled'
        : 'Not enrolled';
    }

    return detail.mfaRequirement === 'required' ? 'Enrolled — required' : 'Enrolled';
  }

  initials(name: string | null | undefined): string {
    return (name ?? '')
      .split(' ')
      .filter(Boolean)
      .map((part) => part[0])
      .join('')
      .toUpperCase()
      .slice(0, 2) || '?';
  }

  copy(text: string | null | undefined, field: string): void {
    if (!text) {
      return;
    }

    void navigator.clipboard.writeText(text).then(() => {
      this.copiedField.set(field);
      setTimeout(() => this.copiedField.set(''), 2000);
    });
  }

  updateEditField(key: keyof ReturnType<typeof this.editForm>, value: string): void {
    this.editForm.update((current) => ({ ...current, [key]: value }));
  }

  // =========================================================================================
  // Internals
  // =========================================================================================

  /** Re-reads the list after a write, so the screen shows what is stored, not what was assumed. */
  private finish(message: string): void {
    this.busy.set(false);
    this.closeDialog();
    this.toast.show('Done', message, 'success');
    this.load();
  }

  private fail(error: Error): void {
    this.busy.set(false);
    this.errorMessage.set(error.message);
  }
}
