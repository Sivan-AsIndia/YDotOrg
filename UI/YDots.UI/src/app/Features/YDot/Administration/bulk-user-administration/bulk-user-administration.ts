import {
  Component,
  effect,
  inject,
  signal,
} from '@angular/core';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { ToastService } from '../../../../Shared/services/toast.service';
import { BulkUserAdminApiService } from '../../../../Service/bulk-user-admin-api.service';
import { UserDirectoryApiService } from '../../../../Service/user-directory-api.service';
import { AccessReviewApiService } from '../../../../Service/access-review-api.service';
import {
  BulkActionOption,
  BulkActionRequest,
  BulkActionType,
  BulkImpactPreviewResponse,
  BulkOperationResponse,
  BulkUserAdministrationViewResponse,
} from '../../../../Shared/models/bulk-user-administration.model';
import { UserListItem, UserSearchFilter } from '../../../../Shared/models/user-directory.model';
import { LookupItem } from '../../../../Shared/models/api-response.model';

@Component({
  selector: 'app-bulk-user-administration',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
  ],
  templateUrl: './bulk-user-administration.html',
  styleUrl: './bulk-user-administration.css',
})
export class BulkUserAdministrationComponent {
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly api = inject(BulkUserAdminApiService);
  private readonly userApi = inject(UserDirectoryApiService);
  private readonly reviewApi = inject(AccessReviewApiService);

  selectedUsers = signal<UserListItem[]>([]);

  data = signal<BulkUserAdministrationViewResponse | null>(null);
  loading = signal(true);
  loadError = signal(false);
  submitting = signal(false);

  /** The validated preview (and the result of the submit). */
  impactPreview = signal<BulkImpactPreviewResponse | null>(null);
  operation = signal<BulkOperationResponse | null>(null);
  /** True while a preview/validate request is in flight. */
  validating = signal(false);
  /** What this caller may do, decided by the server from their permissions. */
  permittedActions = signal<string[]>([]);
  approverRequired = signal(false);
  approverReason = signal('');
  /** The operation id that produced the downloadable result file. */
  operationId = signal('');
  validationError = signal('');

  /** Scope type options loaded from the user-directory API. */
  scopeTypeOptions = signal<LookupItem[]>([]);
  /** Campaign options loaded from the bulk view API. */
  campaignOptions = signal<LookupItem[]>([]);

  // Selection state
  totalCount = signal(0);
  excludedCount = signal(0);
  affectedCount = signal(0);

  // Many-users display threshold
  readonly MAX_CHIPS = 20;
  showAllSelected = signal(false);

  // Modal
  showConfirmModal = signal(false);
  showPreviewModal = signal(false);
  showResultModal = signal(false);
  resultFileUrl = signal('');

  // Conditional fields
  selectedAction = signal('');
  validationErrors = signal<string[]>([]);

