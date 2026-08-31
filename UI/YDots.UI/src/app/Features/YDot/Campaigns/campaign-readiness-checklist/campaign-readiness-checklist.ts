import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';

import { ClickOutsideDirective } from '../../../../Shared/directives/click-outside';
import { CampaignStatus } from '../../../../Shared/models/campaign.model';
import {
  Blocker,
  CrcUiState,
  DependencyKey,
  DependencyResult,
  DependencyStatus,
  ReadinessOwnerOption,
} from '../../../../Shared/models/campaign-readiness.model';
import { CampaignStoreService } from '../../../../Shared/services/campaign-store.service';
import { BudgetTargetStoreService } from '../../../../Shared/services/budget-target-store.service';
import { TrackingAssetStoreService } from '../../../../Shared/services/tracking-asset-store.service';
import { ContentReadinessStoreService } from '../../../../Shared/services/content-readiness-store.service';
import { CampaignReadinessStoreService } from '../../../../Shared/services/campaign-readiness-store.service';
import { CurrentUserService } from '../../../../Shared/services/current-user.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { ReadinessChecklistStoreService } from '../../../../Shared/services/readiness-checklist-store.service';
import { ReadinessCheck } from '../../../../Shared/models/campaign-readiness-checklist.model';
import { AddReadinessCheckComponent } from './add-readiness-check/add-readiness-check';
import { PeopleDirectoryService } from '../../../../Shared/services/people-directory.service';

/**
 * Campaign readiness checklist.
 *
 *  Route           : /cam/campaign-readiness-checklist?ref={campaign code}
 *  Purpose         : Bring content, approvals, budget, tracking, payment and
 *                    communication dependencies into one launch decision.
 *  View permission : cam.campaign-readiness-checklist.view
 *  Primary action  : Validate readiness
 *
 *  Budget approval and Tracking readiness are derived live from the shared
 *  budget-plan and tracking-asset stores — never entered on this page. Public
 *  content / Template / Payment readiness and the Consent notice version come
 *  from a clearly-labelled stub store standing in for other domains. Approve
 *  launch / Return to draft write the campaign's lifecycle state back to the
 *  shared campaign store so the other campaign screens reflect it immediately.
 */
