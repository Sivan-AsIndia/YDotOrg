import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';

import { ClickOutsideDirective } from '../../../../Shared/directives/click-outside';
import { CampaignStatus } from '../../../../Shared/models/campaign.model';
import {
  Blocker,
  CrcUiState,
  ReadinessOwnerOption,
} from '../../../../Shared/models/campaign-readiness.model';
import { CampaignStoreService } from '../../../../Shared/services/campaign-store.service';
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

  /** A blocker's raised-at instant as a person reads it. The API sends a full ISO timestamp. */
  protected raisedWhen(value: string): string {
    if (!value) {
      return '—';
    }

    const when = new Date(value);

    return Number.isNaN(when.getTime())
      ? value
      : when.toLocaleString('en-IN', {
          day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
        });
  }

  // ================= Effective permissions =================

  /**
   * The server's own Actions menu for this caller, on this campaign, right now.
   *
   * THIS IS THE AUTHORITY, and the contract on `CampaignReadiness.permittedActions` says so in as
   * many words. Deciding the menu from permission codes in the browser cannot work, because two
   * of the three inputs are not knowable here:
   *
   *   - THE CAMPAIGN'S STATUS. "Request approval" applies to a Draft campaign and to nothing
   *     else. A campaign already Submitted has been requested; the next act is somebody else's
   *     approval. The browser's gate never looked at status, so on a Submitted campaign it left
   *     Request approval greyed out and explained it with the wrong reason entirely - "Budget
   *     approval and Tracking readiness must pass" - when the true answer was "this has already
   *     been submitted".
   *   - SEGREGATION OF DUTIES. `Campaign.CanBeApprovedBy` refuses the person who submitted it.
   *     The browser does not know who that was until the record loads, and must not guess.
   *
   * The permission codes remain as the fallback for the moment before the readiness record
   * arrives, so the menu does not flicker from empty to populated on every load.
   */
  private readonly serverActions = computed<readonly string[] | null>(() => {
    const verdict = this.checklistStore.verdict(this.campaignRef);
    return verdict?.permittedActions ?? null;
  });

  /**
   * The server's own launch verdict — `canLaunch`, `willActivateAutomatically` and
   * `automaticActivationDate` — read here rather than recomputed from the checklist items.
   *
   * `canLaunch` ALREADY FOLDS IN EVERYTHING: required outstanding checks, open blockers, and the
   * Organisation's own "allow launch with outstanding checks" setting — three things a screen
   * would otherwise have to reproduce, and would eventually reproduce wrong. See the doc comment
   * on `CampaignReadiness` for why a red checklist next to `willActivateAutomatically: true` does
   * NOT mean the launch is held back: automatic activation on the start date does not wait on
   * this checklist, so the panel that reads this must say that plainly rather than implying a
   * gate that is not actually there.
   */
  protected readonly readinessVerdict = computed(() => this.checklistStore.verdict(this.campaignRef));

  private serverAllows(action: string, fallback: boolean): boolean {
    const actions = this.serverActions();
    return actions === null ? fallback : actions.includes(action);
  }

  protected readonly permissions = computed(() => {
    // APPROVE LAUNCH IS CAMPAIGN APPROVAL, reached from this screen rather than from the detail
    // one, so it is gated on `cam.campaigns.approve`. It used to check `cam.readiness.approve` -
    // a code that gates no endpoint anywhere and has since been retired - and Request approval
    // used to check `cam.readiness.edit`, which is the code for editing a checklist item and has
    // nothing to do with submitting a campaign.
    const canApprove = this.currentUser.hasPermission('cam.campaigns.approve');

    return {
      view: this.currentUser.hasPermission('cam.readiness.view'),

      // PASSING A CHECK AND FAILING ONE ARE DIFFERENT PERMISSIONS, and the screen has to know
      // both. `cam.readiness.pass` is declared an APPROVE action, so an Initiator does not hold
      // it; `cam.readiness.fail` is an Operate action, which both roles do. The menu offered
      // both verbs to everybody, and the half nobody could use came back 403.
      validate: this.currentUser.hasPermission('cam.readiness.pass'),
      recordFailure: this.currentUser.hasPermission('cam.readiness.fail'),

      // RAISING A BLOCKER AND CLEARING ONE ARE TWO PERMISSIONS. They were one, which meant
      // whoever could flag a problem could also wave their own flag away - and an open blocker is
      // exactly what stops a check being passed, so holding both emptied the mechanism. The maker
      // raises; the checker resolves.
      assignBlocker: this.currentUser.hasPermission('cam.readiness.manage-blockers'),
      addCheck: this.serverAllows(
        'AddCheck', this.currentUser.hasPermission('cam.readiness.create')),

      // AN APPROVER NEVER SEES "REQUEST APPROVAL". They hold the approval, so a request they
      // raised is one they must then refuse to approve - the platform's four-eyes rule forbids
      // one person doing both halves. This is why an Organisation Administrator was being shown
      // Request approval with Approve launch greyed out beside it: exactly backwards.
      requestApproval: this.serverAllows(
        'RequestApproval',
        this.currentUser.hasPermission('cam.campaigns.submit') && !canApprove),

      approveLaunch: this.serverAllows('ApproveLaunch', canApprove),
    };
  });

  /**
   * What this ROLE holds, before any question of the record's state.
   *
   * THE TWO ARE DIFFERENT QUESTIONS AND THE SCREEN NOW ASKS THEM SEPARATELY.
   * `permissions()` above blends them - it reads the server's `permittedActions`, which encodes
   * the permission AND the campaign's status AND the four-eyes rule - and every action on this
   * screen was rendered from that one answer, disabled, with a sentence underneath. So an
   * Initiator was shown "Approve launch" greyed out on every campaign they would ever open, and
   * an Approver was shown "Request approval" the same way.
   *
   * The rule is now: a permission this role does not hold HIDES the item, because it is never
   * going to become available and a permanently dead control is noise. A state that does not
   * suit yet SHOWS it disabled with the reason, because that one changes.
   */
  protected readonly capabilities = computed(() => {
    const canApprove = this.currentUser.hasPermission('cam.campaigns.approve');

    return {
      addCheck: this.currentUser.hasPermission('cam.readiness.create'),
      editCheck: this.currentUser.hasPermission('cam.readiness.edit'),
      deleteCheck: this.currentUser.hasPermission('cam.readiness.delete'),
      pass: this.currentUser.hasPermission('cam.readiness.pass'),
      fail: this.currentUser.hasPermission('cam.readiness.fail'),
      assignBlocker: this.currentUser.hasPermission('cam.readiness.manage-blockers'),
      resolveBlocker: this.currentUser.hasPermission('cam.readiness.resolve-blockers'),

      // An approver never raises a launch request: they would be the one approving it, and the
      // four-eyes rule then refuses the pair. This is a property of the ROLE, not of the record,
      // so it belongs on this side of the split.
      requestApproval: this.currentUser.hasPermission('cam.campaigns.submit') && !canApprove,
      approveLaunch: canApprove,
    };
  });

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

  // ================= Checklist aggregation =================
  //
  // EVERY CHECK ON THIS SCREEN IS ONE SOMEBODY ENTERED, and every verdict on it is one somebody
  // recorded. This section used to open with six cards the screen derived for itself — Budget
  // approval and Tracking readiness computed from two other stores, and Public content, Payment,
  // Template and Consent read from a stub standing in for domains that do not exist yet. They
  // caused three separate faults:
  //
  //   - THEIR VERDICTS DID NOT SURVIVE THE PAGE. "Pass" on one of them wrote to a component
  //     signal, because there is no server record to write to: a blocker or a verdict hangs off a
  //     readiness CHECK, and 'budget' is not a check id. So passing them, navigating away and
  //     coming back put every one of them back to Pending, and the same reason made "Assign
  //     blocker" on any of them fail outright.
  //   - THEY HELD THE LAUNCH REQUEST SHUT. Request approval was gated on Budget approval AND
  //     Tracking readiness both passing. Budget & Target Plans is on hold, so no plan can be
  //     allocated, so Budget approval could never leave Pending — and Request approval could
  //     never be enabled for any campaign on the platform without recording an "exception".
  //     The server does not ask for any of this at submit; the readiness gate it does enforce is
  //     on ACTIVATE, and it names the outstanding checks itself.
  //   - THEY DILUTED THE COUNTS. Six cards nobody added, four of them permanently Pending, sat in
  //     the Passed/Failed/Pending/Total tiles and in the readiness meter beside the checks that
  //     had actually been assessed.
  //
  // The counts below are the manually-added checks and nothing else, which is also exactly what
  // the server's own `canLaunch` verdict is computed from.

  protected getPassCount(): number {
    return this.checklistItems().filter((c) => c.status === 'Passed').length;
  }
  protected getFailCount(): number {
    return this.checklistItems().filter((c) => c.status === 'Failed').length;
  }
  protected getPendingCount(): number {
    return this.checklistItems().filter((c) => c.status === 'Pending').length;
  }
  protected getTotalCount(): number {
    return this.checklistItems().length;
  }
  protected getReadinessPct(): number {
    const total = this.getTotalCount();
    return total ? Math.round((this.getPassCount() / total) * 100) : 0;
  }

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
  /**
   * Re-reads the checklist from the server and reports where it stands.
   *
   * IT RELOADS RATHER THAN RECOMPUTING. The old version copied the six derived/stub dependency
   * results into a local array and looked for an 'unknown' among them to decide whether a
   * dependent service was down - which is a question about a stub, not about this campaign. The
   * checks are the server's now, so the honest way to re-validate is to fetch them again; the
   * counts below then reflect what came back.
   */
  protected validateReadiness(): void {
    if (!this.permissions().validate) return;

    this.checklistStore.load(this.campaignRef);
    this.lastRefresh.set('Just now · IST');
    this.lastValidatedAt.set(this.lastRefresh());
    this.uiState.set('ready');
    this.toast.show(
      'Readiness validated',
      `${this.getPassCount()} passed · ${this.getFailCount()} failed · ${this.getPendingCount()} pending of ${this.getTotalCount()} items.`,
      'success',
    );
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

  /**
   * Row "Pass/Completed" — signs a check off.
   *
   * THE TOAST WAITS FOR THE SERVER. Both of these used to announce success on the line after
   * dispatching the call, so a refusal - and `cam.readiness.pass` is an approval permission an
   * Initiator does not hold, so refusals are routine - produced "marked as passed" over a card
   * that had not moved. The card is the truth; the toast now says what the card is going to say.
   */
  protected markChecklistPassed(check: ReadinessCheck): void {
    this.closeChecklistRowMenu();
    if (!this.permissions().validate) return;
    this.recordVerdict(check, 'Passed');
  }
  /** Row "Fail" — records that a check is not ready. A separate permission from passing it. */
  protected markChecklistFailed(check: ReadinessCheck): void {
    this.closeChecklistRowMenu();
    if (!this.permissions().recordFailure) return;
    this.recordVerdict(check, 'Failed');
  }
  private recordVerdict(check: ReadinessCheck, status: 'Passed' | 'Failed'): void {
    const verb = status === 'Passed' ? 'passed' : 'failed';

    this.checklistStore.setStatus(this.campaignRef, check.id, status, undefined, (outcome) => {
      if (!outcome.recorded) {
        this.toast.show(
          'Verdict not recorded',
          outcome.error ?? `${check.name} could not be marked as ${verb}.`,
          'error');
        return;
      }

      this.lastRefresh.set('Just now · IST');
      this.toast.show('Readiness check updated', `${check.name} marked as ${verb}.`, 'success');
    });
  }
  /** Row "Delete" — removes the check entirely. */
  /** Whether this check can actually be removed: Pending, unblocked, and the role holds it. */
  protected deleteCheckAllowed(check: ReadinessCheck): boolean {
    return this.capabilities().deleteCheck && check.status === 'Pending' && !check.openBlocker;
  }
  protected deleteCheckDisabledReason(check: ReadinessCheck): string {
    if (check.status !== 'Pending') {
      return `Only a Pending check can be deleted. This one is ${check.status}, and the verdict on it would go too.`;
    }
    if (!!check.openBlocker) {
      return 'This check has a blocker raised against it. Resolve it before removing the check.';
    }
    return '';
  }

  /**
   * Removes a check from the checklist.
   *
   * IT CALLS THE API NOW, AND REPORTS WHAT HAPPENED. It used to call the store's `removeCheck`,
   * which marked the check "not required for launch" because CAM had no delete - so a check the
   * screen said was "deleted" was still on the list, still attributable, just no longer blocking.
   * CAM has a DELETE for a Pending check now; anything judged is still refused, because deleting
   * it would destroy somebody's verdict along with the question.
   */
  protected deleteChecklistItem(check: ReadinessCheck): void {
    this.closeChecklistRowMenu();
    if (!this.deleteCheckAllowed(check)) return;

    this.checklistStore.deleteCheck(this.campaignRef, check.id, (outcome) => {
      if (!outcome.deleted) {
        this.toast.show('Check not deleted', outcome.error ?? 'That check could not be removed.', 'error');
        return;
      }

      this.lastRefresh.set('Just now · IST');
      this.toast.show('Readiness check deleted', `${check.name} was removed.`, 'success');
    });
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

  /** Open Assign blocker pre-targeted at a specific readiness check. */
  protected openBlockerFor(key: string, label: string): void {
    this.closeChecklistRowMenu();
    if (!this.permissions().assignBlocker) return;

    // The menu disables both of these, and this is the second half of the same rule: a passed
    // check has nothing left to block, and the server keeps at most one open blocker per check.
    const check = this.checklistItems().find((item) => item.id === key);

    if (check && (check.status === 'Passed' || !!check.openBlocker)) return;
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
    // THE DIALOG CLOSES AND THE SUCCESS IS ANNOUNCED ONLY IF THE BLOCKER WAS ACTUALLY RAISED.
    //
    // Both used to happen on the next two lines, unconditionally, while the request was still in
    // flight — and the request is refused outright for every DERIVED dependency card (Budget,
    // Tracking, Public content, …) because their keys are 'budget'/'tracking', not readiness
    // check ids, and a blocker has to hang off a check. So the screen said "Blocker raised",
    // closed, and left no blocker anywhere: not on the launch panel, not on the server, not on
    // the next page load.
    this.readinessStore.addBlocker(this.campaignRef, blocker, (outcome) => {
      if (!outcome.raised) {
        this.toast.show(
          'Blocker not raised',
          outcome.error ?? 'The blocker could not be raised.',
          'error');
        return;
      }

      this.blockerDialogOpen.set(false);
      this.showSuccess(this.campaignRef, `Blocker on ${label}`, 'Resolve the blocker or Validate readiness');
    });
  }
  /**
   * Resolves a blocker.
   *
   * GUARDED ON RESOLVE, NOT ON ASSIGN. It checked the assign permission, which is the code for
   * RAISING one - so anybody who could flag a problem could clear their own flag, and an open
   * blocker is exactly what stops a check being passed.
   */
  protected removeBlocker(id: string): void {
    if (!this.capabilities().resolveBlocker) return;

    this.readinessStore.removeBlocker(this.campaignRef, id, (outcome) => {
      this.syncSnapshot();

      this.toast.show(
        outcome.resolved ? 'Blocker resolved' : 'Blocker not resolved',
        outcome.resolved
          ? 'The check is pending verification again.'
          : (outcome.error ?? 'The blocker could not be resolved.'),
        outcome.resolved ? 'success' : 'error');
    });
  }

  // ================= Request approval =================
  protected readonly requestDialogOpen = signal(false);
  protected readonly requestTouched = signal(false);

  /**
   * Whether the campaign may be sent for approval.
   *
   * THE CHECKLIST DOES NOT HOLD THIS SHUT, and that is a change. It was gated on Budget approval
   * AND Tracking readiness both passing, or an "authorised exception" being recorded in their
   * place. Budget & Target Plans is on hold, so no budget plan can be allocated to any campaign,
   * so Budget approval never left Pending - and Request approval was therefore disabled on every
   * campaign on the platform unless somebody first ticked an exception box and wrote ten
   * characters of justification for a module that does not exist.
   *
   * The server does not ask for any of this at SUBMIT. The readiness gate it does enforce is on
   * ACTIVATE, where a required check that has not passed refuses the transition and names itself
   * in the refusal. Requesting approval is asking a second person to look; whether the checklist
   * is finished is part of what they are being asked to look at.
   *
   * The lifecycle state still governs: only a Draft campaign can be submitted, which is what
   * `requestDisabledReason` and the server both say.
   */
  protected readonly requestApprovalEligible = computed(
    () => this.campaign()?.status === 'Draft',
  );

  /**
   * Why Request approval is unavailable, in the words of whatever is actually blocking it.
   *
   * IT USED TO GIVE ONE REASON FOR TWO CAUSES. The template offered "Budget approval and Tracking
   * readiness must pass" whenever the item was disabled, so a campaign that had ALREADY been
   * submitted - the commonest case by far, since submitting is what disables it - was explained
   * with a sentence about budgets. The status check comes first here because it is the one the
   * person can do nothing about by working the checklist harder.
   */
  protected requestDisabledReason(): string {
    const status = this.campaign()?.status;

    if (status && status !== 'Draft') {
      return this.currentUser.hasPermission('cam.campaigns.approve')
        ? 'Approvers do not raise launch requests; use Approve launch.'
        : `Already ${status.toLowerCase()} — a launch request has been raised for this campaign.`;
    }

    return '';
  }

  protected openRequestDialog(): void {
    this.closeActionsMenu();
    if (!this.permissions().requestApproval) return;
    this.requestTouched.set(false);
    this.requestDialogOpen.set(true);
  }
  protected cancelRequest(): void {
    this.requestDialogOpen.set(false);
  }
  protected confirmRequest(): void {
    this.requestTouched.set(true);
    if (!this.requestApprovalEligible()) {
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
    // THE REQUEST GOES TO THE SERVER. This used to be `readinessStore.update(...)` and nothing
    // else - and that method patches the LOCAL record only, as its own contract says: the request
    // state belongs to the campaign lifecycle endpoint, which is what enforces segregation of
    // duties. So the checklist said "Submitted — awaiting Approve launch", the campaign stayed
    // in Draft on the server, `load()` re-derived the state from the campaign on the next visit,
    // and everything the person had done went back to Pending. Nobody's approval queue ever saw
    // the campaign either, which is why Approve launch could never become available.
    this.campaignStore.submitForApproval(
      this.campaignRef,
      this.currentUser.role(),
      this.currentUserRef(),
      (outcome) => {
        if (!outcome.applied) {
          this.toast.show(
            'Approval not requested',
            outcome.error ?? 'The launch request could not be raised.',
            'error');
          return;
        }

        this.readinessStore.update(this.campaignRef, {
          requestState: 'Submitted',
          requestedByRef: this.currentUserRef(),
          requestedByName: this.currentUserName(),
          requestedAt: this.lastRefresh(),
        });
        this.requestDialogOpen.set(false);
        this.showSuccess(
          this.campaignRef,
          'Submitted — awaiting Approve launch',
          'An independent approver must Approve launch');
      },
    );
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
  /**
   * Why Approve launch is unavailable.
   *
   * IT NO LONGER EXPLAINS A MISSING PERMISSION. A caller without `cam.campaigns.approve` is not
   * shown the item at all, so the only reason left to give is the one that can change: nobody has
   * requested the launch yet.
   */
  protected approveDisabledReason(): string {
    const rec = this.readiness();
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

    const applyLocally = () => {
      this.readinessStore.update(this.campaignRef, {
        requestState: 'Approved',
        approvedByRef: this.currentUserRef(),
        approvedByName: this.currentUserName(),
        approvedAt: this.lastRefresh(),
        decisionReason: this.approveReason().trim(),
      });
      this.approveDialogOpen.set(false);
      this.showSuccess(
        this.campaignRef, target ?? this.lifecycleState(), 'Monitor the campaign from Campaign detail');
    };

    // No launch transition to make - the campaign is already running - so only the readiness
    // record advances.
    if (!target) {
      applyLocally();
      return;
    }

    // SETSTATUS, NOT UPDATE. `campaignStore.update` is the CONTENT edit: a PUT whose body carries
    // the campaign's fields and no status at all. Writing `{ status: target }` through it set the
    // status on the local row, saved nothing of the kind, and then the refresh that follows the
    // PUT replaced that row with the server's - still Submitted. The status appeared to change
    // and then silently changed back, which is exactly what "Approve launch — status not
    // updated" looks like from the outside. `setStatus` routes to the lifecycle endpoint that
    // owns the transition.
    this.campaignStore.setStatus(this.campaignRef, target, (outcome) => {
      if (!outcome.applied) {
        this.toast.show(
          'Launch not approved',
          outcome.error ?? 'The campaign could not be approved.',
          'error');
        return;
      }

      applyLocally();
    });
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