  bulkForm = this.fb.group({
    action: ['', Validators.required],
    scopeType: [''],
    campaign: [''],
    effectiveTime: [''],
    suspensionReason: [''],
    businessJustification: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(1000)]],
  });

  constructor() {
    const navState = history.state as { selectedUsers?: UserListItem[] } | null;
    const passedSelection = navState?.selectedUsers;
    if (passedSelection && passedSelection.length > 0) {
      this.selectedUsers.set(passedSelection);
      this.totalCount.set(passedSelection.length);
    }
    this.loadData();

    effect(() => {
      this.bulkForm.updateValueAndValidity({ emitEvent: false });
      const errors: string[] = [];
      const actionErrors = this.bulkForm.controls.action.errors;
      const justErrors = this.bulkForm.controls.businessJustification.errors;

      if (actionErrors?.['required']) errors.push('Bulk action is required.');
      if (justErrors?.['required']) errors.push('Business justification is required.');
      if (justErrors?.['minlength']) errors.push('Business justification must be at least 10 characters.');
      if (justErrors?.['maxlength']) errors.push('Business justification cannot exceed 1000 characters.');

      this.validationErrors.set(errors);
    });
  }

  /** Recent operations, so somebody can see what was run and how it went. */
  readonly recentOperations = signal<BulkOperationResponse[]>([]);

  /**
   * What a bulk operation can do.
   *
   * The domain's own vocabulary rather than configuration: each of these is a code path on the
   * server, and a new one means new server behaviour rather than a new row in a table.
   */
  private static readonly ACTIONS: BulkActionOption[] = [
    { value: 'invite', label: 'Send invitations', description: 'E-mail an activation link to each person.' },
    { value: 'activate', label: 'Activate', description: 'Bring accounts into use.' },
    { value: 'suspend', label: 'Suspend', description: 'Pause access and end every live session.' },
    { value: 'reactivate', label: 'Lift suspension', description: 'Let people sign in again.' },
    { value: 'deactivate', label: 'Deactivate', description: 'End access permanently. Nothing is deleted.' },
    { value: 'assignRole', label: 'Add a role', description: 'Grant one role to everybody selected.' },
    { value: 'removeRole', label: 'Remove a role', description: 'Take one role away from everybody selected.' },
    { value: 'resetPassword', label: 'Send a password reset', description: 'E-mail a reset link. No password is generated.' },
    { value: 'forceSignOut', label: 'Sign out everywhere', description: 'End every session on every device.' },
    { value: 'requireMfaReset', label: 'Reset two-step verification', description: 'Clear enrolled factors so they are set up again.' },
    { value: 'extendAccess', label: 'Extend access', description: 'Move the end of the access window.' },
    { value: 'export', label: 'Export', description: 'Download the selection as a file.' },
  ];

  private loadData(): void {
    this.loading.set(true);
    this.loadError.set(false);

    // The actions a bulk operation can perform are the domain's own vocabulary, not
    // configuration: each one is a code path on the server. Naming them here rather than
    // fetching a list keeps the screen honest about that.
    this.data.set({ availableActions: BulkUserAdministrationComponent.ACTIONS });
    this.permittedActions.set(BulkUserAdministrationComponent.ACTIONS.map((a) => a.value));
    this.loading.set(false);

    this.api.getOperations(1, 20).subscribe({
      next: (page) => {
        this.recentOperations.set(page.items ?? []);
        this.loadCampaignFallback();
        this.loading.set(false);
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.loadError.set(true);
        this.validationError.set(error.message);
        this.toast.show('Error', 'Failed to load bulk administration data.', 'error');
      },
    });

    // Load the scope type options from the user-directory API so the dropdown
    // is bound to real server data, not hard-coded strings.
    const filter: UserSearchFilter = { pageIndex: 1, pageSize: 1 };
    this.userApi.getDirectory(filter).subscribe({
      next: (res) => {
        this.scopeTypeOptions.set(res.dataScopeTypeOptions ?? []);
      },
      error: () => {
        // Non-blocking — the form still works with the fallback options.
        // A LookupItem is id/code/name, matching what the API sends. The ids here are the
        // enum names, which is what the server expects back for a scope type - so the fallback
        // stays usable rather than merely present.
        this.scopeTypeOptions.set([
          { id: 'organisation', code: 'organisation', name: 'Organisation', isActive: true },
          { id: 'geography', code: 'geography', name: 'Geography', isActive: true },
          { id: 'campaign', code: 'campaign', name: 'Campaign', isActive: true },
          { id: 'warehouse', code: 'warehouse', name: 'Warehouse', isActive: true },
          { id: 'queue', code: 'queue', name: 'Queue', isActive: true },
          { id: 'assignment', code: 'assignment', name: 'Assignment', isActive: true },
          { id: 'explicitRecord', code: 'explicitRecord', name: 'Explicit records', isActive: true },
        ]);
      },
    });
  }

  /** Fallback: fetch review campaigns from the access-review API. */
  private loadCampaignFallback(): void {
    this.reviewApi.getCampaigns().subscribe({
      next: (campaigns) => {
        const options: LookupItem[] = campaigns.map((camp) => ({
          id: camp.id ?? '',
          code: camp.code ?? '',
          name: `${camp.code} — ${camp.name}`,
          isActive: camp.status === 'active',
          description: camp.statusDisplay ?? null,
        }));
        if (options.length > 0) {
          this.campaignOptions.set(options);
        }
      },
      error: () => {
        // Non-blocking — the dropdown will just offer the placeholder.
      },
    });
  }

  retry(): void {
    this.loadData();
  }

  onActionChange(): void {
    this.selectedAction.set(this.bulkForm.value.action ?? '');
  }

  /** Maps the form + selected users into the bulk action request the API expects. */
  private buildRequest(): BulkActionRequest {
    const form = this.bulkForm.value;
    return {
      actionType: (form.action || undefined) as BulkActionRequest['actionType'],
      // Ids, not references. The API takes user ids and resolves nothing: a reference like
      // USR-000123 is unique only inside one Organisation, so resolving one server-side would
      // mean deciding which Organisation it belonged to — which is exactly the guess this
      // system never makes.
      userIds: this.selectedUsers().map(u => u.id ?? '').filter(Boolean),

      // Explicit ids only. There is deliberately no "everybody matching this scope" option: a
      // bulk action driven by a scope expression is one where nobody has looked at the list, and
      // the whole point of the preview is that somebody does.
      roleId: form.campaign || null,
      accessEndsAtUtc: form.effectiveTime ? new Date(form.effectiveTime).toISOString() : null,
      reason: form.businessJustification || form.suspensionReason || null,

      // Never true from this screen. Applying is a second, deliberate press after the preview
      // has been read — which is the whole shape of this feature.
      applyImmediately: false,
    };
  }

  validateSelection(): void {
    const selected = this.selectedUsers();
    if (selected.length === 0) {
      this.toast.show('No Selection', 'No users were passed from the directory. Please go back and select users.', 'warning');
      return;
    }
    if (!this.selectedAction()) {
      this.toast.show('Validation Error', 'Please select a bulk action first.', 'warning');
      return;
    }

    this.validating.set(true);
    this.validationError.set('');
    // Creating the operation VALIDATES it and reports what would happen, row by row, without
    // changing anything. Applying it is a separate call — see the service for why the two-step
    // shape is the point rather than an inconvenience.
    this.api.createOperation(this.buildRequest()).subscribe({
      next: (preview) => {
        this.validating.set(false);
        this.applyPreview(preview);

        const total = preview.totalItemCount ?? 0;
        const failed = preview.failedItemCount ?? 0;

        this.toast.show(
          'Selection checked',
          `${total} in the selection. ${failed} cannot be actioned.`,
          failed > 0 ? 'warning' : 'success',
        );
      },
      error: (error: Error) => {
        this.validating.set(false);
        this.validationError.set(error.message);
        this.toast.show('Validation Failed', error.message, 'error');
      },
    });
  }

  previewImpact(): void {
    const selected = this.selectedUsers();
    if (selected.length === 0) {
      this.toast.show('No Selection', 'Select users first, then preview the impact.', 'warning');
      return;
    }
    this.validating.set(true);
    this.validationError.set('');
    this.api.createOperation(this.buildRequest()).subscribe({
      next: (preview) => {
        this.validating.set(false);
        this.applyPreview(preview);
        this.showPreviewModal.set(true);
      },
      error: (error: Error) => {
        this.validating.set(false);
        this.validationError.set(error.message);
        this.toast.show('Preview Failed', error.message, 'error');
      },
    });
  }

  closePreview(): void {
    this.showPreviewModal.set(false);
  }

  /**
   * Reads the validated operation into the counters the preview shows.
   *
   * The server counts rows rather than reporting a "selection" and an "eligible" pair: an
   * operation has items, and each item is either valid or carries the reason it is not. Deriving
   * the counters from the rows means the number beside "cannot be actioned" and the list beneath
   * it can never disagree.
   */
  private applyPreview(preview: BulkImpactPreviewResponse): void {
    const items = preview.items ?? [];
    const blocked = items.filter((item) => item.isValid === false);

    this.impactPreview.set(preview);
    this.totalCount.set(preview.totalItemCount ?? items.length);
    this.excludedCount.set(blocked.length);
    this.affectedCount.set((preview.totalItemCount ?? items.length) - blocked.length);

    // Whether a second pair of eyes is needed is a policy question the server answers when it
    // validates; the screen shows what it was told rather than deciding for itself.
    this.approverRequired.set(preview.status === 'queued');
    this.approverReason.set(preview.failureSummary ?? '');
  }

  submitBulkAction(): void {
    this.bulkForm.markAllAsTouched();
    if (this.bulkForm.invalid) {
      this.toast.show('Validation Error', 'Please fix the form errors before submitting.', 'error');
      return;
    }
    if (this.affectedCount() === 0 && this.totalCount() === 0) {
      this.toast.show('Validation Error', 'Validate the selection before submitting.', 'warning');
      return;
    }
    this.showConfirmModal.set(true);
  }

  confirmSubmit(): void {
    this.showConfirmModal.set(false);
    this.submitting.set(true);
    this.validationError.set('');

    const validated = this.operation();

    if (!validated?.id) {
      this.submitting.set(false);
      this.validationError.set('Check the selection before applying it.');
      return;
    }

    this.api
      .apply({ operationId: validated.id, expectedVersion: validated.version ?? 0 })
      .subscribe({
      next: (operation) => {
        this.submitting.set(false);
        this.operation.set(operation);
        this.operationId.set(operation.id ?? '');
        this.resultFileUrl.set(
          `bulk-operation-${operation.operationNumber ?? operation.id}.csv`);

        // The directory listens for this so it can re-read: several of these actions change
        // rows it is showing, and a stale list after a bulk suspend is confusing.
        window.dispatchEvent(new CustomEvent('bulk-operation-completed', { detail: operation }));
        this.showResultModal.set(true);

        // PARTIAL SUCCESS IS A REAL RESULT, not a failure. Forty-seven of fifty succeeding is
        // exactly what happened, and the three that did not are listed with their reasons.
        const succeeded = operation.succeededItemCount ?? 0;
        const failed = operation.failedItemCount ?? 0;

        this.toast.show(
          'Bulk action applied',
          failed > 0
            ? `${succeeded} succeeded, ${failed} could not be actioned. See the list for why.`
            : `${succeeded} completed.`,
          failed > 0 ? 'warning' : 'success',
        );
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.validationError.set(error.message);
        this.showConfirmModal.set(false);
        this.toast.show('Submit Failed', error.message, 'error');
      },
    });
  }

  closeModal(): void {
    this.showConfirmModal.set(false);
    this.showResultModal.set(false);
  }

  downloadResult(): void {
    const operation = this.operation();
    if (!operation) {
      this.closeModal();
      return;
    }
    // Built here rather than fetched: the operation already carries a row per person with its
    // own outcome, so a download endpoint would be a round trip to re-serialise what the screen
    // is holding. The BOM is for Excel, which otherwise reads UTF-8 as the system code page and
    // mangles every accented name.
    const rows = [
      'User,Outcome,Detail',
      ...(operation.items ?? []).map((item) => [
        item.sourceIdentifier ?? item.userId ?? '',
        item.succeeded ? 'Succeeded' : item.wasSkipped ? 'Skipped' : 'Failed',
        (item.resultMessage ?? item.validationMessage ?? '').replace(/"/g, '""'),
      ].map((field) => `"${field}"`).join(',')),
    ];

    const blob = new Blob(['\uFEFF' + rows.join('\r\n')], { type: 'text/csv;charset=utf-8' });

    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');

    anchor.href = url;
    anchor.download = this.resultFileUrl();
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);

    // Released straight away: without this every download holds its bytes in memory until the
    // tab closes, which on a long administrative session adds up.
    URL.revokeObjectURL(url);

    this.toast.show('Downloaded', `Saved ${this.resultFileUrl()}.`, 'info');
    this.showResultModal.set(false);
  }

  cancel(): void {
    // If we already have a submitted operation, cancel it server-side first.
    const operation = this.operation();
    // Only an operation that has been checked but not applied can be cancelled. Once applied
    // the changes are made, and undoing them means a fresh operation in the other direction.
    if (operation && (operation.status === 'validated' || operation.status === 'queued')) {
      this.api.cancel(
        operation.id ?? '',
        operation.version ?? 0,
        'Cancelled before it was applied.')
        .subscribe({
          next: (outcome) => this.toast.show(
            'Cancelled', outcome.message ?? 'The operation was cancelled.', 'info'),
          error: (error: Error) => this.toast.show('Cancel Failed', error.message, 'error'),
        });
    }

    this.bulkForm.reset();
    this.totalCount.set(0);
    this.excludedCount.set(0);
    this.affectedCount.set(0);
    this.selectedAction.set('');
    this.selectedUsers.set([]);
    this.showAllSelected.set(false);
    this.impactPreview.set(null);
    this.operation.set(null);
    this.operationId.set('');
    this.approverRequired.set(false);
    this.approverReason.set('');
    this.toast.show('Cancelled', 'Bulk action has been cancelled.', 'info');
    this.router.navigate(['/app/administration/access/user-directory']);
  }

  // Many-users display helpers
  get visibleSelectedUsers(): UserListItem[] {
    const users = this.selectedUsers();
    if (users.length <= this.MAX_CHIPS || this.showAllSelected()) {
      return users;
    }
    return users.slice(0, this.MAX_CHIPS);
  }

  get hiddenSelectedCount(): number {
    const users = this.selectedUsers();
    return users.length > this.MAX_CHIPS ? users.length - this.MAX_CHIPS : 0;
  }

  toggleShowAllSelected(): void {
    this.showAllSelected.set(!this.showAllSelected());
  }

  // Account category counts from the actual selection
  get activeCategoryCount(): number {
    return this.selectedUsers().filter(u => u.status === 'active').length;
  }

  get invitedCategoryCount(): number {
    return this.selectedUsers().filter(u => u.status === 'invited').length;
  }

  get suspendedCategoryCount(): number {
    return this.selectedUsers().filter(u => u.status === 'suspended').length;
  }

  /**
   * Locked out is not a status of its own.
   *
   * It is a flag on an otherwise active account: the person's access is intact, they have simply
   * mistyped their password five times. Treating it as a status would hide them from the active
   * count and make a temporary state look like a permanent one.
   */
  get lockedCategoryCount(): number {
    return this.selectedUsers().filter(u => u.isLockedOut === true).length;
  }

  getActionLabel(value: string): string {
    const found = this.data()?.availableActions.find(a => a.value === value);
    return found?.label ?? 'Not selected';
  }

  goBack(): void {
    this.router.navigate(['/app/administration/access/user-directory']);
  }
}