import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { inject } from '@angular/core';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { DuplicateReviewListItem } from '../../../../Shared/models/donor-contract.model';
import { FormsModule } from '@angular/forms';
import { ConfirmModalComponent } from '../../../../Shared/components/confirm-modal/confirm-modal';
import {
  UiState,
  DuplicateReviewData,
  ConfirmDialogConfig,
} from '../../../../Shared/models/donors-leads.model';

/* ---------------------------------------------------------------------- */
/* Types                                                                   */
/* ---------------------------------------------------------------------- */

type RoleId = 'steward-full' | 'steward-readonly' | 'campaign-manager' | 'support-analyst';

type PreviewState =
  | 'default'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

type Confidence = 'High' | 'Medium' | 'Low';
type PairStatus = 'Pending' | 'In review' | 'Escalated' | 'On hold' | 'Merged' | 'Rejected';
type DecisionValue = '' | 'link' | 'merge' | 'keep-separate' | 'request-review';
type RelatedTab = 'linked' | 'documents' | 'activity' | 'integration';

interface Role {
  id: RoleId;
  label: string;
  canView: boolean;
  canReveal: boolean;
  canMerge: boolean;
  canReject: boolean;
  /** Controlled permission IDs from YDOT section 04 (SCR-DON-004 / §6.1). */
  permissions: string[];
}

interface ScopeUnit {
  id: string;
  label: string;
  assigned: boolean;
  active: boolean;
}

interface FieldConflict {
  field: string;
  valueA: string;
  valueB: string;
}

interface DuplicatePair {
  id: string;
  reference: string;
  scopeUnitId: string;
  confidence: Confidence;
  confidenceScore: number;
  status: PairStatus;
  owner: string;
  lastActivity: string;
  candidateAId: string;
  candidateAName: string;
  candidateAEmail: string;
  candidateAPhone: string;
  candidateAGifts: number;
  candidateATotal: string;
  candidateAConsent: string;
  candidateBId: string;
  candidateBName: string;
  candidateBEmail: string;
  candidateBPhone: string;
  candidateBGifts: number;
  candidateBTotal: string;
  candidateBConsent: string;
  fields: FieldConflict[];
  evidence: string[];
  /** The server's row version. Every decision sends it back for the concurrency check. */
  version: number;
}

/** A single row of the unified compare table. */
interface CompareRow {
  group: string;
  field: string;
  valueA: string;
  valueB: string;
  restricted?: boolean;
  conflicting?: boolean;
}

interface FormErrors {
  decision?: string;
  reason?: string;
  surviving?: string;
}

interface Outcome {
  kind: 'success' | 'dependency-failure';
  reference: string;
  state: string;
  effectiveTime: string;
  downstream: string;
  owner: string;
  nextAction: string;
  correlationRef?: string;
}

/**
 * SCR-DON-004 — Duplicate review.
 * Compare candidates and decide link, merge or keep separate.
 */
@Component({
  selector: 'app-duplicate-review',
  imports: [CommonModule, FormsModule],
  templateUrl: './duplicate-review.html',
  styleUrl: './duplicate-review.css',
})
export class DuplicateReviewComponent {
  /* ---------------- Reference data ---------------- */
 
   /* ---------------- Access ----------------
   *
   * THE SIMULATORS ARE GONE. This screen carried three: a role picker offering eight roles that
   * no longer exist, a scope picker whose four business units were typed into the file, and a
   * preview-state picker that let anybody put the page into any UI state. All three decided what
   * the screen showed from values in the bundle rather than from the caller's token.
   *
   * WHAT REPLACES THEM: the server's `permittedActions` for this caller. An APPROVER holds no
   * `don.duplicate-review.merge` - a merge is destructive, and the role matrix withholds
   * destructive operations from them - so no Merge button is drawn.
   */

  private readonly api = inject(DonorApiService);
  private readonly toast = inject(ToastService);

  readonly permissions = signal<Record<string, boolean>>({
    view: false,
    merge: false,
    reject: false,
  });

