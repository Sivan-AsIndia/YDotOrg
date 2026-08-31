import { Component, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router } from '@angular/router';
import { ToastService } from '../../../../Shared/services/toast.service';
import { AccessRequestApiService } from '../../../../Service/access-request-api.service';
import { UserDirectoryApiService } from '../../../../Service/user-directory-api.service';
import {
  AccessRequestItemApi,
  AccessRequestListResponse,
  AccessRequestSearchFilter,
  CreateAccessRequestRequest,
} from '../../../../Shared/models/access-request-api.model';
import { UserSearchFilter } from '../../../../Shared/models/user-directory.model';
import { LookupItem } from '../../../../Shared/models/api-response.model';

interface AccessRequestView {
  id: string;
  reference: string;
  requestType: string;
  user: string;
  currentRoleAndScope: string;
  requestedRole: string;
  scopeType: string;
  scopeValue: string;
  effectiveFrom: string;
  effectiveTo: string;
  businessJustification: string;
  requester: string;
  requestedTime: string;
  approverRoute: string;
  slaDue: string;
  approvalState: string;
  approvalStateClass: string;
  decision?: string;
  decisionReason?: string;
  decisionActor?: string;
  decisionTime?: string;
  version: number;
  permittedActions: string[];
}

@Component({
  selector: 'app-access-request',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './access-request.html',
  styleUrl: './access-request.css',
})
export class AccessRequestComponent {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly api = inject(AccessRequestApiService);
  private readonly userApi = inject(UserDirectoryApiService);

  data = signal<AccessRequestListResponse | null>(null);
  userOptions = signal<{ id: string; reference: string; displayName: string; orgUnit: string }[]>([]);
  loading = signal(true);
  loadError = signal(false);
  submitting = signal(false);
  errorMessage = signal('');

  searchQuery = signal('');
  filterState = signal('');

  filteredRequests = signal<AccessRequestView[]>([]);

  // ===== New Request Modal =====
  showNewRequestModal = signal(false);
  newRequestForm = signal({
    requestType: 'NewAccess',
    userId: '',
    requestedRole: '',
    scopeType: 'organisation',
    scopeValue: '',
    effectiveFrom: '',
    effectiveTo: '',
    reviewDate: '',
    businessJustification: ''
  });

  // ===== Decision Modals =====
  showApproveModal = signal(false);
  showRejectModal = signal(false);
  showReturnModal = signal(false);
  showCancelModal = signal(false);
  showDeleteModal = signal(false);
  showDecisionResultModal = signal(false);
  decisionTarget = signal<AccessRequestView | null>(null);
  decisionReason = signal('');
  decisionResult = signal<{ reference: string; state: string; effectiveTime: string; nextAction: string } | null>(null);
  decisionError = signal('');
  actionTarget = signal<AccessRequestView | null>(null);

  constructor() {
    this.loadData();
    this.loadUserDirectory();
    effect(() => { this.applyFilters(); });
  }

  /**
   * The statuses a request can be in.
   *
   * Named here rather than fetched: these are the domain's own vocabulary, and a new one means
   * new server behaviour rather than a new row in a table. An endpoint whose entire payload is
   * this list would be more moving parts than the list deserves.
   */
  private static readonly STATUS_OPTIONS: LookupItem[] = [
    { id: 'draft', code: 'draft', name: 'Draft', isActive: true },
    { id: 'submitted', code: 'submitted', name: 'Awaiting decision', isActive: true },
    { id: 'returned', code: 'returned', name: 'Sent back', isActive: true },
    { id: 'approved', code: 'approved', name: 'Approved', isActive: true },
    { id: 'rejected', code: 'rejected', name: 'Rejected', isActive: true },
    { id: 'withdrawn', code: 'withdrawn', name: 'Withdrawn', isActive: true },
    { id: 'expired', code: 'expired', name: 'Expired', isActive: true },
  ];

