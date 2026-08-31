import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

/**
 * SCR-UX-005 — Standard record detail.
 *
 * Faithful implementation of section 4.5 (and its identical companion 7.5) of the
 * YDot Practical UI/UX Generation Specification. Every region (4.5.1), field
 * (4.5.2), action (4.5.3), UI state (4.5.4), responsive/accessibility rule (4.5.5)
 * and validation/confirmation pattern (4.5.6) below maps directly to the controlled
 * contract. No functionality outside that contract is added; nothing listed is left out.
 *
 *  Route            : /workspace/standard-record-detail
 *  Purpose          : Display status, summary, related tabs, activity, documents and actions.
 *  Primary users    : Module users
 *  View permission  : ux.standard-record-detail.view
 *  Data scope       : Only records inside the actor's active organisation, campaign,
 *                     geography, warehouse, queue, assignment or explicit record scope.
 *  Primary action   : View
 *  History rule     : Delete is available only for an unused draft with no downstream
 *                     reference; otherwise use the domain lifecycle action.
 *  Theme            : Dark Meadow task surface; warm-paper data rows; calm-blue
 *                     information; antique-gold focus/progress.
 */

/** The eight required UI states from 4.5.4, plus the settled "ready" surface. */
type UiState =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

/** Effective permission set for the acting Module user (4.5.3). */
interface EffectivePermissions {
  readonly view: boolean; // ux.standard-record-detail.view
  readonly edit: boolean; // ux.standard-record-detail.edit
  readonly submit: boolean; // ux.standard-record-detail.submit
  readonly approve: boolean; // ux.standard-record-detail.approve-according-to-state
}

/** A related-history tab; each has its own permission and scope enforcement (4.5.1). */
interface RelatedTab {
  readonly key: string;
  readonly label: string;
  readonly count: number | null;
  readonly permitted: boolean;
}

/** A single activity / timeline entry (4.5.1 Related and history; 4.5.2 Activity). */
interface ActivityStep {
  readonly title: string;
  readonly time: string;
  readonly actor: string;
  readonly state: StepState;
}
type StepState = 'Completed' | 'In Progress' | 'Pending';

/** A read-only linked record (4.5.2 Related records). */
interface RelatedRecord {
  readonly primary: string;
  readonly secondary: string;
  readonly kind: string;
}

/** A read-only document held within record scope (4.5.2 Documents — Confidential). */
interface RecordDocument {
  readonly name: string;
  readonly meta: string;
  readonly added: string;
}

/** A read-only approval / decision entry (4.5.1 Decision / review; 4.5.3 Approve). */
interface ApprovalEntry {
  readonly stage: string;
  readonly decision: string;
  readonly authority: string;
  readonly time: string;
  readonly state: StepState;
}

@Component({
  selector: 'app-standard-record-detail',
  imports: [CommonModule, FormsModule],
  templateUrl: './standard-record-detail.html',
  styleUrl: './standard-record-detail.css',
})
export class StandardRecordDetailComponent {
  // ================= Task header — contract fields (4.5.1 + 4.5.2) =================

  /** Record reference — server-derived, immutable in this view (4.5.2). */
  protected readonly recordReference = 'DONATION-2026-0921';
  /** Title — server-derived, immutable (4.5.2). */
  protected readonly title = 'Corporate Donation';
  /** Status — server-derived current lifecycle state (4.5.2 Status). */
  protected readonly lifecycleState = 'Completed';
  /** Owner — accountable person, server-derived (4.5.2 Owner). */
  protected readonly owner = 'Arun Kumar';
  protected readonly ownerRole = 'Programme Officer';
  /** Scope — the effective data scope this record sits inside (4.5.2 Scope; 4.5 Data scope). */
  protected readonly scope = 'GreenSol India Pvt Ltd · Programme Team · Maharashtra';
  /** Last updated — server-derived freshness (4.5.2 Last updated). */
  protected readonly lastUpdated = signal('22 Jul 2026, 09:15 AM · IST');
  protected readonly lastUpdatedBy = 'Meera Nair · Programme Manager';
  /** Concurrency version — optimistic-lock token used for conflict detection (4.5.2). */
  protected readonly concurrencyVersion = 'Version 7';
  /** Created evidence shown in the header (freshness + owner). */
  protected readonly createdOn = '18 Jul 2026, 10:24 AM';

  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';

  /** Effective permissions decided server-side; the client mirrors the same decision (4.5.3, 4.5.7). */
  protected readonly permissions: EffectivePermissions = {
    view: true,
    edit: true,
    submit: true,
    approve: true,
  };