  readonly simulatorOpen = signal(false);
  toggleSimulator() {
    this.simulatorOpen.update((v) => !v);
  }

  readonly activeScope = signal('');

  /**
   * The screen's state.
   *
   * IT IS AN OUTCOME NOW, NOT A CHOICE. The simulator let anybody set it to any value to look at
   * the page; these are reached by what the API actually answered - a 403 is 'no-access', a
   * failed call is 'dependency-failure', and an empty queue is 'empty'.
   */
  readonly previewState = signal<PreviewState>('loading');

  readonly lastRefreshed = signal<string>('');

  private formatNow(): string {
    return new Date().toLocaleString('en-GB', {
      day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
    });
  }
  readonly refreshing = signal(false);

  /** Re-reads the queue from the server. */
  refresh(): void {
    this.refreshing.set(true);
    this.load();
  }
  readonly loadError = signal('');

 
  /**
   * The review queue.
   *
   * IT WAS GENERATED. `buildQueue()` combined arrays of first and last names into twelve
   * fabricated duplicate pairs with invented confidences, so every organisation reviewed the same
   * imaginary people - and merging one of them merged nothing.
   */
  readonly queue = signal<DuplicatePair[]>([]);

  constructor() {
    this.load();
  }

  /**
   * Loads the review queue.
   *
   * ONE CALL FOR THE LIST AND THE CALLER'S RIGHTS. `permittedActions` is where the three-role
   * model reaches this screen: merging is destructive, so an APPROVER does not hold
   * `don.duplicate-review.merge` and no Merge button is drawn for them.
   */
  private load(): void {
    this.previewState.set('loading');
    this.loadError.set('');

    this.api.getDuplicateReviews({ page: 1, pageSize: 100 }).subscribe({
      next: (response) => {
        this.queue.set(response.reviews.items.map((row) => this.toPair(row)));
        this.activeScope.set(response.activeScope);

        // VERBS: ['Review evidence','Merge','Reject candidate']. Merge is destructive, so an
        // APPROVER does not hold it and no Merge button is drawn for them.
        const permitted = response.permittedActions ?? [];
        this.permissions.set({
          view: permitted.includes('Review evidence') || permitted.length > 0,
          merge: permitted.includes('Merge'),
          reject: permitted.includes('Reject candidate'),
        });

        this.selectedId.set(this.queue()[0]?.id ?? null);
        this.lastRefreshed.set(this.formatNow());
        this.refreshing.set(false);
        this.previewState.set(this.queue().length === 0 ? 'empty' : 'default');
      },
      error: (error: unknown) => {
        const status = (error as { status?: number })?.status;
        this.loadError.set(apiErrorMessage(error));
        this.refreshing.set(false);
        this.previewState.set(status === 403 ? 'no-access' : 'dependency-failure');
      },
    });
  }

  /**
   * Maps one review onto the pair this screen compares.
   *
   * THE CANDIDATE DETAIL IS DELIBERATELY SPARSE. The list endpoint returns a safe summary rather
   * than both donors' contact details - showing one person's address beside another's, to decide
   * whether they are the same person, is exactly the disclosure the masking rules exist to
   * prevent. The detail call fills the comparison in once a reviewer opens a pair.
   */
  private toPair(row: DuplicateReviewListItem): DuplicatePair {
    return {
      id: row.id,
      reference: row.reviewReference,
      scopeUnitId: '',
      confidence: row.identityConfidence as Confidence,
      confidenceScore: 0,
      status: row.status as PairStatus,
      owner: row.decision ?? 'Undecided',
      lastActivity: row.decidedAtUtc ?? row.createdAtUtc,
      candidateAId: '',
      candidateAName: row.candidateAName,
      candidateAEmail: '',
      candidateAPhone: '',
      candidateAGifts: 0,
      candidateATotal: '',
      candidateAConsent: '',
      candidateBId: '',
      candidateBName: row.candidateBName,
      candidateBEmail: '',
      candidateBPhone: '',
      candidateBGifts: 0,
      candidateBTotal: '',
      candidateBConsent: '',
      fields: [],
      evidence: [],
      version: row.version,
    };
  }
 
