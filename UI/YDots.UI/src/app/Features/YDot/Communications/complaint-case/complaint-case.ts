import { Component, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

/* ------------------------------------------------------------------ */
/*  Domain model                                                       */
/* ------------------------------------------------------------------ */

export type LifecycleState =
  | 'new'
  | 'acknowledged'
  | 'investigating'
  | 'resolved'
  | 'closed'
  | 'reopened';

// 'donor-care' and 'supervisor' were job titles IAM no longer issues. A complaint is worked by
// the maker and decided by the checker, so they map to INITIATOR and APPROVER; 'unauthorised'
// stays because it previews the no-access state rather than naming a role.
export type EffectiveRole = 'tenant-admin' | 'initiator' | 'approver' | 'unauthorised';

export type PreviewScenario =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

export type ActionKey = 'acknowledge' | 'assign' | 'investigate' | 'resolve' | 'reopen';

interface StatusHistoryEntry {
  state: LifecycleState;
  label: string;
  timestamp: string;
  actor: string;
}

interface EvidenceItem {
  label: string;
  type: string;
  addedBy: string;
  timestamp: string;
}

interface LinkedRecord {
  label: string;
  reference: string;
  type: string;
}

interface ActivityEntry {
  label: string;
  timestamp: string;
  actor: string;
}

interface ComplaintCase {
  reference: string;
  complaintTypeCode: string;
  complaintTypeLabel: string;
  severity: 'critical' | 'high' | 'medium' | 'low';
  receivedChannel: string;
  receivedAt: string;
  complainant: string;
  summary: string;
  owner: string;
  ownerInitials: string;
  slaDueAt: string;
  slaBreached: boolean;
  investigationNotes: string;
  remedy: string;
  outcome: string;
  closureReason: string;
  state: LifecycleState;
  updatedAt: string;
  version: number;
  history: StatusHistoryEntry[];
  evidence: EvidenceItem[];
  linkedRecords: LinkedRecord[];
  activity: ActivityEntry[];
}

interface FieldErrors {
  [key: string]: string;
}

interface CaseForm {
  complaintTypeCode: string;
  severity: ComplaintCase['severity'];
  receivedChannel: string;
  receivedAt: string;
  complainant: string;
  summary: string;
  investigationNotes: string;
  remedy: string;
  outcome: string;
  closureReason: string;
}

const LIFECYCLE_ORDER: LifecycleState[] = [
  'new',
  'acknowledged',
  'investigating',
  'resolved',
  'closed',
];

const LIFECYCLE_LABEL: Record<LifecycleState, string> = {
  new: 'New',
  acknowledged: 'Acknowledged',
  investigating: 'Investigating',
  resolved: 'Resolved',
  closed: 'Closed',
  reopened: 'Reopened',
};

const CHANNEL_OPTIONS = ['Phone', 'Email', 'Web form', 'Field visit', 'Social media'];

const COMPLAINT_TYPES = [
  { code: 'CMT-DIST-01', label: 'Distribution shortfall' },
  { code: 'CMT-CONDUCT-02', label: 'Staff conduct' },
  { code: 'CMT-QUALITY-03', label: 'Aid item quality' },
  { code: 'CMT-DELAY-04', label: 'Service delay' },
  { code: 'CMT-SAFEGUARD-05', label: 'Safeguarding concern' },
];

function buildCase(overrides: Partial<ComplaintCase> = {}): ComplaintCase {
  const base: ComplaintCase = {
    reference: 'CMP-2026-00142',
    complaintTypeCode: 'CMT-DIST-01',
    complaintTypeLabel: 'Distribution shortfall',
    severity: 'high',
    receivedChannel: 'Phone',
    receivedAt: '29 Jul 2026, 10:20 am',
    complainant: 'Ravi Kumar (on behalf of household #4021)',
    summary:
      'Complainant reports that the winter relief kit received on 28 Jul was missing blankets and contained only half the listed food items.',
    owner: 'Sarah Johnson',
    ownerInitials: 'SJ',
    slaDueAt: '02 Aug 2026, 06:00 pm',
    slaBreached: false,
    investigationNotes: '',
    remedy: '',
    outcome: '',
    closureReason: '',
    state: 'acknowledged',
    updatedAt: '31 Jul 2026, 01:54 pm',
    version: 3,
    history: [
      { state: 'new', label: 'Case logged', timestamp: '29 Jul 2026, 10:22 am', actor: 'Intake bot' },
      { state: 'acknowledged', label: 'Acknowledged', timestamp: '29 Jul 2026, 03:10 pm', actor: 'Sarah Johnson' },
    ],
    evidence: [
      { label: 'Delivery manifest #DM-58821', type: 'Document', addedBy: 'Sarah Johnson', timestamp: '29 Jul 2026' },
      { label: 'Photo — kit contents', type: 'Image', addedBy: 'Ravi Kumar', timestamp: '29 Jul 2026' },
    ],
    linkedRecords: [
      { label: 'Winter Relief Appeal', reference: 'CAM-2026-0101', type: 'Campaign' },
      { label: 'Household #4021', reference: 'HH-4021', type: 'Beneficiary record' },
    ],
    activity: [
      { label: 'Case created from web form', timestamp: '29 Jul 2026, 10:22 am', actor: 'System' },
      { label: 'Assigned to Sarah Johnson', timestamp: '29 Jul 2026, 11:05 am', actor: 'Priya Menon' },
      { label: 'Acknowledged by owner', timestamp: '29 Jul 2026, 03:10 pm', actor: 'Sarah Johnson' },
    ],
  };
  return { ...base, ...overrides };
}

/* ------------------------------------------------------------------ */
/*  Component                                                          */
/* ------------------------------------------------------------------ */

@Component({
  selector: 'app-complaint-case',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './complaint-case.html',
  styleUrl: './complaint-case.css',
})
export class ComplaintCaseComponent {
  /* ---------------- Preview / demo controls ---------------- */

  readonly previewOpen = signal(false);
  readonly scenario = signal<PreviewScenario>('ready');
  readonly role = signal<EffectiveRole>('approver');
  readonly restrictConfidential = signal(false);

  readonly scenarioOptions: { value: PreviewScenario; label: string }[] = [
    { value: 'ready', label: 'Ready — investigating case' },
    { value: 'loading', label: 'Loading' },
    { value: 'empty', label: 'No case found in scope' },
    { value: 'validation', label: 'Validation error' },
    { value: 'duplicate', label: 'Possible duplicate' },
    { value: 'no-access', label: 'No access' },
    { value: 'conflict', label: 'Record changed (conflict)' },
    { value: 'dependency-failure', label: 'Dependency failure' },
    { value: 'success', label: 'Persistent success' },
  ];

  readonly roleOptions: { value: EffectiveRole; label: string }[] = [
    { value: 'tenant-admin', label: 'Tenant admin' },
    { value: 'initiator', label: 'Initiator' },
    { value: 'approver', label: 'Approver' },
    { value: 'unauthorised', label: 'Unauthorised viewer' },
  ];

  togglePreview() {
    this.previewOpen.update((v) => !v);
  }

  setScenario(value: PreviewScenario) {
    this.scenario.set(value);
    this.outcomePanel.set(null);
    this.showDuplicateBanner.set(value === 'duplicate');
    this.showConflictBanner.set(value === 'conflict');
    this.fieldErrors.set({});
    this.errorSummaryFields.set([]);
    if (value === 'success') {
      this.outcomePanel.set({
        action: 'Acknowledge',
        reference: this.caseData().reference,
        state: 'Acknowledged',
        effectiveTime: '31 Jul 2026, 02:10 pm',
        downstream: 'Notification to complainant: queued',
        nextAction: 'Investigate',
      });
    }
  }

  setRole(value: EffectiveRole) {
    this.role.set(value);
  }

  /* ---------------- Case data ---------------- */

  readonly caseData = signal<ComplaintCase>(buildCase());

  readonly stateLabel = computed(() => LIFECYCLE_LABEL[this.caseData().state]);

  readonly lifecycleSteps = computed(() => {
    const current = this.caseData().state;
    const reopened = current === 'reopened';
    const activeIndex = reopened
      ? LIFECYCLE_ORDER.indexOf('investigating')
      : LIFECYCLE_ORDER.indexOf(current);
    return LIFECYCLE_ORDER.map((step, i) => ({
      key: step,
      label: LIFECYCLE_LABEL[step],
      done: i < activeIndex || (reopened && step === 'investigating'),
      active: i === activeIndex && !reopened ? true : reopened && step === 'investigating',
      upcoming: i > activeIndex,
    }));
  });

  /* ---------------- Permissions ---------------- */

  readonly hasViewPermission = computed(
    () => this.scenario() !== 'no-access' && this.role() !== 'unauthorised'
  );

  readonly can = computed(() => {
    const role = this.role();
    const state = this.caseData().state;
    const eligibleRole = role === 'tenant-admin' || role === 'initiator' || role === 'approver';
    return {
      acknowledge: eligibleRole && (state === 'new' || state === 'reopened'),
      assign: eligibleRole && (state === 'new' || state === 'acknowledged'),
      investigate: eligibleRole && state === 'acknowledged',
      resolve: eligibleRole && state === 'investigating',
      reopen: eligibleRole && (state === 'resolved' || state === 'closed'),
      delete: eligibleRole && state === 'new' && this.caseData().history.length <= 1,
    };
  });

  readonly primaryAction = computed<{ key: ActionKey; label: string } | null>(() => {
    const c = this.can();
    if (c.acknowledge) return { key: 'acknowledge', label: 'Acknowledge' };
    if (c.investigate) return { key: 'investigate', label: 'Start investigation' };
    if (c.resolve) return { key: 'resolve', label: 'Resolve case' };
    if (c.reopen) return { key: 'reopen', label: 'Reopen case' };
    return null;
  });

  readonly secondaryActions = computed<{ key: ActionKey; label: string }[]>(() => {
    const c = this.can();
    const list: { key: ActionKey; label: string }[] = [];
    if (c.assign) list.push({ key: 'assign', label: 'Assign owner' });
    if (c.acknowledge === false && c.reopen) list.push({ key: 'reopen', label: 'Reopen case' });
    return list.filter((a) => a.key !== this.primaryAction()?.key);
  });

  /* Field-level visibility helpers */
  isConfidentialVisible(): boolean {
    return !this.restrictConfidential() && this.role() !== 'unauthorised';
  }

  showInvestigationNotes = computed(() =>
    ['investigating', 'resolved', 'closed', 'reopened'].includes(this.caseData().state)
  );
  showRemedyOutcome = computed(() => ['resolved', 'closed'].includes(this.caseData().state));
  showClosureReason = computed(() => ['closed'].includes(this.caseData().state));

  /* ---------------- Form state (editable classification fields) ---------------- */

  readonly form = signal<CaseForm>({
    complaintTypeCode: this.caseData().complaintTypeCode,
    severity: this.caseData().severity,
    receivedChannel: this.caseData().receivedChannel,
    receivedAt: this.caseData().receivedAt,
    complainant: this.caseData().complainant,
    summary: this.caseData().summary,
    investigationNotes: this.caseData().investigationNotes,
    remedy: this.caseData().remedy,
    outcome: this.caseData().outcome,
    closureReason: this.caseData().closureReason,
  });

  readonly complaintTypes = COMPLAINT_TYPES;
  readonly channels = CHANNEL_OPTIONS;
  readonly severities: { value: ComplaintCase['severity']; label: string }[] = [
    { value: 'critical', label: 'Critical' },
    { value: 'high', label: 'High' },
    { value: 'medium', label: 'Medium' },
    { value: 'low', label: 'Low' },
  ];

  typeSearch = signal('');
  readonly filteredTypes = computed(() => {
    const q = this.typeSearch().trim().toLowerCase();
    if (!q) return this.complaintTypes;
    return this.complaintTypes.filter(
      (t) => t.label.toLowerCase().includes(q) || t.code.toLowerCase().includes(q)
    );
  });

  updateForm<K extends keyof CaseForm>(key: K, value: CaseForm[K]) {
    this.form.update((f) => ({ ...f, [key]: value }));
  }

  summaryCount = computed(() => this.form().summary.length);
  notesCount = computed(() => this.form().investigationNotes.length);
  remedyCount = computed(() => this.form().remedy.length);
  closureCount = computed(() => this.form().closureReason.length);

  /* ---------------- Validation ---------------- */

  readonly fieldErrors = signal<FieldErrors>({});
  readonly errorSummaryFields = signal<{ id: string; label: string }[]>([]);
  readonly saveConfirmed = signal(false);

  private validateClassification(): FieldErrors {
    const f = this.form();
    const errors: FieldErrors = {};
    if (!f.complaintTypeCode) errors['complaintType'] = 'Enter Complaint type.';
    if (!f.severity) errors['severity'] = 'Enter Severity.';
    if (!f.receivedChannel) errors['receivedChannel'] = 'Enter Received channel.';
    if (!f.complainant.trim()) errors['complainant'] = 'Enter Complainant or party.';
    else if (f.complainant.trim().length > 200)
      errors['complainant'] = 'Review Complainant or party. The value does not meet the stated format or range.';
    const summary = f.summary.trim();
    if (!summary) errors['summary'] = 'Enter Summary.';
    else if (summary.length < 10 || summary.length > 2000)
      errors['summary'] = 'Review Summary. The value does not meet the stated format or range.';
    return errors;
  }

  saveClassification() {
    const errors =
      this.scenario() === 'validation'
        ? { ...this.validateClassification(), summary: 'Review Summary. The value does not meet the stated format or range.' }
        : this.validateClassification();

    if (Object.keys(errors).length > 0) {
      this.fieldErrors.set(errors);
      this.errorSummaryFields.set(
        Object.entries(errors).map(([id, msg]) => ({ id, label: msg }))
      );
      this.saveConfirmed.set(false);
      queueMicrotask(() => this.focusFirstError());
      return;
    }
    this.fieldErrors.set({});
    this.errorSummaryFields.set([]);
    this.saveConfirmed.set(true);
    this.caseData.update((c) => ({
      ...c,
      complaintTypeCode: this.form().complaintTypeCode,
      complaintTypeLabel:
        this.complaintTypes.find((t) => t.code === this.form().complaintTypeCode)?.label ??
        c.complaintTypeLabel,
      severity: this.form().severity,
      receivedChannel: this.form().receivedChannel,
      complainant: this.form().complainant.trim(),
      summary: this.form().summary.trim(),
      updatedAt: 'just now',
      version: c.version + 1,
    }));
    window.setTimeout(() => this.saveConfirmed.set(false), 3000);
  }

  private focusFirstError() {
    const firstKey = Object.keys(this.fieldErrors())[0];
    if (!firstKey) return;
    const el = document.getElementById('field-' + firstKey);
    el?.focus();
  }

  /* ---------------- Action confirmation dialog ---------------- */

  readonly confirmOpen = signal(false);
  readonly confirmAction = signal<ActionKey | null>(null);
  readonly confirmReason = signal('');
  readonly confirmReasonError = signal('');

  readonly actionCopy: Record<ActionKey, { title: string; verb: string; risky: boolean; needsReason: boolean }> = {
    acknowledge: { title: 'Acknowledge complaint', verb: 'Acknowledge', risky: false, needsReason: false },
    assign: { title: 'Assign owner', verb: 'Assign', risky: false, needsReason: false },
    investigate: { title: 'Start investigation', verb: 'Start investigation', risky: false, needsReason: false },
    resolve: { title: 'Resolve case', verb: 'Resolve', risky: true, needsReason: true },
    reopen: { title: 'Reopen case', verb: 'Reopen', risky: true, needsReason: true },
  };

  openConfirm(action: ActionKey) {
    this.confirmAction.set(action);
    this.confirmReason.set('');
    this.confirmReasonError.set('');
    this.confirmOpen.set(true);
  }

  closeConfirm() {
    this.confirmOpen.set(false);
    this.confirmAction.set(null);
  }

  readonly nextStateFor: Record<ActionKey, LifecycleState> = {
    acknowledge: 'acknowledged',
    assign: 'acknowledged',
    investigate: 'investigating',
    resolve: 'resolved',
    reopen: 'reopened',
  };

  readonly pastTenseFor: Record<ActionKey, string> = {
    acknowledge: 'Acknowledged',
    assign: 'Assigned',
    investigate: 'Investigation started',
    resolve: 'Resolved',
    reopen: 'Reopened',
  };

  /* ---------------- Outcome / persistent confirmation ---------------- */

  readonly outcomePanel = signal<{
    action: string;
    reference: string;
    state: string;
    effectiveTime: string;
    downstream: string;
    nextAction: string;
  } | null>(null);

  readonly toast = signal<{ tone: 'success' | 'error'; message: string } | null>(null);
  private toastTimer: any;

  private showToast(tone: 'success' | 'error', message: string) {
    this.toast.set({ tone, message });
    clearTimeout(this.toastTimer);
    this.toastTimer = window.setTimeout(() => this.toast.set(null), 4000);
  }

  confirmSubmit() {
    const action = this.confirmAction();
    if (!action) return;
    const copy = this.actionCopy[action];

    if (copy.needsReason && this.confirmReason().trim().length < 10) {
      this.confirmReasonError.set(
        'Review Reason. The value does not meet the stated format or range.'
      );
      return;
    }

    if (this.scenario() === 'dependency-failure') {
      this.applyStateChange(action);
      this.confirmOpen.set(false);
      this.showToast('error', 'Local change saved. A dependent step could not complete.');
      this.outcomePanel.set(null);
      this.dependencyFailurePanel.set({
        reference: this.caseData().reference,
        correlation: 'COR-88213-4471',
        provider: 'Notification service',
      });
      return;
    }

    this.applyStateChange(action);
    this.confirmOpen.set(false);
    this.showToast('success', `${copy.verb} confirmed.`);
    this.outcomePanel.set({
      action: copy.verb,
      reference: this.caseData().reference,
      state: LIFECYCLE_LABEL[this.nextStateFor[action]],
      effectiveTime: 'Just now, Asia/Kolkata',
      downstream:
        action === 'acknowledge'
          ? 'Acknowledgement notice to complainant: sent'
          : action === 'resolve'
          ? 'Closure summary to complainant: queued'
          : 'No downstream dependency pending',
      nextAction: this.describeNextAction(this.nextStateFor[action]),
    });
    this.dependencyFailurePanel.set(null);
  }

  private describeNextAction(state: LifecycleState): string {
    switch (state) {
      case 'acknowledged':
        return 'Start investigation';
      case 'investigating':
        return 'Resolve case';
      case 'resolved':
        return 'Close case';
      case 'reopened':
        return 'Start investigation';
      default:
        return 'Review case';
    }
  }

  private applyStateChange(action: ActionKey) {
    const nextState = this.nextStateFor[action];
    this.caseData.update((c) => ({
      ...c,
      state: nextState,
      owner: action === 'assign' ? c.owner : c.owner,
      updatedAt: 'just now',
      version: c.version + 1,
      history: [
        ...c.history,
        {
          state: nextState,
          label: this.pastTenseFor[action],
          timestamp: 'Just now',
          actor: this.role() === 'approver' ? 'You (Approver)' : this.role() === 'tenant-admin' ? 'You (Tenant admin)' : 'You (Initiator)',
        },
      ],
      activity: [
        {
          label: `${this.actionCopy[action].title} — ${this.confirmReason() || 'no additional reason supplied'}`,
          timestamp: 'Just now',
          actor: 'You',
        },
        ...c.activity,
      ],
    }));
  }

  readonly dependencyFailurePanel = signal<{ reference: string; correlation: string; provider: string } | null>(
    null
  );

  retryDependency() {
    this.dependencyFailurePanel.set(null);
    this.showToast('success', 'Dependent step retried successfully.');
    this.outcomePanel.set({
      action: 'Retry',
      reference: this.caseData().reference,
      state: this.stateLabel(),
      effectiveTime: 'Just now, Asia/Kolkata',
      downstream: 'Notification service: delivered',
      nextAction: this.describeNextAction(this.caseData().state),
    });
  }

  dismissOutcome() {
    this.outcomePanel.set(null);
  }

  /* ---------------- Duplicate banner ---------------- */

  readonly showDuplicateBanner = signal(false);

  dismissDuplicate(choice: 'link' | 'change' | 'review' | 'cancel') {
    this.showDuplicateBanner.set(false);
    if (choice === 'link') this.showToast('success', 'Linked to the existing complaint record.');
    if (choice === 'review') this.showToast('success', 'Flagged for supervisor review.');
  }

  /* ---------------- Conflict banner ---------------- */

  readonly showConflictBanner = signal(false);

  resolveConflict(choice: 'compare' | 'reapply' | 'cancel') {
    this.showConflictBanner.set(false);
    if (choice === 'reapply') {
      this.caseData.update((c) => ({ ...c, version: c.version + 1 }));
      this.showToast('success', 'Your changes were reapplied to the latest version.');
    } else if (choice === 'cancel') {
      this.form.update((f) => ({ ...f, complainant: this.caseData().complainant, summary: this.caseData().summary }));
    }
  }

  /* ---------------- Related panel tabs ---------------- */

  readonly relatedTab = signal<'linked' | 'documents' | 'activity' | 'integration'>('activity');

  setRelatedTab(tab: 'linked' | 'documents' | 'activity' | 'integration') {
    this.relatedTab.set(tab);
  }

  /* ---------------- Copy helpers ---------------- */

  readonly copiedField = signal<string | null>(null);

  copyValue(field: string, value: string) {
    navigator.clipboard?.writeText(value).catch(() => {});
    this.copiedField.set(field);
    window.setTimeout(() => this.copiedField.set(null), 1500);
  }

  /* ---------------- Search across scope (context bar) ---------------- */

  readonly caseSearch = signal('');

  severityTone(sev: ComplaintCase['severity']): string {
    return sev;
  }
}