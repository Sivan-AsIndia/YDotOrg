import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import {
  FinanceUiState,
  ExceptionType,
  FinanceExceptionPermissions,
  ScopeAwareOption,
  
} from '../../../../Shared/models/finance.model';
import { ToastService } from '../../../../Shared/services/toast.service';
import { FinanceStateService } from '../shared/finance-state.service';
import { FinanceExceptionRecord } from '../shared/finance.model';

/**
 * SCR-FIN-005 — Finance exception case.
 *
 * Faithful implementation of section 4.5 / 7.5 of the YDot FIN Practical UI/UX
 * Generation Specification (Dark Meadow v1.2).
 *
 *  Route            : /money/finance/finance-exception-case
 *  Purpose          : Investigate missing, duplicate, variance, fee and timing exceptions.
 *  Primary users    : Finance roles
 *  View permission  : fin.finance-exception-case.view
 *  Primary action   : Assign
 *  History rule     : Delete is available only for an unused draft with no downstream
 *                     reference; otherwise use the domain lifecycle action.
 *  Theme            : Dark Meadow task surface; warm-paper data rows; calm-blue
 *                     information; antique-gold focus/progress; non-striped tables.
 */

@Component({
  selector: 'app-finance-exception-case',
  imports: [CommonModule, FormsModule],
  templateUrl: './finance-exception-case.html',
  styleUrl: './finance-exception-case.css',
})
export class FinanceExceptionCaseComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly financeState = inject(FinanceStateService);

  // ================= Task header (4.5.1) =================
  protected readonly pageTitle = 'Finance Exception Case';
  protected readonly pageSubtitle =
    'Investigate missing, duplicate, variance, fee and timing exceptions. Every count, search and action is restricted by effective scope.';
  protected readonly lifecycleState = 'Assigned';
  protected readonly owner = 'Divya Krishnan · Finance Checker';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('Today, 04:40 PM · IST');

  /** Effective permissions decided server-side (4.5.3, 4.5.7). */
  protected readonly permissions: FinanceExceptionPermissions = {
    view: true,
    assign: true,
    evidence: true,
    correct: true,
    resolve: true,
  };

  private readonly workflowPermittedStates = ['Assigned', 'Investigation', 'Correction', 'Pending review'];

  // ================= Context and filters (4.5.1) =================
  protected readonly filtersOpen = signal(false);
  protected toggleFilters(): void {
    this.filtersOpen.update((v) => !v);
  }
  /**
   * Pre-filled from an incoming handoff: ?settlementRef= (Settlement batch detail's
   * Match action), ?paymentRef= (Reconciliation workspace's Unmatch action), or
   * ?workRef= (Finance workbench's Escalate action).
   */
  protected readonly searchTerm = signal(
    this.route.snapshot.queryParamMap.get('settlementRef') ??
    this.route.snapshot.queryParamMap.get('paymentRef') ??
    this.route.snapshot.queryParamMap.get('workRef') ??
    '',
  );
  protected readonly exceptionTypeFilter = signal('');
  protected readonly exceptionTypeOptions: readonly ExceptionType[] = [
    'Missing settlement',
    'Duplicate payment',
    'Variance',
    'Fee variance',
    'Timing exception',
    'Bank reference mismatch',
  ];
  protected readonly statusFilter = signal('');
  protected readonly statusOptions = ['All statuses', 'Open', 'Assigned', 'Investigation', 'Correction', 'Resolved'];

  protected readonly scopeOptions = [
    'My active organisation (default)',
    'YDot Foundation · National',
    'Southern Region · Tamil Nadu',
  ];
  protected readonly scopeFilter = signal(this.scopeOptions[0]);

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.exceptionTypeFilter()) chips.push({ key: 'type', label: `Type: ${this.exceptionTypeFilter()}` });
    if (this.statusFilter()) chips.push({ key: 'status', label: `Status: ${this.statusFilter()}` });
    if (this.searchTerm().trim()) chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    if (this.scopeFilter() !== this.scopeOptions[0]) chips.push({ key: 'scope', label: `Scope: ${this.scopeFilter()}` });
    return chips;
  });

  /**
   * Exception records (4.5.2 field contract) — backed by the shared Finance state so
   * Assign/Correct/Resolve here persist across navigation and are reflected on the
   * Finance Workbench's Exception queue.
   */
  protected readonly records = this.financeState.exceptionRecords;

  /** Exception type catalogue for form select (4.5.2). */
  protected readonly formExceptionTypes: readonly ExceptionType[] = [...this.exceptionTypeOptions];

  protected readonly filteredRecords = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const t = this.exceptionTypeFilter();
    const s = this.statusFilter();
    return this.records().filter((r) => {
      if (q && !(r.exceptionReference.toLowerCase().includes(q) || r.affectedPaymentOrBatch.toLowerCase().includes(q))) {
        return false;
      }
      if (t && r.exceptionType !== t) return false;
      if (s && s !== 'All statuses' && r.status !== s) return false;
      return true;
    });
  });

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.exceptionTypeFilter.set('');
    this.statusFilter.set('');
    this.scopeFilter.set(this.scopeOptions[0]);
  }

  protected removeFilterChip(key: string): void {
    if (key === 'type') this.exceptionTypeFilter.set('');
    else if (key === 'status') this.statusFilter.set('');
    else if (key === 'search') this.searchTerm.set('');
    else if (key === 'scope') this.scopeFilter.set(this.scopeOptions[0]);
  }

  // ================= Actions, eligibility and result (4.5.3) =================
  /**
   * Per-record eligibility — previously every row shared one global check
   * (`workflowPermittedStates.includes(this.lifecycleState)` against a hardcoded
   * `lifecycleState = 'Assigned'` constant that never varied per row), so an
   * already-Resolved exception's Assign/Evidence/Correct/Resolve buttons stayed
   * enabled forever — stale data could be re-actioned indefinitely. Resolved is
   * the documented terminal state (4.5 "Open → assigned → investigation →
   * correction → resolved"); nothing is left to do once a case reaches it.
   */
  private static readonly terminalExceptionStatus = 'Resolved';
  protected assignAllowed(record: FinanceExceptionRecord): boolean {
    return this.permissions.assign && record.status !== FinanceExceptionCaseComponent.terminalExceptionStatus;
  }
  protected evidenceAllowed(record: FinanceExceptionRecord): boolean {
    return this.permissions.evidence && record.status !== FinanceExceptionCaseComponent.terminalExceptionStatus;
  }
  protected correctAllowed(record: FinanceExceptionRecord): boolean {
    return this.permissions.correct && record.status !== FinanceExceptionCaseComponent.terminalExceptionStatus;
  }
  protected resolveAllowed(record: FinanceExceptionRecord): boolean {
    return this.permissions.resolve && record.status !== FinanceExceptionCaseComponent.terminalExceptionStatus;
  }

  /** Why a row's Assign/Evidence/Correct/Resolve button is disabled — shown as a toast since a native disabled button never fires click. */
  protected actionBlockedReason(record: FinanceExceptionRecord): string {
    if (record.status === FinanceExceptionCaseComponent.terminalExceptionStatus) {
      return `${record.exceptionReference} is already Resolved — no further action is available on it.`;
    }
    return 'This action is not available with your current permissions.';
  }

  protected readonly uiState = signal<FinanceUiState>('ready');
  protected setUiState(state: FinanceUiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  protected readonly activeException = signal<readonly { exceptionReference: string }[] | null>(null);
  protected readonly assignDialogOpen = signal(false);
  protected readonly assignTarget = signal('');
  protected readonly assignTo = signal('');
  protected readonly assigneeOptions: readonly ScopeAwareOption[] = [
    { reference: 'USR-0244', name: 'Arjun Menon', context: 'Finance Maker · Kerala', initials: 'AM', tone: 'blue' },
    { reference: 'USR-0258', name: 'Divya Krishnan', context: 'Finance Checker · Karnataka', initials: 'DK', tone: 'plum' },
  ];
  protected readonly assignReason = signal('');
  protected readonly assignReasonMin = 10;
  protected readonly assignReasonMax = 2000;
  protected readonly assignReasonValid = computed(() => {
    const len = this.assignReason().trim().length;
    return len >= this.assignReasonMin && len <= this.assignReasonMax;
  });
  protected readonly assignReasonCount = computed(() => this.assignReason().trim().length);

  protected openAssignDialog(reference: string): void {
    const record = this.records().find((r) => r.exceptionReference === reference);
    if (!record) return;
    if (!this.assignAllowed(record)) {
      this.toast.show('Assign unavailable', this.actionBlockedReason(record), 'warning');
      return;
    }
    this.assignTarget.set(reference);
    this.assignTo.set('');
    this.assignReason.set('');
    this.assignDialogOpen.set(true);
  }
  protected cancelAssign(): void {
    this.assignDialogOpen.set(false);
  }
  protected confirmAssign(): void {
    if (!this.assignReasonValid() || !this.assignTo()) return;
    this.assignDialogOpen.set(false);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    const target = this.assignTarget();
    this.financeState.assignException(target, this.assignTo());
    const assignee = this.assigneeOptions.find((a) => a.reference === this.assignTo());
    this.toast.show('Assigned', `${target} assigned to ${assignee?.name ?? this.assignTo()}.`, 'success');
  }

  // ---- Correct dialog: Root cause [TA] + Correction action [TXT] (4.5.2) ----
  protected readonly correctDialogOpen = signal(false);
  protected readonly correctTarget = signal('');
  protected readonly rootCause = signal('');
  protected readonly correctionAction = signal('');
  protected readonly rootCauseMin = 10;
  protected readonly rootCauseMax = 2000;
  protected readonly rootCauseCount = computed(() => this.rootCause().trim().length);
  protected readonly rootCauseValid = computed(() => {
    const len = this.rootCauseCount();
    return len >= this.rootCauseMin && len <= this.rootCauseMax;
  });
  protected readonly correctionActionValid = computed(() => this.correctionAction().trim().length > 0);

  protected openCorrectDialog(reference: string): void {
    const record = this.records().find((r) => r.exceptionReference === reference);
    if (!record) return;
    if (!this.correctAllowed(record)) {
      this.toast.show('Correct unavailable', this.actionBlockedReason(record), 'warning');
      return;
    }
    this.correctTarget.set(reference);
    this.rootCause.set('');
    this.correctionAction.set('');
    this.correctDialogOpen.set(true);
  }
  protected cancelCorrect(): void {
    this.correctDialogOpen.set(false);
  }
  /** Correct — routes to Financial Correction/Reversal when the investigation requires amending a posted record (Master workflow). */
  protected confirmCorrect(): void {
    if (!this.rootCauseValid() || !this.correctionActionValid()) return;
    this.correctDialogOpen.set(false);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    const exceptionRef = this.correctTarget();
    this.financeState.correctException(exceptionRef);
    this.toast.show('Correction routed', `${exceptionRef} — routing to Financial Correction / Reversal.`, 'success');
    // Financial Correction's "Original transaction" field expects the affected payment/batch
    // reference, not this exception's own reference — carry that through the handoff instead.
    const affectedReference = this.records().find((r) => r.exceptionReference === exceptionRef)?.affectedPaymentOrBatch ?? exceptionRef;
    this.router.navigate(['/app/fin/financial-correction-or-reversal'], { queryParams: { exceptionRef: affectedReference } });
  }

  // ---- Resolve dialog: Resolution reason [TA, confidential] (4.5.2) ----
  protected readonly resolveDialogOpen = signal(false);
  protected readonly resolveTarget = signal('');
  protected readonly resolutionReason = signal('');
  protected readonly resolutionReasonMin = 10;
  protected readonly resolutionReasonMax = 2000;
  protected readonly resolutionReasonCount = computed(() => this.resolutionReason().trim().length);
  protected readonly resolutionReasonValid = computed(() => {
    const len = this.resolutionReasonCount();
    return len >= this.resolutionReasonMin && len <= this.resolutionReasonMax;
  });

  protected openResolveDialog(reference: string): void {
    const record = this.records().find((r) => r.exceptionReference === reference);
    if (!record) return;
    if (!this.resolveAllowed(record)) {
      this.toast.show('Resolve unavailable', this.actionBlockedReason(record), 'warning');
      return;
    }
    this.resolveTarget.set(reference);
    this.resolutionReason.set('');
    this.resolveDialogOpen.set(true);
  }
  protected cancelResolve(): void {
    this.resolveDialogOpen.set(false);
  }
  protected confirmResolve(): void {
    if (!this.resolutionReasonValid()) return;
    this.resolveDialogOpen.set(false);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    const target = this.resolveTarget();
    this.financeState.resolveException(target);
    this.toast.show('Resolved', `${target} marked resolved.`, 'success');
  }

  // ---- Evidence dialog: Evidence [FILE, confidential] (4.5.2) ----
  protected readonly evidenceDialogOpen = signal(false);
  protected readonly evidenceTarget = signal('');
  protected readonly evidenceFileName = signal('');
  protected readonly evidenceUploadStatus = signal<'idle' | 'scanning' | 'classified'>('idle');
  protected readonly evidenceValid = computed(() => this.evidenceUploadStatus() === 'classified');
  private static readonly approvedEvidenceTypes = ['.pdf', '.png', '.jpg', '.jpeg'];
  private static readonly maxEvidenceSizeMb = 10;
  protected readonly evidenceError = signal('');

  protected openEvidenceDialog(reference: string): void {
    const record = this.records().find((r) => r.exceptionReference === reference);
    if (!record) return;
    if (!this.evidenceAllowed(record)) {
      this.toast.show('Evidence unavailable', this.actionBlockedReason(record), 'warning');
      return;
    }
    this.evidenceTarget.set(reference);
    // Reopening on a record that already has evidence shows what's actually linked to it,
    // not a blank form — the record remembers its own evidence (4.5.2 Evidence field).
    this.evidenceFileName.set(record.evidenceFileName ?? '');
    this.evidenceUploadStatus.set(record.evidenceFileName ? 'classified' : 'idle');
    this.evidenceError.set('');
    this.evidenceDialogOpen.set(true);
  }
  protected cancelEvidence(): void {
    this.evidenceDialogOpen.set(false);
  }
  protected onEvidenceFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    const ext = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
    if (!FinanceExceptionCaseComponent.approvedEvidenceTypes.includes(ext)) {
      this.evidenceError.set('Unsupported file type. Approved types: PDF, PNG, JPG.');
      this.evidenceUploadStatus.set('idle');
      this.evidenceFileName.set('');
      return;
    }
    if (file.size > FinanceExceptionCaseComponent.maxEvidenceSizeMb * 1024 * 1024) {
      this.evidenceError.set(`File exceeds the ${FinanceExceptionCaseComponent.maxEvidenceSizeMb} MB limit.`);
      this.evidenceUploadStatus.set('idle');
      this.evidenceFileName.set('');
      return;
    }
    this.evidenceError.set('');
    this.evidenceFileName.set(file.name);
    this.evidenceUploadStatus.set('scanning');
    setTimeout(() => this.evidenceUploadStatus.set('classified'), 500);
  }
  protected confirmEvidence(): void {
    if (!this.evidenceValid()) return;
    this.evidenceDialogOpen.set(false);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.financeState.attachExceptionEvidence(this.evidenceTarget(), this.evidenceFileName());
    this.toast.show('Evidence attached', `${this.evidenceFileName()} linked to ${this.evidenceTarget()}.`, 'success');
  }

  /** Badge class for approval requirement tone — matches statusBadgeClass palette. */
  protected approvalBadgeClass(tone: string): string {
    switch (tone) {
      case 'danger': return 'bg-danger bg-opacity-10 text-danger';
      case 'warning': return 'bg-warning bg-opacity-10 text-warning';
      case 'success': return 'bg-success bg-opacity-10 text-success';
      default: return 'bg-secondary bg-opacity-10 text-secondary';
    }
  }

  // ================= Persistent outcome (4.5.1) =================
  protected readonly persistentOutcome = computed(() => ({
    reference: this.assignTarget() || '—',
    state: this.lifecycleState,
    effectiveTime: this.lastRefresh(),
    downstreamStatus: `${this.filteredRecords().length} exceptions in scope`,
    owner: this.owner,
    nextAction: 'Investigate and resolve the assigned exception',
  }));

  // ================= Formatting helpers =================
  protected formatAmount(value: number): string {
    return value.toLocaleString('en-IN');
  }
  protected formatDateTime(iso: string): string {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }) +
      ' · ' + d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
  }
  protected riskToneClass(tone: string): string {
    return `fw-risk-${tone}`;
  }

  /** Badge class for exception type — matches User Directory badge style. */
  protected typeBadgeClass(type: ExceptionType): string {
    switch (type) {
      case 'Missing settlement':
      case 'Duplicate payment':
        return 'bg-danger bg-opacity-10 text-danger';
      case 'Variance':
      case 'Fee variance':
        return 'bg-warning bg-opacity-10 text-warning';
      default:
        return 'bg-info bg-opacity-10 text-info';
    }
  }

  /** Badge class for status — matches User Directory badge style. */
  protected statusBadgeClass(status: string): string {
    switch (status) {
      case 'Resolved':
        return 'bg-success bg-opacity-10 text-success';
      case 'Open':
        return 'bg-danger bg-opacity-10 text-danger';
      case 'Assigned':
      case 'Investigation':
        return 'bg-info bg-opacity-10 text-info';
      case 'Correction':
        return 'bg-warning bg-opacity-10 text-warning';
      default:
        return 'bg-secondary bg-opacity-10 text-secondary';
    }
  }
}