@Component({
  selector: 'app-campaign-readiness-checklist',
  imports: [CommonModule, FormsModule, ClickOutsideDirective, AddReadinessCheckComponent],
  templateUrl: './campaign-readiness-checklist.html',
  styleUrl: './campaign-readiness-checklist.css',
})
export class CampaignReadinessChecklistComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly campaignStore = inject(CampaignStoreService);
  private readonly budgetStore = inject(BudgetTargetStoreService);
  private readonly trackingStore = inject(TrackingAssetStoreService);
  private readonly contentStub = inject(ContentReadinessStoreService);
  private readonly readinessStore = inject(CampaignReadinessStoreService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly toast = inject(ToastService);
  private readonly checklistStore = inject(ReadinessChecklistStoreService);

  protected readonly pageTitle = 'Campaign Readiness Checklist';
  protected readonly pageSubtitle = 'Validate all dependencies before campaign launch.';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('Today, 09:30 AM · IST');

  /**
   * The campaign this checklist is for, from the route.
   *
   * NO DEFAULT. It used to fall back to 'CAMP-2025-0011' - a campaign that was seeded into a
   * browser array and exists nowhere. Reached without a reference, the screen showed a readiness
   * checklist for a campaign nobody could act on; it now shows its empty state and says what is
   * missing, which is the difference between "no campaign selected" and "this campaign is ready".
   */
  protected readonly campaignRef = this.route.snapshot.queryParamMap.get('ref') ?? '';

  /** The acting session's "user id" — reused for the self-approval block on Approve launch. */
  protected readonly currentUserRef = computed(() => this.currentUser.reference());
  protected readonly currentUserName = computed(() => this.currentUser.current().name);

  /**
   * The people who can own a campaign launch or a blocker.
   *
   * FROM IAM, WITHIN THE CALLER'S DATA SCOPE. The five people listed here were invented, and this
   * screen is where that mattered most: a blocker holding up a launch was assigned to one of them,
   * which meant it was assigned to nobody and would sit unresolved until somebody noticed the
   * launch was not happening.
   */
  private readonly people = inject(PeopleDirectoryService);

  protected readonly ownerOptions = computed<readonly ReadinessOwnerOption[]>(() =>
    this.people.assignable().map((person) => ({
      reference: person.reference,
      name: person.name,
      context: person.context,
    })),
  );

  protected ownerName(ref: string): string {
    return this.people.name(ref);
  }

  // ================= Effective permissions =================
  protected readonly permissions = computed(() => ({
    view: this.currentUser.hasPermission('cam.readiness.view'),
    validate: this.currentUser.hasPermission('cam.readiness.pass'),
    assignBlocker: this.currentUser.hasPermission('cam.readiness.manage-blockers'),
    requestApproval: this.currentUser.hasPermission('cam.readiness.edit'),
    approveLaunch: this.currentUser.hasPermission('cam.readiness.approve'),
    returnToDraft: this.currentUser.hasPermission('cam.readiness.return-to-draft'),
  }));

  // ================= Campaign + readiness record (live from the shared stores) =================
  protected readonly campaign = computed(() =>
    this.campaignStore.all().find((c) => c.code === this.campaignRef) ?? null,
  );
  protected readonly campaignName = computed(() => this.campaign()?.name ?? '—');
  protected readonly lifecycleState = computed<CampaignStatus | '—'>(() => this.campaign()?.status ?? '—');

  protected readonly readiness = computed(() => this.readinessStore.snapshot()[this.campaignRef] ?? null);
  protected readonly ownerReference = computed(
    () => this.readiness()?.ownerReference ?? this.campaign()?.ownerReference ?? '',
  );
  protected readonly blockers = computed<readonly Blocker[]>(() => this.readiness()?.blockers ?? []);

  // ================= Derived dependency aggregation =================

  /** Budget approval — derived from the shared budget-plan store, never entered here. */
  private budgetDependency(): DependencyResult {
    const ref = this.campaignRef;
    const plans = (this.budgetStore.all() as readonly any[]).filter((p) => p?.campaignRef === ref);
    if (plans.length === 0) {
      return { key: 'budget', label: 'Budget approval', status: 'pending', note: 'No budget plan allocated for this campaign', derived: true };
    }
    const hasApproved = (plan: any): boolean =>
      Array.isArray(plan.versions)
        ? plan.versions.some((v: any) => v.approvalState === 'Approved')
        : plan.approvalState === 'Approved' || plan.hasApprovedVersion === true;
    const isRejected = (plan: any): boolean =>
      Array.isArray(plan.versions)
        ? plan.versions.length > 0 && plan.versions[plan.versions.length - 1].approvalState === 'Rejected'
        : plan.approvalState === 'Rejected';
    const approvedCount = plans.filter(hasApproved).length;
    const status: DependencyStatus = plans.every(hasApproved)
      ? 'pass'
      : plans.some(isRejected)
        ? 'fail'
        : 'pending';
    return {
      key: 'budget',
      label: 'Budget approval',
      status,
      note: `${approvedCount}/${plans.length} plan${plans.length === 1 ? '' : 's'} approved by Finance`,
      derived: true,
    };
  }

  /** Tracking readiness — derived from the shared tracking-asset store: assets must have
   *  passed Test and cleared Approve. Draft assets are excluded from the launch set. */
  private trackingDependency(): DependencyResult {
    const assets = this.trackingStore.forCampaign(this.campaignRef).filter((a) => a.assetStatus !== 'Draft');
    if (assets.length === 0) {
      return { key: 'tracking', label: 'Tracking readiness', status: 'pending', note: 'No tracking assets generated yet', derived: true };
    }
    const isReady = (a: (typeof assets)[number]): boolean =>
      a.lastTestResult === 'Passed' && (a.approvalState === 'Approved' || a.approvalState === 'Not required');
    const isBlocked = (a: (typeof assets)[number]): boolean =>
      a.lastTestResult === 'Failed' || a.approvalState === 'Rejected';
    const readyCount = assets.filter(isReady).length;
    const status: DependencyStatus = assets.every(isReady) ? 'pass' : assets.some(isBlocked) ? 'fail' : 'pending';
    return {
      key: 'tracking',
      label: 'Tracking readiness',
      status,
      note: `${readyCount}/${assets.length} assets tested & approved`,
      derived: true,
    };
  }

  /** Content / Template / Payment / Consent — read from the labelled STUB store (external domains). */
  private stubDependency(
    key: DependencyKey,
    label: string,
    status: DependencyStatus,
    note: string,
  ): DependencyResult {
    // A stubbed dependency whose backing service is unreachable reports `unknown`,
    // which is what routes the page into the Dependency-failure state — kept
    // strictly separate from the locally computed budget/tracking results.
    if (!this.contentStub.serviceReachable()) {
      return { key, label, status: 'unknown', note: 'Dependent service unreachable', derived: false };
    }
    return { key, label, status, note, derived: false };
  }

  /** Manual status override per dependency card — set via its Action menu (Pass / Fail) and
   *  cleared via "Reset to automatic". Takes precedence over the computed value below, so the
   *  existing (derived/stub) cards get the same manual override the added checks have. */
  protected readonly dependencyOverrides = signal<Partial<Record<DependencyKey, 'pass' | 'fail'>>>({});
  protected isDependencyOverridden(key: DependencyKey): boolean {
    return this.dependencyOverrides()[key] !== undefined;
  }
  private applyOverride(result: DependencyResult): DependencyResult {
    const override = this.dependencyOverrides()[result.key];
    if (!override) return result;
    return {
      ...result,
      status: override,
      note: `Manually marked as ${override === 'pass' ? 'Passed' : 'Failed'} — overrides the computed result.`,
    };
  }

  /** The six launch dependencies, in the page's established order. Recomputed live. */
  protected readonly dependencyResults = computed<readonly DependencyResult[]>(() => {
    const stub = this.contentStub.get(this.campaignRef);
    const raw = [
      this.stubDependency('content', 'Public content status', stub.publicContentStatus, stub.publicContentNote),
      this.budgetDependency(),
      this.trackingDependency(),
      this.stubDependency('payment', 'Payment readiness', stub.paymentStatus, stub.paymentNote),
      this.stubDependency('template', 'Template readiness', stub.templateStatus, stub.templateNote),
      this.stubDependency(
        'consent',
        'Consent notice version',
        stub.consentPublished ? 'pass' : 'pending',
        stub.consentPublished ? `Notice ${stub.consentNoticeVersion} published` : 'Consent notice not published',
      ),
    ];
    return raw.map((r) => this.applyOverride(r));
  });

  // ----- Dependency card Action menu: Pass / Fail / Reset to automatic -----
  protected readonly dependencyRowMenuOpen = signal<DependencyKey | ''>('');
  protected toggleDependencyMenu(key: DependencyKey): void {
    this.dependencyRowMenuOpen.update((cur) => (cur === key ? '' : key));
  }
  protected closeDependencyMenu(): void {
    this.dependencyRowMenuOpen.set('');
  }
  protected markDependencyPassed(item: DependencyResult): void {
    this.closeDependencyMenu();
    this.dependencyOverrides.update((cur) => ({ ...cur, [item.key]: 'pass' }));
    this.lastRefresh.set('Just now · IST');
    this.toast.show('Readiness check updated', `${item.label} marked as passed.`, 'success');
  }
  protected markDependencyFailed(item: DependencyResult): void {
    this.closeDependencyMenu();
    this.dependencyOverrides.update((cur) => ({ ...cur, [item.key]: 'fail' }));
    this.lastRefresh.set('Just now · IST');
    this.toast.show('Readiness check updated', `${item.label} marked as failed.`, 'success');
  }
  protected resetDependencyOverride(item: DependencyResult): void {
    this.closeDependencyMenu();
    this.dependencyOverrides.update((cur) => {
      const next = { ...cur };
      delete next[item.key];
      return next;
    });
    this.lastRefresh.set('Just now · IST');
    this.toast.show('Readiness check updated', `${item.label} reset to automatic.`, 'success');
  }

  protected readonly consentNoticeVersion = computed(() => this.contentStub.get(this.campaignRef).consentNoticeVersion);

  /** Map a dependency status onto the 3-band meter (unknown shows as pending). */
  protected meterStatus(s: DependencyStatus): 'pass' | 'fail' | 'pending' {
    return s === 'pass' ? 'pass' : s === 'fail' ? 'fail' : 'pending';
  }
  protected getPassCount(): number {
    return (
      this.dependencyResults().filter((i) => i.status === 'pass').length +
      this.checklistItems().filter((c) => c.status === 'Passed').length
    );
  }
  protected getFailCount(): number {
    return (
      this.dependencyResults().filter((i) => i.status === 'fail').length +
      this.checklistItems().filter((c) => c.status === 'Failed').length
    );
  }
  protected getPendingCount(): number {
    return (
      this.dependencyResults().filter((i) => i.status === 'pending' || i.status === 'unknown').length +
      this.checklistItems().filter((c) => c.status === 'Pending').length
    );
  }
  protected getTotalCount(): number {
    return this.dependencyResults().length + this.checklistItems().length;
  }
  protected getReadinessPct(): number {
    const total = this.getTotalCount();
    return total ? Math.round((this.getPassCount() / total) * 100) : 0;
  }

  /** Budget + Tracking are the "at minimum" gate for Request approval. */
  protected readonly budgetResult = computed(() => this.dependencyResults().find((d) => d.key === 'budget')!);
  protected readonly trackingResult = computed(() => this.dependencyResults().find((d) => d.key === 'tracking')!);
  protected readonly minimumGateMet = computed(
    () => this.budgetResult().status === 'pass' && this.trackingResult().status === 'pass',
  );
  /** Dependencies not yet passing — the candidate targets for Assign blocker. */
  protected readonly unmetDependencies = computed(() => this.dependencyResults().filter((d) => d.status !== 'pass'));

  // ================= UI state machine =================
  protected readonly uiState = signal<CrcUiState>('loading');
  protected setUiState(state: CrcUiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  // Concurrency snapshot — taken at load and after each of the page's own committed actions.
  private readonly loadedStatus = signal<CampaignStatus | null>(null);
  private readonly loadedReadinessVersion = signal<number | null>(null);
  private syncSnapshot(): void {
    this.loadedStatus.set(this.campaign()?.status ?? null);
    this.loadedReadinessVersion.set(this.readiness()?.version ?? null);
  }
  /** "This record changed after you opened it" — campaign state or readiness record moved underneath us. */
  private isStale(): boolean {
    const statusChanged = this.loadedStatus() !== null && this.campaign()?.status !== this.loadedStatus();
    const versionChanged =
      this.loadedReadinessVersion() !== null && (this.readiness()?.version ?? null) !== this.loadedReadinessVersion();
    return statusChanged || versionChanged;
  }

  constructor() {
    /**
     * Loads the readiness record once the campaign itself has arrived.
     *
     * IT USED TO BE A 500ms TIMER. The stores were local arrays, so half a second was ample and
     * "no campaign after 500ms" reliably meant "no such campaign". Now that both stores fetch, a
     * slow response would have put the screen into its EMPTY state - telling somebody a campaign
     * does not exist because the network was busy, and, worse, showing a readiness checklist with
     * nothing on it as though nothing needed checking.
     *
     * The effect fires again when the campaign lands, so the screen resolves when the data does.
     */
    let loadedFor: string | null = null;

    effect(() => {
      const campaign = this.campaign();

      if (!this.permissions().view) {
        this.uiState.set('no-access');
        return;
      }

      if (!this.campaignRef) {
        this.uiState.set('empty');
        return;
      }

      if (!campaign) {
        // Still loading, unless the store has finished and this campaign is genuinely not in it.
        this.uiState.set(
          this.campaignStore.isLoading() ? 'loading' : 'empty',
        );
        return;
      }

      if (loadedFor !== this.campaignRef) {
        loadedFor = this.campaignRef;
        this.readinessStore.ensure(this.campaignRef, campaign.ownerReference);
      }

      this.syncSnapshot();
      this.uiState.set('ready');
    });

    // No access hides the record, fields, counts and actions — reacts live to a session switch
    // (e.g. via the shared CurrentUserService switcher on another CAM screen). Never CSS-only.
    effect(() => {
      const canView = this.permissions().view;
      const current = untracked(this.uiState);
      if (!canView && current !== 'no-access' && current !== 'loading') {
        this.uiState.set('no-access');
      } else if (canView && current === 'no-access') {
        this.uiState.set(this.campaign() ? 'ready' : 'empty');
      }
    });
  }

  // ================= Persistent success + last-validated evidence =================
  protected readonly lastValidatedAt = signal<string | null>(null);
  protected readonly validationResults = signal<readonly DependencyResult[] | null>(null);
  protected readonly successReference = signal('');
  protected readonly successState = signal('');
  protected readonly successEffective = signal('');
  protected readonly successNextAction = signal('');

  private showSuccess(reference: string, state: string, nextAction: string): void {
    this.successReference.set(reference);
    this.successState.set(state);
    this.successEffective.set(this.lastRefresh());
    this.successNextAction.set(nextAction);
    // Surface the outcome as a toast rather than an inline banner.
    this.toast.show('Saved successfully', `${reference} · ${state}. Next: ${nextAction}.`, 'success');
    this.uiState.set('ready');
    this.syncSnapshot();
  }

  // ================= Validate readiness =================
  protected validateReadiness(): void {
    if (!this.permissions().validate) return;
    // A real recompute against the shared stores (the computed signals re-read them now):
    // both the derived/stub dependencies AND every manually-added readiness check.
    const results = this.dependencyResults().map((r) => ({ ...r }));
    this.validationResults.set(results);
    // Stamp a fresh timestamp so Overall readiness, the Passed/Failed/Pending/Total
    // counts and the "Last validated" note all reflect this run — including any
    // check just added.
    this.lastRefresh.set('Just now · IST');
    this.lastValidatedAt.set(this.lastRefresh());
    // Separate a failed *dependent service* step from the confirmed local result.
    if (results.some((r) => r.status === 'unknown')) {
      this.uiState.set('dependency-failure');
      return;
    }
    this.uiState.set('ready');
    this.toast.show(
      'Readiness validated',
      `${this.getPassCount()} passed · ${this.getFailCount()} failed · ${this.getPendingCount()} pending of ${this.getTotalCount()} items.`,
      'success',
    );
  }
  /** Retry only the failed dependency using a stable correlation reference. */
  protected retryDependency(): void {
    this.contentStub.setReachable(true);
    this.validateReadiness();
  }
  /** Dev affordance to demonstrate the dependency-failure path (stub service outage). */
  protected simulateDependencyOutage(): void {
    this.contentStub.setReachable(false);
    this.validateReadiness();
  }

  // ================= Owner selector + Planned launch time =================
  protected onOwnerChange(reference: string): void {
    if (!reference) return;
    this.readinessStore.update(this.campaignRef, { ownerReference: reference });
    this.syncSnapshot();
  }
  protected onPlannedLaunchChange(value: string): void {
    this.readinessStore.update(this.campaignRef, { plannedLaunchTime: value });
    this.syncSnapshot();
  }
  protected readonly plannedLaunchTime = computed(() => this.readiness()?.plannedLaunchTime ?? '');
  /** Interpreted, human-legible date-time shown before submit. */
  protected interpretLaunchTime(value: string): string {
    if (!value) return '';
    const d = new Date(value);
    if (Number.isNaN(d.getTime())) return '';
    return d.toLocaleString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: true,
    });
  }
  protected launchTimeInPast(value: string): boolean {
    if (!value) return false;
    const d = new Date(value);
    return !Number.isNaN(d.getTime()) && d.getTime() < Date.now();
  }

  // ================= Overflow actions menu =================
  protected readonly actionsMenuOpen = signal(false);
  protected toggleActionsMenu(): void {
    this.actionsMenuOpen.update((v) => !v);
  }
  protected closeActionsMenu(): void {
    this.actionsMenuOpen.set(false);
  }

  // ================= Add / edit readiness check (off-canvas) =================
  protected readonly addCheckDrawerOpen = signal(false);
  /** The check being edited, or null while authoring a new one — drives the drawer's mode. */
  protected readonly editingCheck = signal<ReadinessCheck | null>(null);

  /** Every manually-added readiness check for this campaign, newest first. */
  protected readonly checklistItems = computed<readonly ReadinessCheck[]>(() =>
    this.checklistStore.checksFor(this.campaignRef),
  );
  protected checklistStatusClass(status: ReadinessCheck['status']): 'pass' | 'fail' | 'pending' {
    return status === 'Passed' ? 'pass' : status === 'Failed' ? 'fail' : 'pending';
  }

  // ----- Row overflow menu: Delete / Edit / Pass·Completed -----
  protected readonly checklistRowMenuOpen = signal<string | null>(null);
  protected toggleChecklistRowMenu(id: string): void {
    this.checklistRowMenuOpen.update((cur) => (cur === id ? null : id));
  }
  protected closeChecklistRowMenu(): void {
    this.checklistRowMenuOpen.set(null);
  }

  // ----- View a checklist item (read-only popup of what was captured on create) -----
  protected readonly viewCheck = signal<ReadinessCheck | null>(null);
  protected openViewCheck(check: ReadinessCheck): void {
    this.viewCheck.set(check);
  }
  protected closeViewCheck(): void {
    this.viewCheck.set(null);
  }

  // ----- View an existing (derived / stub) dependency card in the same popup style -----
  protected readonly viewDependency = signal<DependencyResult | null>(null);
  protected openViewDependency(item: DependencyResult): void {
    this.viewDependency.set(item);
  }
  protected closeViewDependency(): void {
    this.viewDependency.set(null);
  }
  protected dependencySourceLabel(item: DependencyResult): string {
    if (this.isDependencyOverridden(item.key)) return 'Manually overridden';
    return item.derived ? 'Derived live' : 'Stub · external domain';
  }
  protected editFromView(check: ReadinessCheck): void {
    this.viewCheck.set(null);
    this.openEditCheckDrawer(check);
  }

  protected openAddCheckDrawer(): void {
    this.closeActionsMenu();
    this.editingCheck.set(null);
    this.addCheckDrawerOpen.set(true);
  }
  protected openEditCheckDrawer(check: ReadinessCheck): void {
    this.closeChecklistRowMenu();
    this.editingCheck.set(check);
    this.addCheckDrawerOpen.set(true);
  }
  protected closeAddCheckDrawer(): void {
    this.addCheckDrawerOpen.set(false);
    this.editingCheck.set(null);
  }
  /**
   * Persist the authored (or edited) check to the shared store, then refresh the underlying
   * page from the live stores and surface the app toast. The derived readiness
   * aggregation (counts, meter, statuses, timestamp) is untouched — this only
   * records the check and re-reads.
   */
  protected onCheckAdded(check: ReadinessCheck): void {
    if (this.editingCheck()) {
      this.checklistStore.updateCheck(this.campaignRef, check.id, check);
      this.toast.show('Readiness check updated', `${check.name} was updated.`, 'success');
    } else {
      this.checklistStore.addCheck(this.campaignRef, check);
      this.toast.show('Readiness check added', `${check.name} added to ${this.campaignRef}.`, 'success');
    }
    this.lastRefresh.set('Just now · IST');
  }

  /** Row "Pass/Completed" — manually approves a Pending check; it moves out of Pending
   *  without waiting on a computed result. */
  protected markChecklistPassed(check: ReadinessCheck): void {
    this.closeChecklistRowMenu();
    this.checklistStore.setStatus(this.campaignRef, check.id, 'Passed');
    this.lastRefresh.set('Just now · IST');
    this.toast.show('Readiness check updated', `${check.name} marked as passed.`, 'success');
  }
  /** Row "Fail" — manually rejects a check. */
  protected markChecklistFailed(check: ReadinessCheck): void {
    this.closeChecklistRowMenu();
    this.checklistStore.setStatus(this.campaignRef, check.id, 'Failed');
    this.lastRefresh.set('Just now · IST');
    this.toast.show('Readiness check updated', `${check.name} marked as failed.`, 'success');
  }
  /** Row "Delete" — removes the check entirely. */
  protected deleteChecklistItem(check: ReadinessCheck): void {
    this.closeChecklistRowMenu();
    this.checklistStore.removeCheck(this.campaignRef, check.id);
    this.lastRefresh.set('Just now · IST');
    this.toast.show('Readiness check deleted', `${check.name} was removed.`, 'success');
  }

  // ================= Assign blocker =================
  // Assign blocker is now raised per checklist card (from its Action menu), so the dialog
  // targets one specific dependency / check rather than choosing one from a list.
  protected readonly blockerDialogOpen = signal(false);
  protected readonly blockerDependency = signal<string>('');
  protected readonly blockerTargetLabel = signal<string>('');
  protected readonly blockerOwnerRef = signal('');
  protected readonly blockerNote = signal('');
  protected readonly blockerTouched = signal(false);
  protected readonly blockerOwnerValid = computed(() => this.blockerOwnerRef() !== '');
  protected readonly blockerNoteValid = computed(() => this.blockerNote().trim().length > 0);
  protected readonly blockerValid = computed(
    () => this.blockerDependency() !== '' && this.blockerOwnerValid() && this.blockerNoteValid(),
  );

  /** Open Assign blocker pre-targeted at a specific checklist card (dependency or manual check). */
  protected openBlockerFor(key: string, label: string): void {
    this.closeDependencyMenu();
    this.closeChecklistRowMenu();
    if (!this.permissions().assignBlocker) return;
    this.blockerDependency.set(key);
    this.blockerTargetLabel.set(label);
    this.blockerOwnerRef.set('');
    this.blockerNote.set('');
    this.blockerTouched.set(false);
    this.blockerDialogOpen.set(true);
  }
  protected cancelBlocker(): void {
    this.blockerDialogOpen.set(false);
  }
  protected confirmBlocker(): void {
    this.blockerTouched.set(true);
    if (!this.blockerValid()) {
      // Keep the dialog open with inline field errors; preserve the entered values.
      return;
    }
    const depKey = this.blockerDependency();
    // A blocker already open on the same dependency is a duplicate — offer compare/cancel, never overwrite.
    if (this.blockers().some((b) => b.dependencyKey === depKey)) {
      this.uiState.set('duplicate');
      return;
    }
    const label = this.blockerTargetLabel();
    const ownerRef = this.blockerOwnerRef();
    const blocker: Blocker = {
      id: `BLK-${Date.now()}`,
      dependencyKey: depKey,
      dependencyLabel: label,
      owner: this.ownerName(ownerRef),
      ownerRef,
      note: this.blockerNote().trim(),
      createdByRef: this.currentUserRef(),
      createdAt: this.lastRefresh(),
    };
    this.readinessStore.addBlocker(this.campaignRef, blocker);
    this.blockerDialogOpen.set(false);
    this.showSuccess(this.campaignRef, `Blocker on ${label}`, 'Resolve the blocker or Validate readiness');
  }
  protected removeBlocker(id: string): void {
    if (!this.permissions().assignBlocker) return;
    this.readinessStore.removeBlocker(this.campaignRef, id);
    this.syncSnapshot();
  }

  // ================= Request approval =================
  protected readonly requestDialogOpen = signal(false);
  protected readonly exceptionRecorded = signal(false);
  protected readonly exceptionReason = signal('');
  protected readonly requestTouched = signal(false);
  /** Eligible when the minimum derived gate (budget + tracking) passes, OR an authorised exception is recorded. */
  protected readonly requestApprovalEligible = computed(() => this.minimumGateMet() || this.exceptionRecorded());
  protected readonly requestReasonValid = computed(
    () => !this.exceptionRecorded() || this.exceptionReason().trim().length >= 10,
  );

  protected openRequestDialog(): void {
    this.closeActionsMenu();
    if (!this.permissions().requestApproval) return;
    this.exceptionRecorded.set(false);
    this.exceptionReason.set('');
    this.requestTouched.set(false);
    this.requestDialogOpen.set(true);
  }
  protected cancelRequest(): void {
    this.requestDialogOpen.set(false);
  }
  protected confirmRequest(): void {
    this.requestTouched.set(true);
    if (!this.requestApprovalEligible() || !this.requestReasonValid()) {
      this.uiState.set('validation');
      return;
    }
    if (this.isStale()) {
      this.requestDialogOpen.set(false);
      this.uiState.set('conflict');
      return;
    }
    const rec = this.readiness();
    if (rec && rec.requestState !== 'Draft') {
      // A launch request already exists — duplicate, not a silent re-submit.
      this.requestDialogOpen.set(false);
      this.uiState.set('duplicate');
      return;
    }
    this.readinessStore.update(this.campaignRef, {
      requestState: 'Submitted',
      requestedByRef: this.currentUserRef(),
      requestedByName: this.currentUserName(),
      requestedAt: this.lastRefresh(),
    });
    this.requestDialogOpen.set(false);
    this.showSuccess(this.campaignRef, 'Submitted — awaiting Approve launch', 'An independent approver must Approve launch');
  }

  // ================= Approve launch =================
  protected readonly approveDialogOpen = signal(false);
  protected readonly approveReason = signal('');
  protected readonly approveReasonMin = 10;
  protected readonly approveReasonMax = 2000;
  protected readonly approveTouched = signal(false);
  protected readonly approveReasonCount = computed(() => this.approveReason().trim().length);
  protected readonly approveReasonValid = computed(() => {
    const len = this.approveReason().trim().length;
    return len >= this.approveReasonMin && len <= this.approveReasonMax;
  });

  /** The launch lifecycle target from the current state. */
  private nextLaunchState(current: CampaignStatus): CampaignStatus | null {
    switch (current) {
      case 'Draft':
      case 'Submitted':
      case 'Approved':
        return 'Scheduled';
      case 'Scheduled':
        return 'Active';
      default:
        return null; // Active / Paused / Closing / Closed / Cancelled are not launch transitions.
    }
  }
  protected readonly launchTarget = computed<CampaignStatus | null>(() => {
    const c = this.campaign();
    return c ? this.nextLaunchState(c.status) : null;
  });
  /** True when the acting session is the one that requested approval — blocks self-approval. */
  protected readonly isOwnRequest = computed(() => {
    const rec = this.readiness();
    return !!rec?.requestedByRef && rec.requestedByRef === this.currentUserRef();
  });
  protected readonly approveLaunchAllowed = computed(() => {
    const rec = this.readiness();
    // Enabled as soon as approval has been requested (Submitted) — a campaign that has
    // no further launch transition (e.g. already Active) is still approvable and simply
    // keeps its current lifecycle state.
    return !!rec && rec.requestState === 'Submitted' && this.permissions().approveLaunch;
  });
  protected approveDisabledReason(): string {
    const rec = this.readiness();
    if (!this.permissions().approveLaunch) return 'Approve launch requires the independent-approver permission.';
    if (!rec || rec.requestState !== 'Submitted') return 'Approve launch is only available after Request approval.';
    return '';
  }

  protected openApproveDialog(): void {
    this.closeActionsMenu();
    if (!this.approveLaunchAllowed()) return;
    if (this.isStale()) {
      this.uiState.set('conflict');
      return;
    }
    this.approveReason.set('');
    this.approveTouched.set(false);
    this.approveDialogOpen.set(true);
  }
  protected cancelApprove(): void {
    this.approveDialogOpen.set(false);
  }
  protected confirmApprove(): void {
    this.approveTouched.set(true);
    if (!this.approveReasonValid()) return;
    if (this.isStale()) {
      this.approveDialogOpen.set(false);
      this.uiState.set('conflict');
      return;
    }
    const target = this.launchTarget();
    // Advance the CAMPAIGN lifecycle in the shared store when a launch transition exists;
    // otherwise the campaign keeps its current state and only the readiness record advances.
    if (target) {
      this.campaignStore.update(this.campaignRef, { status: target });
    }
    this.readinessStore.update(this.campaignRef, {
      requestState: 'Approved',
      approvedByRef: this.currentUserRef(),
      approvedByName: this.currentUserName(),
      approvedAt: this.lastRefresh(),
      decisionReason: this.approveReason().trim(),
    });
    this.approveDialogOpen.set(false);
    this.showSuccess(this.campaignRef, target ?? this.lifecycleState(), 'Monitor the campaign from Campaign detail');
  }

  // ================= Return to draft =================
  protected readonly returnDialogOpen = signal(false);
  protected readonly returnReason = signal('');
  protected readonly returnReasonMin = 10;
  protected readonly returnReasonMax = 2000;
  protected readonly returnTouched = signal(false);
  protected readonly returnReasonCount = computed(() => this.returnReason().trim().length);
  protected readonly returnReasonValid = computed(() => {
    const len = this.returnReason().trim().length;
    return len >= this.returnReasonMin && len <= this.returnReasonMax;
  });
  protected readonly returnToDraftAllowed = computed(() => {
    const c = this.campaign();
    return (
      !!c &&
      this.permissions().returnToDraft &&
      !['Draft', 'Closed', 'Cancelled'].includes(c.status)
    );
  });
  protected returnDisabledReason(): string {
    const c = this.campaign();
    if (!this.permissions().returnToDraft) return 'Return to draft requires the return-to-draft permission.';
    if (!c) return '';
    if (c.status === 'Draft') return 'This campaign is already in Draft.';
    if (['Closed', 'Cancelled'].includes(c.status)) return `A ${c.status} campaign cannot be returned to draft.`;
    return '';
  }

  protected openReturnDialog(): void {
    this.closeActionsMenu();
    if (!this.returnToDraftAllowed()) return;
    if (this.isStale()) {
      this.uiState.set('conflict');
      return;
    }
    this.returnReason.set('');
    this.returnTouched.set(false);
    this.returnDialogOpen.set(true);
  }
  protected cancelReturn(): void {
    this.returnDialogOpen.set(false);
  }
  protected confirmReturn(): void {
    this.returnTouched.set(true);
    if (!this.returnReasonValid()) return;
    if (this.isStale()) {
      this.returnDialogOpen.set(false);
      this.uiState.set('conflict');
      return;
    }
    // Regress the CAMPAIGN lifecycle to Draft in the shared store — visible on the other campaign screens.
    this.campaignStore.update(this.campaignRef, { status: 'Draft' });
    this.readinessStore.update(this.campaignRef, {
      requestState: 'Draft',
      requestedByRef: null,
      requestedByName: null,
      requestedAt: null,
      approvedByRef: null,
      approvedByName: null,
      approvedAt: null,
      decisionReason: this.returnReason().trim(),
    });
    this.returnDialogOpen.set(false);
    this.showSuccess(this.campaignRef, 'Draft', 'Resolve blockers, then Request approval again');
  }

  // ================= Validation summary + focus =================
  /** The offending field(s) for the page-level Validation state — drives the linked error summary. */
  protected readonly validationErrors = computed<readonly { label: string; fieldId: string; message: string }[]>(() => {
    const errors: { label: string; fieldId: string; message: string }[] = [];
    const t = this.plannedLaunchTime();
    if (!t) {
      errors.push({ label: 'Planned launch time', fieldId: 'crc-planned-launch', message: 'Enter Planned launch time.' });
    } else if (this.launchTimeInPast(t)) {
      errors.push({
        label: 'Planned launch time',
        fieldId: 'crc-planned-launch',
        message: 'Review Planned launch time. The value does not meet the stated format or range.',
      });
    }
    return errors;
  });
  /** Focus the first invalid field after correction. */
  protected focusField(fieldId: string): void {
    const el = document.getElementById(fieldId);
    if (el) {
      this.uiState.set('ready');
      setTimeout(() => (el as HTMLElement).focus(), 0);
    }
  }

  // ================= Conflict recovery =================
  protected reviewLatest(): void {
    this.syncSnapshot();
    this.uiState.set('ready');
  }
}