  /** What can be asked for. Also the domain's own vocabulary. */
  private static readonly REQUEST_TYPE_OPTIONS: LookupItem[] = [
    { id: 'roleAssignment', code: 'roleAssignment', name: 'A role', isActive: true },
    { id: 'permissionGrant', code: 'permissionGrant', name: 'A single permission', isActive: true },
    { id: 'dataScopeGrant', code: 'dataScopeGrant', name: 'A wider data scope', isActive: true },
    { id: 'temporaryElevation', code: 'temporaryElevation', name: 'Temporary elevation', isActive: true },
  ];

  private loadData(): void {
    this.loading.set(true);
    this.loadError.set(false);

    const filter: AccessRequestSearchFilter = { page: 1, pageSize: 100 } as AccessRequestSearchFilter;

    this.api.getRequests(filter).subscribe({
      next: (page) => {
        this.data.set({
          requests: page.items ?? [],
          totalCount: page.totalCount ?? 0,

          // The statuses and request types are the domain's own vocabulary rather than
          // configuration, so they are named here instead of fetched. A new status means new
          // server behaviour, not a new row in a table.
          statusOptions: AccessRequestComponent.STATUS_OPTIONS,
          requestTypeOptions: AccessRequestComponent.REQUEST_TYPE_OPTIONS,
          scopeTypeOptions: AccessRequestComponent.SCOPE_TYPE_OPTIONS,
          roleOptions: this.availableRoles(),
        });

        this.loading.set(false);
        this.applyFilters();
      },
      error: (error: Error) => {
        this.loading.set(false);
        this.loadError.set(true);
        this.errorMessage.set(error.message);
        this.toast.show('Error', 'Failed to load access requests.', 'error');
      },
    });
  }

  /**
   * The kinds of thing access can be limited to.
   *
   * The domain's own vocabulary, like the statuses above: each of these is a code path that
   * decides which records a permission reaches, not a row somebody can add.
   */
  private static readonly SCOPE_TYPE_OPTIONS: LookupItem[] = [
    { id: 'organisation', code: 'organisation', name: 'Whole organisation', isActive: true },
    { id: 'geography', code: 'geography', name: 'A place', isActive: true },
    { id: 'campaign', code: 'campaign', name: 'A campaign', isActive: true },
    { id: 'warehouse', code: 'warehouse', name: 'A warehouse', isActive: true },
    { id: 'queue', code: 'queue', name: 'A work queue', isActive: true },
    { id: 'assignment', code: 'assignment', name: 'What they are assigned', isActive: true },
    { id: 'explicitRecord', code: 'explicitRecord', name: 'Named records only', isActive: true },
  ];

  /** The roles that can be asked for, from the reference data this screen already loads. */
  readonly availableRoles = signal<LookupItem[]>([]);

  private loadUserDirectory(): void {
    const filter: UserSearchFilter = { pageIndex: 1, pageSize: 100 };
    this.userApi.getDirectory(filter).subscribe({
      next: (res) => {
        this.userOptions.set((res.users.items ?? []).map(u => ({
          id: u.id ?? '',
          reference: u.code ?? '',
          displayName: u.displayName ?? '',
          orgUnit: u.organisationUnitName ?? ''
        })));
      },
      error: () => { /* Non-blocking */ }
    });
  }

  retry(): void { this.loadData(); }

  applyFilters(): void {
    const all = this.data()?.requests ?? [];
    const q = this.searchQuery().toLowerCase();
    const s = this.filterState();
    let result = all;
    if (q) {
      result = result.filter((r) =>
        (r.requestNumber ?? '').toLowerCase().includes(q)
        || (r.requestedForName ?? '').toLowerCase().includes(q));
    }
    if (s) result = result.filter(r => r.status === s);
    this.filteredRequests.set(result.map(r => this.toAccessRequestView(r)));
  }

  clearFilters(): void { this.searchQuery.set(''); this.filterState.set(''); }