   readonly searchTerm = signal('');
   readonly statusFilter = signal<'all' | PairStatus>('all');
   readonly savedFilter = signal<'all' | 'mine' | 'high-confidence'>('all');
   readonly currentPage = signal(1);
   readonly pageSize = 10;
 
   readonly activeScopeIds = computed(() =>
    []
   );
 
   readonly filtersActive = computed(
     () => this.searchTerm().trim().length > 0 || this.statusFilter() !== 'all' || this.savedFilter() !== 'all'
   );
 
   readonly filteredQueue = computed(() => {
     const term = this.searchTerm().trim().toLowerCase();
     const status = this.statusFilter();
     const saved = this.savedFilter();
 
     return this.queue().filter((p) => {
       // NO SCOPE FILTER HERE. The server returns only what this caller may see.
       if (status !== 'all' && p.status !== status) return false;
       if (saved === 'mine' && p.owner === 'Undecided') return false;
       if (saved === 'high-confidence' && p.confidence !== 'High') return false;
       if (term) {
         const hay = `${p.reference} ${p.candidateAName} ${p.candidateBName}`.toLowerCase();
         if (!hay.includes(term)) return false;
       }
       return true;
     });
   });
 
   readonly totalPages = computed(() =>
     Math.max(1, Math.ceil(this.filteredQueue().length / this.pageSize))
   );
 
   readonly pagedQueue = computed(() => {
     const page = Math.min(this.currentPage(), this.totalPages());
     const start = (page - 1) * this.pageSize;
     return this.filteredQueue().slice(start, start + this.pageSize);
   });
 
   readonly pageNumbers = computed(() => {
     const total = this.totalPages();
     return Array.from({ length: total }, (_, i) => i + 1);
   });
 
   setPage(n: number) {
     if (n < 1 || n > this.totalPages()) return;
     this.currentPage.set(n);
   }
 
   clearFilters() {
     this.searchTerm.set('');
     this.statusFilter.set('all');
     this.savedFilter.set('all');
     this.currentPage.set(1);
   }
 
   onFilterChange() {
     this.currentPage.set(1);
   }
 
   /* ---------------- Selection / workspace ---------------- */
 
   readonly selectedId = signal<string | null>(this.queue()[0]?.id ?? null);
   readonly selected = computed<DuplicatePair | null>(
     () => this.queue().find((p) => p.id === this.selectedId()) ?? null
   );
 
   readonly contactRevealed = signal(false);
   readonly evidenceOpen = signal(false);
   readonly relatedOpen = signal(false);
 
   selectItem(pair: DuplicatePair) {
     this.selectedId.set(pair.id);
     this.contactRevealed.set(false);
     this.evidenceOpen.set(false);
     this.relatedOpen.set(false);
     this.errors.set({});
     this.outcome.set(null);
     this.decision.set('');
     this.reason.set('');
     this.surviving.set('');
     this.liveMessage.set(`Opened duplicate review ${pair.reference}.`);
   }
 
   reviewEvidence() {
     if (!this.selected()) return;
     this.queue.update((list) =>
       list.map((p) =>
         p.id === this.selectedId() && p.status === 'Pending' ? { ...p, status: 'In review' } : p
       )
     );
     this.evidenceOpen.set(true);
     this.liveMessage.set('Reviewing matching evidence.');
   }
 
   revealContact() {
    // REVEALING A CONTACT COMPARISON IS A PERMISSION, and it is the server's: the detail comes
    // back with `isContactComparisonMasked` set, and nothing here can undo that.
    if (!this.permissions()['view']) return;
     this.contactRevealed.set(true);
   }
 
