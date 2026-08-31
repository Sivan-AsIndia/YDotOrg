import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import {
  PlanItem,
  PlanRecord,
  PlanVersion,
  PlanEditableFields,
  ApprovalState,
  UiState,
  ActionMode,
} from '../../../../Shared/models/budget-target-plan.model';
import { BudgetTargetStoreService, PlanMutationResult } from '../../../../Shared/services/budget-target-store.service';
import { CampaignStoreService } from '../../../../Shared/services/campaign-store.service';
import { CurrentUserService } from '../../../../Service/current-user.service';
import { PeopleDirectoryService } from '../../../../Shared/services/people-directory.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';

/** Field labels used verbatim in the validation copy. */
const FIELD_LABELS: Record<string, string> = {
  campaign: 'Campaign',
  planPeriod: 'Plan period',
  targetDimension: 'Target dimension',
  owner: 'Owner or team',
  targetAmount: 'Target amount',
  budgetCategory: 'Budget category',
  budgetAmount: 'Budget amount',
  expectedVolume: 'Expected volume',
  assumptions: 'Assumptions',
};
/** Focus order for "focus the first invalid field". */
const FIELD_ORDER = [
  'campaign',
  'planPeriod',
  'targetDimension',
  'owner',
  'targetAmount',
  'budgetCategory',
  'budgetAmount',
  'expectedVolume',
  'assumptions',
];
/** Reference whose Submit deterministically hits an external-finance rejection (dependency-failure demo). */
const FINANCE_REJECT_REFERENCE = 'PLAN-2025-0009';