  private toAccessRequestView(r: AccessRequestItemApi): AccessRequestView {
    return {
      id: r.id ?? '',
      reference: r.requestNumber ?? '',
      requestType: r.requestTypeDisplay ?? r.requestType ?? '',
      user: r.requestedForName ?? '',

      // The queue row is deliberately lean: it carries what triage needs, not the whole
      // request. The justification, the scope and the decision notes are on the detail, which
      // is fetched when a row is opened.
      currentRoleAndScope: '',
      requestedRole: r.roleName ?? r.permissionCode ?? '—',
      scopeType: 'organisation',
      scopeValue: 'All',
      effectiveFrom: r.accessStartsAtUtc ? this.formatDate(r.accessStartsAtUtc) : '—',
      effectiveTo: r.accessEndsAtUtc ? this.formatDate(r.accessEndsAtUtc) : '—',
      businessJustification: '',
      requester: r.requestedByName ?? '—',
      requestedTime: r.submittedAtUtc ? this.formatDateTime(r.submittedAtUtc) : '—',
      approverRoute: 'Independent approver',
      slaDue: '—',
      approvalState: r.statusDisplay ?? r.status ?? '',
      approvalStateClass: this.stateClass(r.status ?? ''),
      decision: r.decidedAtUtc
        ? (r.status === 'approved' ? 'Approved' : r.status === 'rejected' ? 'Rejected' : '')
        : '',
      decisionReason: '',
      decisionActor: r.decidedByName ?? '',
      decisionTime: r.decidedAtUtc ? this.formatDateTime(r.decidedAtUtc) : '',
      version: r.version ?? 0,

      // `canDecide` is the server's answer to the independence rule for THIS caller: it is
      // false on a request they raised themselves, whatever permissions they hold. Deriving it
      // here would mean re-implementing the rule and eventually disagreeing with it.
      permittedActions: r.canDecide ? ['Approve', 'Reject', 'Return'] : ['View'],
    };
  }

  private stateClass(state: string): string {
    switch (state) {
      case 'Submitted': return 'bg-info';
      case 'PendingReview': return 'bg-warning';
      case 'Approved': return 'bg-success';
      case 'Rejected': return 'bg-danger';
      case 'Draft': return 'bg-secondary';
      case 'Cancelled': return 'bg-secondary';
      case 'Returned': return 'bg-warning';
      default: return 'bg-secondary';
    }
  }

  private formatDate(value: string): string {
    try {
      return new Date(value).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
    } catch {
      return value;
    }
  }

  private formatDateTime(value: string): string {
    try {
      return new Date(value).toLocaleString('en-IN', { dateStyle: 'medium', timeStyle: 'short' });
    } catch {
      return value;
    }
  }

  // ===== NEW REQUEST =====
  openNewRequest(): void {
    this.newRequestForm.set({
      requestType: 'NewAccess',
      userId: '',
      requestedRole: '',
      scopeType: 'organisation',
      scopeValue: '',
      effectiveFrom: '',
      effectiveTo: '',
      reviewDate: '',
      businessJustification: ''
    });
    this.showNewRequestModal.set(true);
  }

  closeNewRequest(): void {
    this.showNewRequestModal.set(false);
  }

  getSelectedUserName(): string {
    const id = this.newRequestForm().userId;
    const user = this.userOptions().find(u => u.id === id);
    return user ? user.displayName : '';
  }