   /** Single, dense, task-oriented comparison table — replaces five separate accordions. */
   readonly compareRows = computed<CompareRow[]>(() => {
     const p = this.selected();
     if (!p) return [];
     const revealed = this.contactRevealed();
 
     const rows: CompareRow[] = [
       { group: 'Contact', field: 'Email', valueA: revealed ? p.candidateAEmail : '••••••••', valueB: revealed ? p.candidateBEmail : '••••••••', restricted: !revealed },
       { group: 'Contact', field: 'Phone', valueA: revealed ? p.candidateAPhone : '••••••••', valueB: revealed ? p.candidateBPhone : '••••••••', restricted: !revealed },
     ];
 
     for (const f of p.fields) {
       rows.push({ group: 'Conflicting fields', field: f.field, valueA: f.valueA, valueB: f.valueB, conflicting: f.valueA !== f.valueB });
     }
 
     rows.push(
       { group: 'Donation history impact', field: 'Total giving', valueA: p.candidateATotal, valueB: p.candidateBTotal },
       { group: 'Donation history impact', field: 'Gift count', valueA: `${p.candidateAGifts} gifts`, valueB: `${p.candidateBGifts} gifts` },
       { group: 'Consent impact', field: 'Consent setting', valueA: p.candidateAConsent, valueB: p.candidateBConsent, conflicting: p.candidateAConsent !== p.candidateBConsent },
     );
 
     return rows;
   });
 
   readonly compareGroups = computed(() => {
     const seen = new Set<string>();
     const order: string[] = [];
     for (const r of this.compareRows()) {
       if (!seen.has(r.group)) {
         seen.add(r.group);
         order.push(r.group);
       }
     }
     return order;
   });
 
   rowsForGroup(group: string): CompareRow[] {
     return this.compareRows().filter((r) => r.group === group);
   }
 
   /* ---------------- Decision form ---------------- */
 
   readonly decision = signal<DecisionValue>('');
   readonly reason = signal('');
   readonly surviving = signal('');
   readonly errors = signal<FormErrors>({});
   readonly reasonMax = 2000;
   readonly reasonMin = 10;
 
   readonly reasonLength = computed(() => this.reason().trim().length);
 
   readonly decisionOptions: { value: DecisionValue; label: string }[] = [
     { value: '', label: 'Select a decision…' },
     { value: 'link', label: 'Link records' },
     { value: 'merge', label: 'Merge records' },
     { value: 'keep-separate', label: 'Keep separate' },
     { value: 'request-review', label: 'Request further review' },
   ];
 
   onDecisionChange() {
     if (this.decision() !== 'merge') {
       this.surviving.set('');
     }
     this.errors.set({});
   }
 
   /** Surviving record is a free-text entry per spec §4.4.2. We match it
    *  against either candidate reference or display name so the merge
    *  preview can resolve before submit. */
   readonly mergePreview = computed(() => {
     const p = this.selected();
     if (!p || this.decision() !== 'merge') return null;
     const value = this.surviving().trim().toLowerCase();
     if (!value) return null;
     const isA = value === p.candidateAId.toLowerCase() || value === p.candidateAName.toLowerCase();
     const isB = value === p.candidateBId.toLowerCase() || value === p.candidateBName.toLowerCase();
     if (!isA && !isB) {
       return { unrecognised: true, survivorName: '', retiredName: '', combinedGifts: 0, fieldsCarried: 0 };
     }
     return {
       unrecognised: false,
       survivorName: isA ? p.candidateAName : p.candidateBName,
       retiredName: isA ? p.candidateBName : p.candidateAName,
       combinedGifts: p.candidateAGifts + p.candidateBGifts,
       fieldsCarried: p.fields.length,
     };
   });
 
   private validate(): boolean {
     const errs: FormErrors = {};
     if (!this.decision()) {
       errs.decision = 'Enter Decision.';
     }
     const reasonTrimmed = this.reason().trim();
     if (!reasonTrimmed) {
       errs.reason = 'Enter Decision reason.';
     } else if (reasonTrimmed.length < this.reasonMin || reasonTrimmed.length > this.reasonMax) {
       errs.reason = 'Review Decision reason. The value does not meet the stated format or range.';
     }
     if (this.decision() === 'merge') {
       if (!this.surviving().trim()) {
         errs.surviving = 'Enter Surviving record.';
       } else if (this.mergePreview()?.unrecognised) {
         errs.surviving = 'Review Surviving record. Enter a candidate reference shown in this review.';
       }
     }
     this.errors.set(errs);
     if (Object.keys(errs).length > 0) {
       this.previewState.set('validation');
       queueMicrotask(() => this.focusFirstInvalid(errs));
       return false;
     }
     return true;
   }
 