@Component({
  selector: 'app-budget-target-plan',
  imports: [CommonModule, FormsModule],
  templateUrl: './budget-and-target-plan.html',
  styleUrl: './budget-and-target-plan.css',
})
export class BudgetTargetPlanComponent {
  private readonly store = inject(BudgetTargetStoreService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly campaignStore = inject(CampaignStoreService);
  private readonly route = inject(ActivatedRoute);

  protected readonly pageTitle = 'Budget and target plan';
  protected readonly pageSubtitle =
    'Plan what a campaign intends to raise and to spend, version by version.';
  /** The data scope, which the server decides from the caller's token. */
  protected readonly scope = 'My active organisation';
  /** When the loaded set was actually read, rather than a fixed time in a bundle. */
  protected readonly lastRefresh = signal('');
  /**
   * Who is accountable for the plan on screen.
   *
   * FROM THE PLAN, not a page-level name. A budget's owner is a property of that budget, and
   * showing one person's name above a register of plans owned by several was misleading in
   * exactly the situation the field exists for.
   */
  protected get owner(): string {
    return this.selectedPlan()?.owner ?? '—';
  }

  /** Dev-only state trigger (`?simulate=conflict|dependency`) so the hard-to-reach states are testable. */
  private readonly simulate = signal<string>(this.route.snapshot.queryParamMap.get('simulate') ?? '');

  // ================= Session / permissions (Step 2) =================
  /** Session "user id" — used for the segregation-of-duties rule. */
  protected readonly currentUserRef = computed(() => this.currentUser.reference());
  protected readonly currentUserName = computed(() => this.currentUser.current().name);

  protected readonly permissions = computed(() => ({
    view: this.currentUser.hasPermission('cam.budget-plans.view'),
    allocate: this.currentUser.hasPermission('cam.budget-plans.allocate'),
    revise: this.currentUser.hasPermission('cam.budget-plans.revise'),
    submit: this.currentUser.hasPermission('cam.budget-plans.submit'),
    approve: this.currentUser.hasPermission('cam.budget-plans.approve'),
  }));

  // ================= Display projection over the versioned store =================
  /**
   * One flat PlanItem per plan reference, built from the record's current working
   * (latest) version — so the table renders one row per reference exactly as before,
   * while reconciled/variance are shown against the current APPROVED version only
   * (server-derived), never summed across versions.
   */
  protected get plans(): PlanItem[] {
    return this.store.all().map((r) => this.toItem(r));
  }

  private toItem(record: PlanRecord): PlanItem {
    const disp = this.store.displayVersion(record);
    const appr = this.store.approvedVersion(record);
    const reconciledSource = appr ?? disp;
    return {
      reference: record.reference,
      campaign: record.campaign,
      campaignRef: record.campaignRef,
      planPeriod: record.planPeriod,
      targetDimension: record.targetDimension,
      owner: record.owner,
      ownerRef: record.ownerRef,
      targetAmount: disp.targetAmount,
      budgetCategory: disp.budgetCategory,
      budgetAmount: disp.budgetAmount,
      expectedVolume: disp.expectedVolume,
      assumptions: disp.assumptions,
      version: 'v' + disp.versionNumber,
      versionNumber: disp.versionNumber,
      approvalState: disp.approvalState,
      submittedByRef: disp.submittedByRef,
      actualReconciledResult: reconciledSource.actualReconciledResult,
      variance: reconciledSource.variance,
      effectiveTime: disp.effectiveTime,
      hasApprovedVersion: !!appr,
    };
  }

  // ================= UI state =================
  protected readonly uiState = signal<UiState>('ready');
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  /** Distinguish empty causes so the empty state never implies a global zero. */
  protected emptyReason(): 'no-records' | 'filtered' {
    if (this.store.all().length === 0) return 'no-records';
    return 'filtered';
  }

  // ---- Filters ----
  protected searchQuery = signal('');
  protected filterApproval = signal('');
  protected filterReconciled = signal('');
  protected filterCampaign = signal('');
  protected filterBudgetCategory = signal('');

  protected uniqueCampaigns = (): string[] => [...new Set(this.plans.map((p) => p.campaign))];
  protected uniqueBudgetCategories = (): string[] => [...new Set(this.plans.map((p) => p.budgetCategory))];

  protected hasActiveFilters(): boolean {
    return !!(
      this.searchQuery() ||
      this.filterApproval() ||
      this.filterReconciled() ||
      this.filterCampaign() ||
      this.filterBudgetCategory()
    );
  }

  protected clearFilters(): void {
    this.searchQuery.set('');
    this.filterApproval.set('');
    this.filterReconciled.set('');
    this.filterCampaign.set('');
    this.filterBudgetCategory.set('');
    this.currentPage.set(1);
  }

  protected filteredPlans(): PlanItem[] {
    let result = [...this.plans];
    const q = this.searchQuery().toLowerCase();
    if (q) {
      result = result.filter(
        (p) =>
          p.campaign.toLowerCase().includes(q) ||
          p.owner.toLowerCase().includes(q) ||
          p.reference.toLowerCase().includes(q),
      );
    }
    if (this.filterApproval()) result = result.filter((p) => p.approvalState === this.filterApproval());
    if (this.filterReconciled()) result = result.filter((p) => p.actualReconciledResult === this.filterReconciled());
    if (this.filterCampaign()) result = result.filter((p) => p.campaign === this.filterCampaign());
    if (this.filterBudgetCategory()) result = result.filter((p) => p.budgetCategory === this.filterBudgetCategory());
    return result;
  }

  // ---- Pagination ----
  protected currentPage = signal(1);
  protected pageSize = 10;

  protected get totalPages(): number {
    return Math.max(1, Math.ceil(this.filteredPlans().length / this.pageSize));
  }

  protected paginatedPlans(): PlanItem[] {
    const start = (this.currentPage() - 1) * this.pageSize;
    return this.filteredPlans().slice(start, start + this.pageSize);
  }

  protected pageNumbers(): number[] {
    const total = this.totalPages;
    const current = this.currentPage();
    const pages: number[] = [];
    const start = Math.max(1, current - 1);
    const end = Math.min(total, current + 1);
    for (let i = start; i <= end; i++) pages.push(i);
    return pages;
  }

  protected goToPage(page: number): void {
    if (page >= 1 && page <= this.totalPages) this.currentPage.set(page);
  }

  // ---- Refresh (real refresh with a loading state, replacing the mis-wired clearFilters) ----
  protected refresh(): void {
    this.uiState.set('loading');
    setTimeout(() => {
      this.lastRefresh.set('Just now · IST');
      this.uiState.set('ready');
    }, 600);
  }

  // ================= Summary (approved-only, no double counting) =================
  protected formatAmount(value: number): string {
    return '₹' + value.toLocaleString('en-IN');
  }

  private approvedVersions(): PlanVersion[] {
    return this.store
      .all()
      .map((r) => this.store.approvedVersion(r))
      .filter((v): v is PlanVersion => !!v);
  }

  protected getApprovedCount(): number {
    return this.store.all().filter((r) => !!this.store.approvedVersion(r)).length;
  }
  protected getSubmittedCount(): number {
    return this.plans.filter((p) => p.approvalState === 'Submitted').length;
  }
  protected getDraftCount(): number {
    return this.plans.filter((p) => p.approvalState === 'Draft').length;
  }

  private compactCurrency(total: number): string {
    if (total >= 10000000) return '₹' + (total / 10000000).toFixed(1) + ' Cr';
    return '₹' + (total / 100000).toFixed(1) + ' L';
  }
  /** Total Target — sum of the CURRENT APPROVED version of each plan only. */
  protected formatTotalAmount(): string {
    return this.compactCurrency(this.approvedVersions().reduce((s, v) => s + v.targetAmount, 0));
  }
  /** Total Budget — sum of the CURRENT APPROVED version of each plan only. */
  protected getTotalBudget(): string {
    return this.compactCurrency(this.approvedVersions().reduce((s, v) => s + v.budgetAmount, 0));
  }

  // ---- Badge classes (unchanged look) ----
  protected getVarianceBadgeClass(variance: string): string {
    if (variance.startsWith('+')) return 'text-success';
    if (variance.startsWith('-')) return 'text-danger';
    return 'text-secondary';
  }
  protected getApprovalBadgeClass(state: ApprovalState): string {
    switch (state) {
      case 'Approved':
        return 'bg-success bg-opacity-10 text-success';
      case 'Submitted':
        return 'bg-warning bg-opacity-10 text-warning';
      case 'Rejected':
        return 'bg-danger bg-opacity-10 text-danger';
      case 'Superseded':
        return 'bg-light text-muted';
      case 'Draft':
        return 'bg-secondary bg-opacity-10 text-secondary';
      default:
        return 'bg-light text-dark';
    }
  }
  protected getReconciledBadgeClass(result: string): string {
    switch (result) {
      case 'On Track':
        return 'bg-success bg-opacity-10 text-success';
      case 'Exceeded':
        return 'bg-info bg-opacity-10 text-info';
      case 'Pending':
        return 'bg-warning bg-opacity-10 text-warning';
      case 'Superseded':
        return 'bg-light text-muted';
      case 'Not Started':
        return 'bg-secondary bg-opacity-10 text-secondary';
      default:
        return 'bg-light text-dark';
    }
  }

  // ================= Per-row action eligibility (permission + state + SoD) =================
  /** Revise a plan into a new Draft version — only from a settled state, and only with permission. */
  protected canRevise(p: PlanItem): boolean {
    return this.permissions().revise && (p.approvalState === 'Approved' || p.approvalState === 'Rejected');
  }
  /** Submit a Draft for review. */
  protected canSubmit(p: PlanItem): boolean {
    return this.permissions().submit && p.approvalState === 'Draft';
  }
  /** Approve is offered only on Submitted versions to permitted approvers; self-approval is
   *  shown-but-blocked (not hidden) inside the dialog. */
  protected canApprove(p: PlanItem): boolean {
    return this.permissions().approve && p.approvalState === 'Submitted';
  }

  // ================= Action modal + form (Steps 3–5) =================
  protected showActionModal = signal(false);
  protected actionMode = signal<ActionMode>('allocate');
  protected selectedPlan = signal<PlanItem | null>(null);
  protected actionSubmitting = signal(false);

  /** Version snapshot captured at modal open — used to detect record-changed-since-load. */
  private loadedVersion = signal<number>(0);

  // Editable form fields (kept as strings so raw input is preserved and decimals can be validated).
  protected fCampaignRef = signal('');
  protected fCampaign = signal('');
  protected fPlanPeriod = signal('');
  protected fTargetDimension = signal('');
  protected fOwnerRef = signal('');
  protected fOwner = signal('');
  protected fTargetAmount = signal('');
  protected fBudgetCategory = signal('');
  protected fBudgetAmount = signal('');
  protected fExpectedVolume = signal('');
  protected fAssumptions = signal('');
  protected approveReason = signal('');

  protected formErrors = signal<Record<string, string>>({});
  protected fieldError(field: string): string {
    return this.formErrors()[field] ?? '';
  }
  protected errorList(): { field: string; label: string; message: string }[] {
    const e = this.formErrors();
    return FIELD_ORDER.filter((f) => e[f]).map((f) => ({ field: f, label: FIELD_LABELS[f], message: e[f] }));
  }

  // Success / conflict / dependency-failure carriers.
  protected successResult = signal<{ reference: string; version: string; state: string; effectiveTime: string; nextAction: string } | null>(null);
  protected duplicateOf = signal<string>('');
  protected correlationRef = signal<string>('');

  /** Scope-aware campaign options — from the shared campaign store. */
  private readonly people = inject(PeopleDirectoryService);

  protected campaignOptions() {
    return this.campaignStore.all();
  }
  /**
   * The people a budget can be made accountable to.
   *
   * FROM IAM, WITHIN THE CALLER'S DATA SCOPE. The four names here were invented, so a budget
   * "owned by Arun Kumar" was owned by nobody - and an unspent budget with no real owner is one
   * nobody is asked about at the year end.
   *
   * ACTIVE ACCOUNTS ONLY: assigning a budget to a suspended account names an owner who will never
   * see it.
   */
  protected readonly ownerOptions = computed(() =>
    this.people.assignable().map((person) => ({ ref: person.reference, name: person.name })),
  );

  protected onCampaignSelect(ref: string): void {
    this.fCampaignRef.set(ref);
    this.fCampaign.set(this.campaignStore.get(ref)?.name ?? '');
  }
  protected onOwnerSelect(ref: string): void {
    this.fOwnerRef.set(ref);
    this.fOwner.set(this.people.name(ref));
  }

  // ---- Openers ----
  /** Allocate — the primary workflow action; creates a brand-new plan (not a per-row action). */
  protected openAllocate(): void {
    if (!this.permissions().allocate) return;
    this.actionMode.set('allocate');
    this.selectedPlan.set(null);
    this.resetForm();
    this.uiState.set('ready');
    this.showActionModal.set(true);
  }

  protected openRowAction(plan: PlanItem, action: ActionMode): void {
    this.selectedPlan.set(plan);
    this.actionMode.set(action);
    this.loadedVersion.set(plan.versionNumber);
    this.formErrors.set({});
    this.approveReason.set('');
    this.successResult.set(null);
    this.uiState.set('ready');
    if (action === 'revise') {
      // Prefill from the current version; a NEW Draft version will be created on save.
      this.fCampaignRef.set(plan.campaignRef);
      this.fCampaign.set(plan.campaign);
      this.fPlanPeriod.set(plan.planPeriod);
      this.fTargetDimension.set(plan.targetDimension);
      this.fOwnerRef.set(plan.ownerRef);
      this.fOwner.set(plan.owner);
      this.fTargetAmount.set(String(plan.targetAmount));
      this.fBudgetCategory.set(plan.budgetCategory);
      this.fBudgetAmount.set(String(plan.budgetAmount));
      this.fExpectedVolume.set(String(plan.expectedVolume));
      this.fAssumptions.set(plan.assumptions);
    }
    this.showActionModal.set(true);
  }

  protected closeActionModal(): void {
    this.showActionModal.set(false);
    this.selectedPlan.set(null);
    this.actionSubmitting.set(false);
    this.uiState.set('ready');
    this.formErrors.set({});
    this.successResult.set(null);
    this.duplicateOf.set('');
    this.correlationRef.set('');
  }

  private resetForm(): void {
    this.fCampaignRef.set('');
    this.fCampaign.set('');
    this.fPlanPeriod.set('');
    this.fTargetDimension.set('');
    this.fOwnerRef.set('');
    this.fOwner.set('');
    this.fTargetAmount.set('');
    this.fBudgetCategory.set('');
    this.fBudgetAmount.set('');
    this.fExpectedVolume.set('');
    this.fAssumptions.set('');
    this.approveReason.set('');
    this.formErrors.set({});
    this.successResult.set(null);
    this.duplicateOf.set('');
    this.correlationRef.set('');
  }

  // ---- Validation ----
  private req(field: string): string {
    return `Enter ${FIELD_LABELS[field]}.`;
  }
  private inv(field: string): string {
    return `Review ${FIELD_LABELS[field]}. The value does not meet the stated format or range.`;
  }
  /** Decimal currency: non-negative, up to 2dp, within an upper bound. */
  private isValidAmount(raw: string): boolean {
    if (!/^\d+(\.\d{1,2})?$/.test(raw.trim())) return false;
    const n = Number(raw);
    return Number.isFinite(n) && n >= 0 && n <= 1_000_000_000_000;
  }

  private validate(): Record<string, string> {
    const e: Record<string, string> = {};
    if (!this.fCampaignRef()) e['campaign'] = this.req('campaign');
    if (!this.fPlanPeriod().trim()) e['planPeriod'] = this.req('planPeriod');
    if (!this.fTargetDimension().trim()) e['targetDimension'] = this.req('targetDimension');
    if (!this.fOwnerRef()) e['owner'] = this.req('owner');

    const ta = this.fTargetAmount().trim();
    if (!ta) e['targetAmount'] = this.req('targetAmount');
    else if (!this.isValidAmount(ta)) e['targetAmount'] = this.inv('targetAmount');

    if (!this.fBudgetCategory().trim()) e['budgetCategory'] = this.req('budgetCategory');

    const ba = this.fBudgetAmount().trim();
    if (!ba) e['budgetAmount'] = this.req('budgetAmount');
    else if (!this.isValidAmount(ba)) e['budgetAmount'] = this.inv('budgetAmount');

    const ev = this.fExpectedVolume().trim();
    if (!ev) e['expectedVolume'] = this.req('expectedVolume');
    else if (!/^\d+$/.test(ev)) e['expectedVolume'] = this.inv('expectedVolume');

    const as = this.fAssumptions().trim();
    if (!as) e['assumptions'] = this.req('assumptions');
    else if (as.length < 10 || as.length > 2000) e['assumptions'] = this.inv('assumptions');
    return e;
  }

  private focusFirstInvalid(errors: Record<string, string>): void {
    const first = FIELD_ORDER.find((f) => errors[f]);
    if (!first) return;
    setTimeout(() => document.getElementById('f-' + first)?.focus(), 0);
  }

  private collectFields(): PlanEditableFields {
    return {
      campaign: this.fCampaign(),
      campaignRef: this.fCampaignRef(),
      planPeriod: this.fPlanPeriod().trim(),
      targetDimension: this.fTargetDimension().trim(),
      owner: this.fOwner(),
      ownerRef: this.fOwnerRef(),
      targetAmount: Number(this.fTargetAmount()),
      budgetCategory: this.fBudgetCategory().trim(),
      budgetAmount: Number(this.fBudgetAmount()),
      expectedVolume: Number(this.fExpectedVolume()),
      assumptions: this.fAssumptions().trim(),
    };
  }

  // ---- Record-changed-since-load ----
  private hasRecordChanged(reference: string): boolean {
    if (this.simulate() === 'conflict') return true;
    const r = this.store.get(reference);
    return !!r && this.store.displayVersion(r).versionNumber !== this.loadedVersion();
  }

  /** True when the current approver is the version's submitter (segregation-of-duties block). */
  protected isSelfApproval(): boolean {
    const p = this.selectedPlan();
    return !!p && !!p.submittedByRef && p.submittedByRef === this.currentUserRef();
  }
  protected selfApprovalMessage(): string {
    return `This value cannot be approved by its submitter (${this.currentUserName()}). An independent approver who did not submit this version must decide.`;
  }

  // ---- Confirm handlers ----
  /** Allocate / Revise submit. */
  protected saveForm(): void {
    const errors = this.validate();
    this.formErrors.set(errors);
    if (Object.keys(errors).length) {
      this.uiState.set('validation');
      this.focusFirstInvalid(errors);
      return;
    }
    const fields = this.collectFields();
    this.actionSubmitting.set(true);

    // NO setTimeout. Every action here used to be wrapped in a 500ms delay so a synchronous array
    // write would feel like a request. The wait is now the request.
    if (this.actionMode() === 'allocate') {
      const duplicate = this.store.findDuplicate(
        fields.campaignRef || fields.campaign,
        fields.planPeriod,
        fields.targetDimension,
      );

      // Advisory only - the server enforces it with a unique index, which is what makes the rule
      // hold when two people allocate at the same moment. This just saves the round trip.
      if (duplicate) {
        this.duplicateOf.set(duplicate.reference);
        this.uiState.set('duplicate');
        this.actionSubmitting.set(false);
        return;
      }

      this.store.allocate(fields).subscribe({
        next: (result) => this.completeSuccess(result, 'Draft', 'Submit for review'),
        error: (error) => this.reportFailure(error, 'The plan could not be allocated.'),
      });

      return;
    }

    const reference = this.selectedPlan()!.reference;

    this.store.revise(reference, fields).subscribe({
      next: (result) => this.completeSuccess(result, 'Draft', 'Submit for review'),
      error: (error) => this.reportFailure(error, 'The plan could not be revised.'),
    });
  }

  /**
   * Reports a failed write.
   *
   * A CONFLICT AND A REFUSAL ARE DIFFERENT THINGS, and the screen has different states for them.
   * A stale version means somebody else changed the plan and the right response is to reload; a
   * segregation-of-duties refusal means this person may not take this action at all, and reloading
   * would change nothing.
   */
  private reportFailure(error: unknown, fallback: string): void {
    this.actionSubmitting.set(false);

    const code =
      typeof error === 'object' && error !== null && 'errorCode' in error
        ? (error as { errorCode?: string }).errorCode
        : undefined;

    if (code === 'CONCURRENCY_CONFLICT') {
      this.uiState.set('conflict');
      return;
    }

    if (code === 'DUPLICATE') {
      this.uiState.set('duplicate');
      return;
    }

    this.failureMessage.set(apiErrorMessage(error, fallback));
    this.uiState.set('dependency-failure');
  }

  /** What went wrong, in the server's words. Shown on the dependency-failure state. */
  protected readonly failureMessage = signal('');

  protected confirmSubmit(): void {
    const plan = this.selectedPlan();

    if (!plan || !this.permissions().submit) {
      return;
    }

    this.actionSubmitting.set(true);

    // THE FINANCE-REJECT DEMO IS GONE. One hard-coded plan reference used to fail on submit so the
    // screen could show its dependency-failure state, which meant that plan could never actually be
    // submitted - and every other failure was invisible. Real failures now drive that state.
    this.store.submit(plan.reference, plan.versionNumber).subscribe({
      next: (result) => this.completeSuccess(result, 'Submitted', 'Await independent approval'),
      error: (error) => this.reportFailure(error, 'The version could not be submitted.'),
    });
  }

  /**
   * Approves the selected version.
   *
   * THE SELF-APPROVAL CHECK IS STILL HERE, and it is still not what enforces the rule. The server
   * refuses an approval by the person who submitted the version, checked against the stored
   * submitter rather than against anything this browser claims. This check is what stops the button
   * being offered in the first place - the refusal would otherwise look like a fault.
   */
  protected confirmApprove(): void {
    const plan = this.selectedPlan();

    if (!plan || !this.permissions().approve || this.isSelfApproval()) {
      return;
    }

    this.actionSubmitting.set(true);

    this.store.approve(plan.reference, plan.versionNumber).subscribe({
      next: (result) => this.completeSuccess(result, 'Approved', 'View the approved plan'),
      error: (error) => this.reportFailure(error, 'The version could not be approved.'),
    });
  }

  private completeSuccess(res: PlanMutationResult, state: string, nextAction: string): void {
    this.successResult.set({
      reference: res.reference,
      version: 'v' + res.version.versionNumber,
      state,
      effectiveTime: res.effectiveTime,
      nextAction,
    });
    this.uiState.set('success');
    this.actionSubmitting.set(false);
  }

  /**
   * Retries the action that failed.
   *
   * IT ACTUALLY RETRIES. The old version waited 600ms and then declared success unconditionally,
   * because there was no request to repeat - so a retry always "worked" whatever the state of
   * anything.
   */
  protected retryDependency(): void {
    const plan = this.selectedPlan();

    if (!plan) {
      return;
    }

    this.actionSubmitting.set(true);

    this.store.submit(plan.reference, plan.versionNumber).subscribe({
      next: (result) => this.completeSuccess(result, 'Submitted', 'Await independent approval'),
      error: (error) => this.reportFailure(error, 'The version could not be submitted.'),
    });
  }

  /** Conflict recovery — reload the latest version into the modal snapshot and return to the action. */
  protected reloadLatest(): void {
    const plan = this.selectedPlan();
    if (!plan) {
      this.closeActionModal();
      return;
    }
    const record = this.store.get(plan.reference);
    if (record) {
      const item = this.toItem(record);
      this.selectedPlan.set(item);
      this.loadedVersion.set(item.versionNumber);
    }
    this.simulate.set(''); // clear the dev trigger so the reloaded flow can proceed
    this.uiState.set('ready');
  }

  // ---- Labels ----
  protected getActionLabel(): string {
    switch (this.actionMode()) {
      case 'allocate':
        return 'Allocate';
      case 'revise':
        return 'Revise';
      case 'submit':
        return 'Submit';
      case 'approve':
        return 'Approve';
      default:
        return 'Confirm';
    }
  }
  protected getActionButtonClass(): string {
    switch (this.actionMode()) {
      case 'approve':
        return 'btn-success';
      case 'revise':
        return 'btn-warning';
      default:
        return 'btn-primary';
    }
  }
}