  /** Lifecycle states in which Edit is permitted (4.5.3 — permitted lifecycle state). */
  private readonly editPermittedStates = ['Draft', 'Submitted', 'Approved', 'In Progress', 'Completed'];
  /** Lifecycle states in which Submit is permitted (4.5.3). */
  private readonly submitPermittedStates = ['Draft', 'Returned'];
  /**
   * Approve according to state acts on the record's open review item. The header state is
   * "Completed", but the acknowledgement step is still "Pending review" (see timeline), so an
   * independent approver may decide it. Allowed states per 4.5.3: Submitted / Pending review.
   */
  protected readonly reviewState = signal<'Pending review' | 'Decided'>('Pending review');

  // ================= Context and filters (4.5.1) =================

  /** Active scope label — server-qualified; totals below are qualified by it (4.5.1). */
  protected readonly activeScope = 'FY 2026-27 · GreenSol India Pvt Ltd';
  /** Search within the record's related content (4.5.1 Context and filters — search). */
  protected readonly searchTerm = signal('');
  /** Saved filter over related content (4.5.1 — saved filter). */
  protected readonly savedViews = ['Full record (Default)', 'Financials focus', 'Approvals focus'];
  protected readonly savedView = signal(this.savedViews[0]);

  // ================= Related tabs — task-oriented groups (4.5.1 Main work) =================

  protected readonly relatedTabs: readonly RelatedTab[] = [
    { key: 'overview', label: 'Overview', count: null, permitted: true },
    { key: 'details', label: 'Details', count: null, permitted: true },
    { key: 'donor', label: 'Donor & Organisation', count: null, permitted: true },
    { key: 'financials', label: 'Financials', count: null, permitted: true },
    { key: 'documents', label: 'Documents', count: 8, permitted: true },
    { key: 'activity', label: 'Activity', count: null, permitted: true },
    { key: 'approvals', label: 'Approvals', count: 3, permitted: true },
    { key: 'related', label: 'Related Records', count: 12, permitted: true },
  ];
  protected readonly visibleTabs = computed(() => this.relatedTabs.filter((t) => t.permitted));
  protected readonly activeTab = signal<string>('overview');
  protected selectTab(key: string): void {
    this.activeTab.set(key);
  }

  // ================= Record summary display (4.5.1 Main work — Display status, summary) =================

  /** The record's summary attributes. All read-only, server-derived display content (4.5.2 Summary). */
  protected readonly referenceNumber = 'REF-2026-00588';
  protected readonly purpose =
    'Supporting blind stick distribution for visually impaired individuals across rural communities.';
  protected readonly priority = 'Medium';
  protected readonly campaign = 'Blind Stick Distribution Drive Jul 2026';
  protected readonly donorOrganisation = 'GreenSol India Pvt Ltd';
  protected readonly donationAmount = 1236500;
  protected readonly receivedAmount = 1236500;
  protected readonly utilisedAmount = 425600;
  protected readonly pendingAllocation = 810900;
  protected readonly transactionDate = '21 Jul 2026';

  /** Notes — additional summary content (4.5.2 Summary). */
  protected readonly note = {
    body: 'The donor has requested quarterly impact reports and priority visibility in future distribution drives.',
    author: 'Meera Nair',
    time: '22 Jul 2026, 09:15 AM',
  };

  /** Allocation percentages (display only; derived from the read-only amounts). */
  protected readonly receivedPct = computed(() =>
    this.pct(this.receivedAmount, this.donationAmount),
  );
  protected readonly utilisedPct = computed(() => this.pct(this.utilisedAmount, this.donationAmount));
  protected readonly pendingPct = computed(() => this.pct(this.pendingAllocation, this.donationAmount));

  // ================= Activity / timeline (4.5.2 Activity; 4.5.1 Related and history) =================

  protected readonly activity: readonly ActivityStep[] = [
    { title: 'Donation Created', time: '18 Jul 2026, 10:24 AM', actor: 'Arun Kumar', state: 'Completed' },
    { title: 'Submitted for Approval', time: '18 Jul 2026, 11:05 AM', actor: 'Arun Kumar', state: 'Completed' },
    { title: 'Approved', time: '19 Jul 2026, 09:32 AM', actor: 'Ritika Sharma', state: 'Completed' },
    { title: 'Funds Received', time: '21 Jul 2026, 06:30 PM', actor: 'Finance Team', state: 'Completed' },
    { title: 'Acknowledgement Sent', time: '22 Jul 2026, 09:15 AM', actor: 'Meera Nair', state: 'In Progress' },
  ];