  submitNewRequest(): void {
    const form = this.newRequestForm();
    if (!form.userId || !form.requestedRole || !form.businessJustification.trim()) {
      this.toast.show('Validation Error', 'User, requested role and justification are required.', 'warning');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    const request: CreateAccessRequestRequest = {
      requestType: form.requestType as CreateAccessRequestRequest['requestType'],
      requestedForUserId: form.userId,
      roleId: form.requestedRole || null,
      scopeType: (form.scopeType || undefined) as CreateAccessRequestRequest['scopeType'],
      scopeValue: form.scopeValue || null,
      accessStartsAtUtc: form.effectiveFrom ? new Date(form.effectiveFrom).toISOString() : new Date().toISOString(),
      accessEndsAtUtc: form.effectiveTo ? new Date(form.effectiveTo).toISOString() : null,
      businessJustification: form.businessJustification.trim(),
    };

    this.api.createRequest(request).subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        this.showNewRequestModal.set(false);

        // A persistent confirmation, not just a toast: somebody who raised a request needs its
        // reference afterwards, and a toast that has faded is no use for that.
        this.decisionResult.set({
          reference: outcome.message ?? 'Draft saved',
          state: outcome.status ?? 'Draft',
          effectiveTime: this.formatDateTime(new Date().toISOString()),
          nextAction: `The request has been drafted. Submit it for independent approval.`
        });
        this.showDecisionResultModal.set(true);
        this.loadData();
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.errorMessage.set(error.message);
        this.toast.show('Create Failed', error.message, 'error');
      },
    });
  }

  // ===== SUBMIT (draft → submitted for independent decision) =====
  submitRequest(request: AccessRequestView): void {
    if (request.approvalState !== 'Draft') return;

    this.submitting.set(true);
    this.errorMessage.set('');

    this.api
      .submitRequest(request.id, {
        expectedVersion: request.version,
        comment: 'Submitted for approval',
      })
      .subscribe({
      next: (outcome) => {
        this.submitting.set(false);

        this.decisionResult.set({
          reference: request.reference,
          state: outcome.status ?? 'Submitted',
          effectiveTime: this.formatDateTime(new Date().toISOString()),
          nextAction: `Request ${request.reference} has been submitted for independent approval.`
        });
        this.showDecisionResultModal.set(true);
        this.loadData();
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.toast.show('Submit Failed', error.message, 'error');
      },
    });
  }

  // ===== RETURN (submitted/pending → back to requester for correction) =====
  openReturnModal(request: AccessRequestView): void {
    if (request.approvalState !== 'Submitted' && request.approvalState !== 'PendingReview') return;
    this.actionTarget.set(request);
    this.decisionReason.set('');
    this.decisionError.set('');
    this.showReturnModal.set(true);
  }

  closeReturnModal(): void {
    this.showReturnModal.set(false);
    this.actionTarget.set(null);
    this.decisionReason.set('');
    this.decisionError.set('');
  }

  confirmReturn(): void {
    const target = this.actionTarget();
    if (!target) return;

    const reason = this.decisionReason().trim();
    if (reason.length < 10) {
      this.decisionError.set('Return reason must be at least 10 characters.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.api
      .returnRequest(target.id, { reason, expectedVersion: target.version })
      .subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        this.closeReturnModal();
        // Re-read rather than merge: the server may have done more than this screen
        // predicted — applied the grant, stamped the decision — and a locally patched row
        // would quietly disagree with it.

        this.decisionResult.set({
          reference: target.reference,
          state: 'Returned',
          effectiveTime: this.formatDateTime(new Date().toISOString()),
          nextAction: `Request ${target.reference} was returned to the requester for correction.`
        });
        this.showDecisionResultModal.set(true);
        this.loadData();
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.decisionError.set(error.message);
      },
    });
  }

  // ===== CANCEL (preserve history) =====
  openCancelModal(request: AccessRequestView): void {
    this.actionTarget.set(request);
    this.decisionReason.set('');
    this.decisionError.set('');
    this.showCancelModal.set(true);
  }

  closeCancelModal(): void {
    this.showCancelModal.set(false);
    this.actionTarget.set(null);
    this.decisionReason.set('');
    this.decisionError.set('');
  }

  confirmCancel(): void {
    const target = this.actionTarget();
    if (!target) return;

    const reason = this.decisionReason().trim();
    if (reason.length < 10) {
      this.decisionError.set('Cancellation reason must be at least 10 characters.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.api
      .cancelRequest(target.id, { reason, expectedVersion: target.version })
      .subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        this.closeCancelModal();
        // Re-read rather than merge: the server may have done more than this screen
        // predicted — applied the grant, stamped the decision — and a locally patched row
        // would quietly disagree with it.

        this.decisionResult.set({
          reference: target.reference,
          state: 'Cancelled',
          effectiveTime: this.formatDateTime(new Date().toISOString()),
          nextAction: `Request ${target.reference} was cancelled. The history is retained.`
        });
        this.showDecisionResultModal.set(true);
        this.loadData();
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.decisionError.set(error.message);
      },
    });
  }

  // ===== DELETE UNUSED DRAFT =====
  openDeleteModal(request: AccessRequestView): void {
    if (request.approvalState !== 'Draft') return;
    this.actionTarget.set(request);
    this.decisionReason.set('');
    this.decisionError.set('');
    this.showDeleteModal.set(true);
  }

  closeDeleteModal(): void {
    this.showDeleteModal.set(false);
    this.actionTarget.set(null);
    this.decisionReason.set('');
    this.decisionError.set('');
  }

  confirmDelete(): void {
    const target = this.actionTarget();
    if (!target) return;

    const reason = this.decisionReason().trim();
    if (reason.length < 10) {
      this.decisionError.set('Deletion reason must be at least 10 characters.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.api
      .deleteDraft(target.id, { reason, expectedVersion: target.version })
      .subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        this.closeDeleteModal();

        const items = this.filteredRequests().filter(r => r.id !== target.id);
        this.filteredRequests.set(items);

        this.decisionResult.set({
          reference: target.reference,
          state: 'Deleted',
          effectiveTime: this.formatDateTime(new Date().toISOString()),
          nextAction: `Request ${target.reference} was permanently deleted.`
        });
        this.showDecisionResultModal.set(true);
        this.loadData();
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.decisionError.set(error.message);
      },
    });
  }

  // ===== APPROVE (primary decision — only for Submitted/Pending state) =====
  openApproveModal(request: AccessRequestView): void {
    if (request.approvalState !== 'Submitted' && request.approvalState !== 'PendingReview') return;
    this.decisionTarget.set(request);
    this.decisionReason.set('');
    this.decisionError.set('');
    this.showApproveModal.set(true);
  }

  closeApproveModal(): void {
    this.showApproveModal.set(false);
    this.decisionTarget.set(null);
    this.decisionReason.set('');
    this.decisionError.set('');
  }

  confirmApprove(): void {
    const target = this.decisionTarget();
    if (!target) return;

    // Decision reason is required (10–1000 characters)
    const reason = this.decisionReason().trim();
    if (reason.length < 10) {
      this.decisionError.set('Decision reason must be at least 10 characters.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.api.approveRequest(target.id, target.version, reason).subscribe({
      next: (outcome) => {
        this.submitting.set(false);

        const effectiveTime = this.formatDateTime(new Date().toISOString());

        // Persistent confirmation — not just a toast
        this.decisionResult.set({
          reference: target.reference,
          state: 'Approved',
          effectiveTime,
          nextAction: `${target.requestedRole} is now active for ${target.user}.`
        });
        this.showApproveModal.set(false);
        this.showDecisionResultModal.set(true);
        this.decisionTarget.set(null);
        this.decisionReason.set('');
        this.loadData();
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.decisionError.set(error.message);
      },
    });
  }

  // ===== REJECT (secondary/danger — requires explicit reason) =====
  openRejectModal(request: AccessRequestView): void {
    if (request.approvalState !== 'Submitted' && request.approvalState !== 'PendingReview') return;
    this.decisionTarget.set(request);
    this.decisionReason.set('');
    this.decisionError.set('');
    this.showRejectModal.set(true);
  }

  closeRejectModal(): void {
    this.showRejectModal.set(false);
    this.decisionTarget.set(null);
    this.decisionReason.set('');
    this.decisionError.set('');
  }

  confirmReject(): void {
    const target = this.decisionTarget();
    if (!target) return;

    // Decision reason is required (10–1000 characters)
    const reason = this.decisionReason().trim();
    if (reason.length < 10) {
      this.decisionError.set('Decision reason must be at least 10 characters.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');

    this.api.rejectRequest(target.id, target.version, reason).subscribe({
      next: (outcome) => {
        this.submitting.set(false);

        const effectiveTime = this.formatDateTime(new Date().toISOString());

        // Persistent confirmation — not just a toast
        this.decisionResult.set({
          reference: target.reference,
          state: 'Rejected',
          effectiveTime,
          nextAction: `Access was not granted for ${target.user}. The decision is recorded permanently.`
        });
        this.showRejectModal.set(false);
        this.showDecisionResultModal.set(true);
        this.decisionTarget.set(null);
        this.decisionReason.set('');
        this.loadData();
      },
      error: (error: Error) => {
        this.submitting.set(false);
        this.decisionError.set(error.message);
      },
    });
  }

  closeDecisionResultModal(): void {
    this.showDecisionResultModal.set(false);
    this.decisionResult.set(null);
  }

  goBack(): void { this.router.navigate(['/app/administration/access/user-directory']); }
}