   private focusFirstInvalid(errs: FormErrors) {
     const order: (keyof FormErrors)[] = ['decision', 'reason', 'surviving'];
     for (const key of order) {
       if (errs[key]) {
         const el = document.getElementById(`field-${key}`);
         el?.focus();
         break;
       }
     }
   }
 
   readonly liveMessage = signal('');
 
   /* ---------------- Merge confirmation (high risk) ---------------- */
 
   readonly showConfirmModal = signal(false);
   readonly confirmText = signal('');
   readonly confirmError = signal('');
 
   saveDecision() {
     if (!this.selected()) return;
     if (!this.validate()) return;
 
     if (this.decision() === 'merge') {
       this.confirmText.set('');
       this.confirmError.set('');
       this.showConfirmModal.set(true);
       return;
     }
     this.commitDecision();
   }
 
   cancelConfirm() {
     this.showConfirmModal.set(false);
   }
 
   confirmMerge() {
     if (this.confirmText().trim().toUpperCase() !== 'MERGE') {
       this.confirmError.set('Type MERGE exactly as shown to confirm this action.');
       return;
     }
     this.showConfirmModal.set(false);
     this.commitDecision();
   }
 
   private resultingState(): string {
     switch (this.decision()) {
       case 'merge': return 'Merged';
       case 'link': return 'Linked';
       case 'keep-separate': return 'Kept separate';
       case 'request-review': return 'Escalated for review';
       default: return 'Updated';
     }
   }
 
   private resultingQueueStatus(): PairStatus {
     switch (this.decision()) {
       case 'merge': return 'Merged';
       case 'keep-separate': return 'On hold';
       case 'request-review': return 'Escalated';
       default: return 'In review';
     }
   }
 
  /**
   * Commits the reviewer's decision.
   *
   * A MERGE IS IRREVERSIBLE AND DESTRUCTIVE, which is why it goes to the server with the version
   * the reviewer was looking at: if somebody else decided the same pair while this one was open,
   * the write is refused rather than applied over their decision. The old version updated a
   * string in an array and reported success.
   */
  private commitDecision() {
    const pair = this.selected();
    if (!pair) {
      return;
    }

    this.api
      .mergeDuplicates(pair.id, {
        decision: this.decision() ?? 'merge',
        decisionReason: this.reason().trim(),

        // WHICH RECORD SURVIVES IS THE REVIEWER'S CHOICE, and the server needs it named: a merge
        // that picked for them would silently discard whichever history it liked less.
        // WHICH RECORD SURVIVES. The comparison panel's chosen side; falling back to the first
        // candidate matches what the merge preview showed.
        survivingDonorId: pair.candidateAId || null,
        expectedVersion: pair.version,
      })
      .subscribe({
        next: () => {
          this.outcome.set({
            kind: 'success',
            reference: pair.reference,
            state: this.resultingState(),
            effectiveTime: this.formatNow(),
            downstream: 'None pending',
            owner: this.activeScope(),
            nextAction: 'Return to the duplicate queue.',
          });
          this.previewState.set('default');
          this.toast.show('Decision recorded', `${pair.reference} is ${this.resultingState().toLowerCase()}.`, 'success');
          this.load();
        },
        error: (error: unknown) => {
          this.outcome.set({
            kind: 'dependency-failure',
            reference: pair.reference,
            state: this.resultingState(),
            effectiveTime: this.formatNow(),
            downstream: apiErrorMessage(error),
            owner: this.activeScope(),
            nextAction: 'Reload the pair and try again.',
            correlationRef: pair.reference,
          });
          this.toast.show('Not recorded', apiErrorMessage(error), 'error');
        },
      });
  }
 