  // ================= Documents (4.5.2 Documents — Confidential) =================

  protected readonly documents: readonly RecordDocument[] = [
    { name: 'Acknowledgement Letter', meta: 'PDF · 214 KB', added: '22 Jul 2026 · Meera Nair' },
    { name: 'Payment Transaction TRX-77821', meta: 'PDF · 96 KB', added: '21 Jul 2026 · Finance Team' },
    { name: 'Donation Agreement', meta: 'PDF · 480 KB', added: '18 Jul 2026 · Arun Kumar' },
    { name: 'Corporate CSR Mandate', meta: 'PDF · 302 KB', added: '18 Jul 2026 · Arun Kumar' },
    { name: '80G Receipt', meta: 'PDF · 128 KB', added: '21 Jul 2026 · Finance Team' },
    { name: 'Impact Report — Q2', meta: 'PDF · 1.2 MB', added: '20 Jul 2026 · Meera Nair' },
    { name: 'KYC — GreenSol India', meta: 'PDF · 220 KB', added: '18 Jul 2026 · Compliance' },
    { name: 'Bank Advice — NEFT', meta: 'PDF · 74 KB', added: '21 Jul 2026 · Finance Team' },
  ];

  // ================= Related records (4.5.2 Related records) =================

  protected readonly related: readonly RelatedRecord[] = [
    { primary: 'Blind Stick Distribution Drive Jul 2026', secondary: 'Campaign', kind: 'campaign' },
    { primary: 'GreenSol India Pvt Ltd', secondary: 'Donor / Organisation', kind: 'donor' },
    { primary: 'DONATION-2026-0918', secondary: 'Previous Donation', kind: 'donation' },
    { primary: 'Acknowledgement Letter', secondary: 'Document', kind: 'document' },
    { primary: 'Payment Transaction TRX-77821', secondary: 'Finance Record', kind: 'finance' },
  ];
  protected readonly relatedTotal = 12;

  // ================= Decision / review + Approvals (4.5.1 + 4.5.3) =================

  protected readonly approvals: readonly ApprovalEntry[] = [
    { stage: 'Programme review', decision: 'Approved', authority: 'Ritika Sharma · Programme Head', time: '19 Jul 2026, 09:32 AM', state: 'Completed' },
    { stage: 'Finance verification', decision: 'Funds confirmed', authority: 'Finance Team · Controller', time: '21 Jul 2026, 06:30 PM', state: 'Completed' },
    { stage: 'Acknowledgement review', decision: 'Awaiting decision', authority: 'Meera Nair · Programme Manager', time: 'Pending review', state: 'In Progress' },
  ];

  /** Before-and-after / evidence for the open decision (4.5.1 Decision / review). */
  protected readonly decisionReview = computed(() => ({
    before: 'Acknowledgement — In Progress',
    after: 'Acknowledgement — Sent',
    effectivePermission: 'ux.standard-record-detail.approve-according-to-state',
    evidence: `${this.recordReference} · ${this.concurrencyVersion} · ${this.lastUpdated()}`,
    reason: 'Confirm donor acknowledgement dispatch',
    resultingState: 'Acknowledgement Sent · confirmed result shown',
  }));

  // ================= Access & visibility (4.5.2 Visibility; 4.5.5 privacy) =================

  protected readonly visibleTo = ['Programme Team', 'Finance Team', 'Executive Sponsor'];
  protected readonly tags = ['Corporate', 'Blind Stick', 'Education', 'High Impact', 'FY 2026-27'];

  // ================= Actions, eligibility and result (4.5.3) =================

  protected readonly actionsMenuOpen = signal(false);
  protected toggleActionsMenu(): void {
    this.actionsMenuOpen.update((v) => !v);
  }

  /** Edit — permitted only with permission and permitted lifecycle state (4.5.3). */
  protected readonly editAllowed = computed(
    () => this.permissions.edit && this.editPermittedStates.includes(this.lifecycleState) && this.uiState() !== 'no-access',
  );
  /** Submit — permitted lifecycle state (4.5.3). */
  protected readonly submitAllowed = computed(
    () => this.permissions.submit && this.submitPermittedStates.includes(this.lifecycleState) && this.uiState() !== 'no-access',
  );
  /** Approve according to state — independent approver, Submitted / Pending review only (4.5.3). */
  protected readonly approveAllowed = computed(
    () => this.permissions.approve && this.reviewState() === 'Pending review' && this.uiState() !== 'no-access',
  );

