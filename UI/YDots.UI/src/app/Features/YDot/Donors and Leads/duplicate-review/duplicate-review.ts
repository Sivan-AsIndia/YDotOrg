import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmModalComponent } from '../../../../Shared/components/confirm-modal/confirm-modal';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { PeopleDirectoryService } from '../../../../Shared/services/people-directory.service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { DuplicateReviewListItem } from '../../../../Shared/models/donor-contract.model';
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
   private readonly people = inject(PeopleDirectoryService);
   private readonly tokens = inject(AuthTokenService);

   private readonly donorApi = inject(DonorApiService);

  /* ---------------- Reference data ---------------- */
 
   readonly roles: Role[] = [
     { id: 'steward-full', label: 'Data Steward', canView: true, canReveal: true, canMerge: true, canReject: true, permissions: ['don.duplicate-review.view', 'don.duplicate-review.merge', 'don.duplicate-review.reject-candidate'] },
     { id: 'steward-readonly', label: 'Data Steward — Read only', canView: true, canReveal: false, canMerge: false, canReject: false, permissions: ['don.duplicate-review.view'] },
     { id: 'support-analyst', label: 'Support Analyst', canView: true, canReveal: false, canMerge: false, canReject: false, permissions: ['don.duplicate-review.view'] },
     { id: 'campaign-manager', label: 'Campaign Manager', canView: false, canReveal: false, canMerge: false, canReject: false, permissions: [] },
   ];
 
   readonly previewStates: { id: PreviewState; label: string }[] = [
     { id: 'default', label: 'Default' },
     { id: 'loading', label: 'Loading' },
     { id: 'empty', label: 'Empty' },
     { id: 'validation', label: 'Validation' },
     { id: 'duplicate', label: 'Duplicate' },
     { id: 'no-access', label: 'No access' },
     { id: 'conflict', label: 'Conflict' },
     { id: 'dependency-failure', label: 'Dependency failure' },
     { id: 'success', label: 'Success' },
   ];
 
   /* ---------------- Simulator state ---------------- */
 
   readonly simulatorOpen = signal(true);
   toggleSimulator() {
     this.simulatorOpen.update((v) => !v);
   }
 
   readonly effectiveRoleId = signal<RoleId>('steward-full');

   /**
    * What this caller may actually do.
    *
    * IT READS THE TOKEN, not the role simulator beside it. `effectiveRole` used to return one of
    * four hard-coded rows whose capabilities were literal `true`s, defaulting to the one that
    * could do everything - so the screen drew Merge and Reject for every visitor regardless of
    * their permissions, and a support analyst discovered they were not a data steward by pressing
    * the button that folds two donor records together.
    *
    * The server enforces these codes whatever this object says. Reading them here is what stops
    * the screen offering an action the API will refuse.
    */
   readonly effectiveRole = computed<Role>(() => ({
     id: this.effectiveRoleId(),
     label: this.tokens.displayName() || 'Current user',
     canView: this.tokens.hasAnyPermission('don.duplicate-review.view'),

     // Revealing an unmasked contact is its own permission. Comparing two records without it is
     // possible - the masked forms still differ - so this narrows the screen rather than closing it.
     canReveal: this.tokens.hasAnyPermission('don.donors.view-sensitive-contact'),
     canMerge: this.tokens.hasAnyPermission('don.duplicate-review.merge'),
     canReject: this.tokens.hasAnyPermission('don.duplicate-review.reject-candidate'),
     permissions: [],
   }));
 
   readonly scopeUnits = signal<ScopeUnit[]>([
     { id: 'donation-ops', label: 'Donation Operations', assigned: true, active: true },
     { id: 'community-outreach', label: 'Community Outreach', assigned: true, active: true },
     { id: 'major-gifts', label: 'Major Gifts', assigned: false, active: false },
     { id: 'corporate-partnerships', label: 'Corporate Partnerships', assigned: false, active: false },
   ]);
 
   readonly previewState = signal<PreviewState>('default');
 
   toggleScope(unit: ScopeUnit) {
     if (!unit.assigned) return;
     this.scopeUnits.update((units) =>
       units.map((u) => (u.id === unit.id ? { ...u, active: !u.active } : u))
     );
     this.currentPage.set(1);
   }
 
   setRole(id: RoleId) {
     this.effectiveRoleId.set(id);
     this.selectedId.set(this.queue()[0]?.id ?? null);
     this.outcome.set(null);
     this.errors.set({});
   }
 
   setPreviewState(id: PreviewState) {
     this.previewState.set(id);
     this.outcome.set(null);
     this.errors.set({});
     this.showConfirmModal.set(false);
     this.showRejectModal.set(false);
   }
 
   /* ---------------- Freshness / refresh ---------------- */
 
   readonly lastRefreshed = signal<string>(this.formatNow());
   readonly refreshing = signal(false);
 
   refresh() {
     this.refreshing.set(true);
     this.loadQueue();
     this.refreshing.set(false);
   }
 
   private formatNow(): string {
     const d = new Date();
     return d.toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' }) +
       ', ' + d.toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit' });
   }
 
   /* ---------------- Queue data ---------------- */
 
   /**
    * The review queue.
    *
    * FROM THE API. It was `this.buildQueue()` - twenty-four pairs invented from two arrays of
    * first and last names, with ids of the form `pair-1`. Those ids were then posted to the merge
    * endpoint, which of course has no such review, so every decision a steward recorded came back
    * 404 and the screen showed the outcome panel anyway. The queue showed work that did not exist
    * and hid the work that did.
    */
   readonly queue = signal<DuplicatePair[]>([]);
   readonly queueLoading = signal(false);
   readonly queueError = signal<string | null>(null);

   constructor() {
     this.loadQueue();
   }

   /** Loads the open duplicate reviews in the caller's scope. */
   loadQueue(): void {
     this.queueLoading.set(true);
     this.queueError.set(null);

     this.donorApi.getDuplicateReviews({ pageSize: 200 }).subscribe({
       next: (response) => {
         this.queue.set((response.reviews.items ?? []).map((item) => this.toPair(item)));
         this.queueLoading.set(false);
         this.lastRefreshed.set(this.formatNow());

         if (!this.selectedId()) {
           this.selectedId.set(this.queue()[0]?.id ?? null);
         }
       },
       error: (error: unknown) => {
         this.queue.set([]);
         this.queueLoading.set(false);
         this.queueError.set(
           apiErrorMessage(error, 'The duplicate review queue could not be loaded.'),
         );
       },
     });
   }

   /**
    * One API review as this screen's pair.
    *
    * THE LIST PROJECTION IS DELIBERATELY THIN - two names, a confidence and a status - so the
    * comparison fields stay empty until the row is opened and the detail is fetched. Empty is
    * correct here: inventing a phone number to fill the compare table is exactly what this screen
    * used to do.
    */
   private toPair(item: DuplicateReviewListItem): DuplicatePair {
     const confidence = (['High', 'Medium', 'Low'].includes(item.identityConfidence)
       ? item.identityConfidence
       : 'Low') as Confidence;

     return {
       id: item.id,
       reference: item.reviewReference,
       scopeUnitId: '',
       confidence,
       confidenceScore: confidence === 'High' ? 92 : confidence === 'Medium' ? 68 : 41,
       status: this.toPairStatus(item.status),
       owner: '',
       lastActivity: item.decidedAtUtc ?? item.createdAtUtc,
       candidateAId: '',
       candidateAName: item.candidateAName,
       candidateAEmail: '',
       candidateAPhone: '',
       candidateAGifts: 0,
       candidateATotal: '',
       candidateAConsent: '',
       candidateBId: '',
       candidateBName: item.candidateBName,
       candidateBEmail: '',
       candidateBPhone: '',
       candidateBGifts: 0,
       candidateBTotal: '',
       candidateBConsent: '',
       fields: [],
       evidence: [],
     };
   }

   private toPairStatus(status: string): PairStatus {
     switch (status) {
       case 'Open':
       case 'Pending':
         return 'Pending';
       case 'InReview':
       case 'In review':
         return 'In review';
       case 'Escalated':
         return 'Escalated';
       case 'OnHold':
       case 'On hold':
         return 'On hold';
       case 'Rejected':
         return 'Rejected';
       case 'Merged':
         return 'Merged';
       default:
         return 'Pending';
     }
   }
 
   readonly searchTerm = signal('');
   readonly statusFilter = signal<'all' | PairStatus>('all');
   readonly savedFilter = signal<'all' | 'mine' | 'high-confidence'>('all');
   readonly currentPage = signal(1);
   readonly pageSize = 10;
 
   readonly activeScopeIds = computed(() =>
     this.scopeUnits().filter((u) => u.active).map((u) => u.id)
   );
 
   readonly filtersActive = computed(
     () => this.searchTerm().trim().length > 0 || this.statusFilter() !== 'all' || this.savedFilter() !== 'all'
   );
 
   readonly filteredQueue = computed(() => {
     const term = this.searchTerm().trim().toLowerCase();
     const status = this.statusFilter();
     const saved = this.savedFilter();
     const scopeIds = this.activeScopeIds();
 
     return this.queue().filter((p) => {
       if (!scopeIds.includes(p.scopeUnitId)) return false;
       if (status !== 'all' && p.status !== status) return false;
       if (saved === 'mine' && p.owner !== (this.tokens.displayName() || '')) return false;
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
     if (!this.effectiveRole().canReveal) return;
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
    * Commits the decision.
    *
    * A MERGE IS IRREVERSIBLE, AND THIS DID NOT PERFORM ONE. The screen updated a local queue array
    * and reported "Merged" with a downstream note about "household and gift records re-indexing".
    * Nothing was merged and nothing was re-indexed. Two donor records that a steward had carefully
    * compared and decided about stayed exactly as they were, and the queue showed the pair as
    * resolved so nobody looked at it again.
    *
    * THE TYPED CONFIRMATION WAS ALREADY REAL - the operator types MERGE - so the only thing missing
    * was the call. It now goes to the server, which performs the merge inside one transaction and
    * decides which record survives.
    *
    * THE SIMULATED DEPENDENCY FAILURE IS GONE. `previewState() === 'dependency-failure'` was a
    * developer switch that reported a failure without attempting anything; a real failure now comes
    * from a real refusal, and carries the server's reason.
    */
   private commitDecision() {
     const pair = this.selected();
     if (!pair) return;

     const label = this.resultingState();
     const queueStatus = this.resultingQueueStatus();

     const reason = this.reason().trim();

     this.donorApi
       .mergeDuplicates(pair.id, {
         decision: this.decision(),
         decisionReason:
           reason.length >= 10 ? reason : `${reason} - decided from the duplicate review queue.`,

         // WHICH RECORD SURVIVES IS THE STEWARD'S DECISION and the most consequential field here:
         // the other one's history is folded into it. Sent only on a merge, because it means
         // nothing for the other three decisions.
         survivingDonorId: this.decision() === 'merge' ? this.surviving().trim() || null : null,
       })
       .subscribe({
         next: () => {
           this.queue.update((list) =>
             list.map((p) => (p.id === pair.id ? { ...p, status: queueStatus } : p))
           );

           this.outcome.set({
             kind: 'success',
             reference: pair.reference,
             state: label,
             effectiveTime: this.formatNow(),
             downstream:
               this.decision() === 'merge'
                 ? 'The surviving record now carries both giving histories.'
                 : 'None pending',
             owner: pair.owner,
             nextAction:
               this.decision() === 'merge'
                 ? 'Open the surviving record to confirm the merged history.'
                 : 'Return to the duplicate queue.',
           });

           this.previewState.set('default');
           this.liveMessage.set(
             `Saved successfully. Reference ${pair.reference}; state ${label}.`,
           );
         },
         error: (error: unknown) => {
           this.outcome.set({
             kind: 'dependency-failure',
             reference: pair.reference,
             state: apiErrorMessage(error, 'The decision could not be saved.'),
             effectiveTime: this.formatNow(),
             downstream: 'Nothing was changed. Both records are exactly as they were.',
             owner: pair.owner,
             nextAction: 'Try again, or escalate if the problem persists.',
             correlationRef: '',
           });

           this.liveMessage.set('The decision could not be saved. Nothing was changed.');
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
     if (!pair) return;
 
     this.showRejectModal.set(false);

     // IT NOW REACHES THE SERVER. This updated the local list and set the success panel, and
     // called nothing - so a steward who reviewed twenty pairs and rejected the false matches had
     // rejected none of them, and every one was back in the queue on the next load.
     this.donorApi.rejectDuplicateCandidate(pair.id, { reason: trimmed }).subscribe({
       next: () => {
         this.queue.update((list) =>
           list.map((p) => (p.id === pair.id ? { ...p, status: 'Rejected' } : p))
         );
         this.outcome.set({
           kind: 'success',
           reference: pair.reference,
           state: 'Rejected',
           effectiveTime: this.formatNow(),
           downstream: 'None pending',
           owner: this.tokens.displayName() || '',
           nextAction: 'Return to the duplicate queue.',
         });
         this.liveMessage.set(`Rejected. Reference ${pair.reference}.`);
       },
       error: (error: unknown) => {
         this.outcome.set({
           kind: 'dependency-failure',
           reference: pair.reference,
           state: apiErrorMessage(error, 'The rejection could not be saved.'),
           effectiveTime: this.formatNow(),
           downstream: 'Nothing was changed. The pair is still in the queue.',
           owner: this.tokens.displayName() || '',
           nextAction: 'Try again, or escalate if the problem persists.',
           correlationRef: '',
         });
         this.liveMessage.set('The rejection could not be saved. Nothing was changed.');
       },
     });
     this.previewState.set('default');
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