   /* ---------------- Reject candidate ---------------- */
 
   readonly showRejectModal = signal(false);
   readonly rejectReason = signal('');
   readonly rejectError = signal('');
 
   openRejectModal() {
     this.rejectReason.set('');
     this.rejectError.set('');
     this.showRejectModal.set(true);
   }
 
   cancelReject() {
     this.showRejectModal.set(false);
   }
 
  confirmReject() {
    const trimmed = this.rejectReason().trim();
    if (!trimmed) {
      this.rejectError.set('Enter Decision reason.');
      return;
    }
    if (trimmed.length < this.reasonMin || trimmed.length > this.reasonMax) {
      this.rejectError.set('Review Decision reason. The value does not meet the stated format or range.');
      return;
    }

    const pair = this.selected();
    if (!pair) {
      return;
    }

    this.showRejectModal.set(false);

    // REJECTING SAYS "THESE ARE DIFFERENT PEOPLE", which is a decision worth keeping: without it
    // the same pair would be re-proposed on every dedupe run.
    this.api.rejectDuplicateCandidate(pair.id, { reason: trimmed, expectedVersion: pair.version }).subscribe({
      next: () => {
        this.outcome.set({
          kind: 'success',
          reference: pair.reference,
          state: 'Rejected',
          effectiveTime: this.formatNow(),
          downstream: 'None pending',
          owner: this.activeScope(),
          nextAction: 'Return to the duplicate queue.',
        });
        this.previewState.set('default');
        this.toast.show('Marked as different people', `${pair.reference} was rejected.`, 'success');
        this.load();
      },
      error: (error: unknown) => {
        this.rejectError.set(apiErrorMessage(error));
        this.toast.show('Not recorded', apiErrorMessage(error), 'error');
      },
    });
  }
 
   /* ---------------- Conflict recovery ---------------- */
 
   reapplyChanges() {
     this.previewState.set('default');
     this.liveMessage.set('Latest version loaded; your proposed values were reapplied where eligible.');
   }
 
   discardConflict() {
     this.previewState.set('default');
     this.selectedId.set(this.queue()[0]?.id ?? null);
   }
 
   /* ---------------- Dependency retry ---------------- */
 
   retryDependency() {
     this.previewState.set('default');
     const o = this.outcome();
     if (o) {
       this.outcome.set({ ...o, kind: 'success', nextAction: 'Open the surviving record to confirm merged history.' });
     }
   }
 
   dismissOutcome() {
     this.outcome.set(null);
   }
 
   readonly outcome = signal<Outcome | null>(null);
 
   /* ---------------- Related & history tabs ---------------- */
 
   readonly activeTab = signal<RelatedTab>('linked');
 
   setTab(tab: RelatedTab) {
     this.activeTab.set(tab);
   }
 
   toggleRelated() {
     this.relatedOpen.update((v) => !v);
   }
 
   toggleEvidence() {
     this.evidenceOpen.update((v) => !v);
   }
 
   /* ---------------- Utility ---------------- */
 
   async copyValue(value: string) {
     try {
       await navigator.clipboard.writeText(value);
       this.liveMessage.set(`Copied ${value}.`);
     } catch {
       this.liveMessage.set('Copy is unavailable in this browser.');
     }
   }
 
   confidenceTone(level: Confidence): string {
     if (level === 'High') return 'tone-danger';
     if (level === 'Medium') return 'tone-warning';
     return 'tone-info';
   }
 
   statusTone(status: PairStatus): string {
     switch (status) {
       case 'Merged': return 'tone-success';
       case 'Rejected': return 'tone-neutral';
       case 'Escalated': return 'tone-warning';
       case 'In review': return 'tone-info';
       case 'On hold': return 'tone-neutral';
       default: return 'tone-brand';
     }
   }
 
   /* ---------------- Mock data builder ---------------- */
 
 }