  // ----- Edit action (4.5.3) -----
  protected readonly editDialogOpen = signal(false);
  protected requestEdit(): void {
    if (!this.editAllowed()) {
      return;
    }
    this.actionsMenuOpen.set(false);
    this.editDialogOpen.set(true);
  }
  protected cancelEdit(): void {
    this.editDialogOpen.set(false);
  }
  /** Refresh only the authorised record in scope; show the confirmed result (4.5.3 Edit). */
  protected confirmEdit(): void {
    this.editDialogOpen.set(false);
    this.lastUpdated.set('Today, just now · IST');
    this.successRef.set(this.recordReference);
    this.uiState.set('success');
  }

  // ----- Submit action (4.5.3 — idempotent) -----
  protected readonly submitDialogOpen = signal(false);
  protected requestSubmit(): void {
    if (!this.submitAllowed()) {
      return;
    }
    this.actionsMenuOpen.set(false);
    this.submitDialogOpen.set(true);
  }
  protected cancelSubmit(): void {
    this.submitDialogOpen.set(false);
  }
  /** Execute idempotently; show stable reference, committed result and next safe action (4.5.3 Submit). */
  protected confirmSubmit(): void {
    this.submitDialogOpen.set(false);
    this.successRef.set(this.recordReference);
    this.uiState.set('success');
  }

  // ----- Approve according to state — high-risk decision (4.5.3 + 4.5.6 high-risk) -----
  protected readonly approveDialogOpen = signal(false);
  protected readonly approveReason = signal('');
  protected readonly approveReasonMin = 10;
  protected readonly approveReasonMax = 2000;
  protected readonly approveReasonValid = computed(() => {
    const len = this.approveReason().trim().length;
    return len >= this.approveReasonMin && len <= this.approveReasonMax;
  });
  protected readonly approveReasonCount = computed(() => this.approveReason().trim().length);

  protected requestApprove(): void {
    if (!this.approveAllowed()) {
      return;
    }
    this.actionsMenuOpen.set(false);
    this.approveReason.set('');
    this.approveDialogOpen.set(true);
  }
  protected cancelApprove(): void {
    this.approveDialogOpen.set(false);
  }
  /** Record decision, independent authority, reason, effective version, time and resulting state (4.5.3). */
  protected confirmApprove(): void {
    if (!this.approveReasonValid()) {
      return;
    }
    this.approveDialogOpen.set(false);
    this.reviewState.set('Decided');
    this.successRef.set(this.recordReference);
    this.uiState.set('success');
  }

  // ================= UI state demonstrability (4.5.4 / 4.5.7) =================

  protected readonly uiState = signal<UiState>('ready');
  protected readonly successRef = signal(this.recordReference);
  protected readonly uiStates: readonly UiState[] = [
    'ready',
    'loading',
    'empty',
    'validation',
    'duplicate',
    'no-access',
    'conflict',
    'dependency-failure',
    'success',
  ];
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  // ================= Persistent outcome (4.5.1) =================

  /** A toast may support but cannot replace this persistent confirmation (4.5.1 Persistent outcome). */
  protected readonly persistentOutcome = computed(() => ({
    reference: this.recordReference,
    state: this.lifecycleState,
    effectiveTime: this.lastUpdated(),
    downstreamStatus:
      this.reviewState() === 'Pending review'
        ? 'Acknowledgement dispatch pending'
        : 'All dependencies confirmed',
    owner: `${this.owner} · ${this.ownerRole}`,
    nextAction:
      this.reviewState() === 'Pending review'
        ? 'Approve acknowledgement according to state'
        : 'No action required',
  }));

  // ================= Formatting helpers =================

  protected formatAmount(value: number): string {
    return value.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }
  protected stepClass(state: StepState): string {
    switch (state) {
      case 'Completed':
        return 'step-done';
      case 'In Progress':
        return 'step-progress';
      case 'Pending':
        return 'step-pending';
    }
  }
  protected badgeClass(state: StepState | string): string {
    switch (state) {
      case 'Completed':
        return 'badge-good';
      case 'In Progress':
        return 'badge-info';
      case 'Pending':
        return 'badge-warn';
      default:
        return 'badge-info';
    }
  }
  private pct(part: number, whole: number): string {
    if (!whole) {
      return '0%';
    }
    const v = (part / whole) * 100;
    return `${Number.isInteger(v) ? v : v.toFixed(1)}%`;
  }
}
