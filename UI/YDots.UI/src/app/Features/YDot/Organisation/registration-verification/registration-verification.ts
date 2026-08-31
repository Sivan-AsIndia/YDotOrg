import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { OrganisationApiService } from '../../../../Service/organisation-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  OrganisationDetailResponse,
  OrganisationListItemResponse,
  TenantDocumentStatus,
} from '../../../../Shared/models/iam-contract.model';
import { ToastService } from '../../../../Shared/services/toast.service';
import { DocumentSubmissionsComponent } from '../../../../Shared/document-submissions/document-submissions';

type Decision = 'none' | 'approve' | 'reject' | 'suspend' | 'reactivate' | 'archive';

/**
 * Reviewing an Organisation's registration, and deciding on it.
 *
 * WHAT THIS SCREEN IS FOR
 * -----------------------
 * An Organisation submits its profile and its certificates, and somebody on the platform team
 * has to look at them and decide. This is that desk. Without an id in the route it lists
 * everything waiting; with one it shows that submission in full.
 *
 * A REJECTION MUST CARRY A REASON, and the form enforces it before the button is enabled — the
 * server refuses one without, but a refusal after the fact is a worse experience than a disabled
 * button with an explanation beside it. A rejection that says only "no" leaves the organisation
 * with nothing to act on, which turns a decision into a dead end.
 *
 * WHY THE ACTIONS COME FROM THE SERVER
 * ------------------------------------
 * `permittedActions` is computed from the lifecycle state and the reviewer's permissions.
 * Rendering from it means an Organisation that is already approved does not show an Approve
 * button, without this component keeping its own copy of the state machine — which is the copy
 * that drifts.
 *
 * EVERY DECISION CARRIES ExpectedVersion. Two reviewers opening the same submission means the
 * second one is told to reload rather than silently overwriting the first one's decision.
 */
@Component({
  selector: 'app-registration-verification',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, DocumentSubmissionsComponent],
  templateUrl: './registration-verification.html',
  styleUrl: './registration-verification.css',
})
export class RegistrationVerificationComponent implements OnInit, OnDestroy {
  private readonly api = inject(OrganisationApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  private readonly destroy$ = new Subject<void>();

  readonly organisationId = signal<string | null>(null);
  readonly organisation = signal<OrganisationDetailResponse | null>(null);
  readonly queue = signal<OrganisationListItemResponse[]>([]);

  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly deciding = signal(false);
  readonly errorMessage = signal('');

  // ---- Decision dialog -----------------------------------------------------------------------
  readonly decision = signal<Decision>('none');
  readonly reason = signal('');
  readonly notes = signal('');
  readonly activateImmediately = signal(true);

  // ---- Document review -----------------------------------------------------------------------
  readonly reviewingDocument = signal<string | null>(null);
  readonly documentNotes = signal('');

  // =========================================================================================
  // Derived
  // =========================================================================================

  readonly isQueueView = computed(() => this.organisationId() === null);

  readonly permittedActions = computed(() => this.organisation()?.permittedActions ?? []);

  can(action: string): boolean {
    return this.permittedActions().includes(action);
  }

  readonly documents = computed(() => this.organisation()?.documents ?? []);

  /**
   * Files that belong to no grouped submission.
   *
   * Uploaded before submissions existed, so nothing else would ever show them. Filtering on the
   * absence of a submission is also what stops a grouped file being listed twice - once by the
   * submissions component and again in the table beneath it.
   */
  readonly ungroupedDocuments = computed(() =>
    this.documents().filter((document) => !document.submissionId));

  /** Re-reads the organisation after the submissions component changes something. */
  reload(): void {
    this.load();
  }
  readonly timeline = computed(() => this.organisation()?.timeline ?? []);

  /**
   * A rejection needs a reason, an approval does not.
   *
   * Suspension and archiving need one too: both stop people working, and somebody will ask why.
   */
  readonly reasonRequired = computed(
    () => this.decision() === 'reject'
      || this.decision() === 'suspend'
      || this.decision() === 'archive');

  readonly canConfirm = computed(() => {
    if (this.deciding() || this.decision() === 'none') {
      return false;
    }

    return !this.reasonRequired() || this.reason().trim().length >= 10;
  });

  readonly decisionTitle = computed(() => {
    switch (this.decision()) {
      case 'approve': return 'Approve this organisation';
      case 'reject': return 'Send this back';
      case 'suspend': return 'Suspend this organisation';
      case 'reactivate': return 'Lift the suspension';
      case 'archive': return 'Archive this organisation';
      default: return '';
    }
  });

  // =========================================================================================
  // Lifecycle
  // =========================================================================================

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    this.organisationId.set(id);
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.errorMessage.set('');

    const id = this.organisationId();

    if (!id) {
      this.api
        .getAwaitingReview()
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: (items) => {
            this.queue.set(items);
            this.loading.set(false);
          },
          error: (error: unknown) => this.failLoad(error, 'The review queue could not be loaded.'),
        });

      return;
    }

    this.api
      .get(id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (organisation) => {
          this.organisation.set(organisation);
          this.loading.set(false);
        },
        error: (error: unknown) => this.failLoad(error, 'This submission could not be loaded.'),
      });
  }

  private failLoad(error: unknown, fallback: string): void {
    this.loading.set(false);
    this.loadFailed.set(true);
    this.errorMessage.set(apiErrorMessage(error, fallback));
  }

  open(id: string): void {
    void this.router.navigate(['/app/administration/organisation/registration-verification', id]);
  }

  backToQueue(): void {
    void this.router.navigate(['/app/administration/organisation/registration-verification']);
  }

  // =========================================================================================
  // Taking the review
  // =========================================================================================

  /**
   * Claims the submission before deciding on it.
   *
   * It moves the Organisation to "under review" and records who has it, so two people do not
   * spend an afternoon each on the same certificates. It is a courtesy between reviewers rather
   * than a lock — the decision endpoints do not require it.
   */
  startReview(): void {
    const organisation = this.organisation();

    if (!organisation?.id || this.deciding()) {
      return;
    }

    this.deciding.set(true);

    this.api
      .startReview(organisation.id, { expectedVersion: organisation.version ?? 0 })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (outcome) => {
          this.deciding.set(false);
          this.toast.show(
            'Review started',
            outcome.message ?? 'This submission is now marked as under review.',
            'info');
          this.load();
        },
        error: (error: unknown) => this.failDecision(error),
      });
  }

  // =========================================================================================
  // Deciding
  // =========================================================================================

  choose(decision: Decision): void {
    this.decision.set(decision);
    this.reason.set('');
    this.notes.set('');
    this.activateImmediately.set(true);
    this.errorMessage.set('');
  }

  cancelDecision(): void {
    this.decision.set('none');
    this.reason.set('');
    this.notes.set('');
  }

  confirm(): void {
    const organisation = this.organisation();

    if (!organisation?.id || !this.canConfirm()) {
      return;
    }

    this.deciding.set(true);
    this.errorMessage.set('');

    const id = organisation.id;
    const version = organisation.version ?? 0;
    const reason = this.reason().trim();
    const notes = this.notes().trim() || null;

    const call = (() => {
      switch (this.decision()) {
        case 'approve':
          return this.api.review(id, {
            approved: true,
            expectedVersion: version,
            notes,
            // Approving and activating in one step is the normal case: an approved Organisation
            // that still cannot be used needs a second visit from somebody to finish the job.
            activateImmediately: this.activateImmediately(),
          });

        case 'reject':
          return this.api.review(id, {
            approved: false,
            expectedVersion: version,
            reason,
            notes,
          });

        case 'suspend':
          return this.api.suspend(id, { reason, expectedVersion: version });

        case 'reactivate':
          return this.api.reactivate(id, { notes: reason || notes, expectedVersion: version });

        case 'archive':
          return this.api.archive(id, { reason, expectedVersion: version });

        default:
          return null;
      }
    })();

    if (!call) {
      this.deciding.set(false);
      return;
    }

    call.pipe(takeUntil(this.destroy$)).subscribe({
      next: (outcome) => {
        this.deciding.set(false);
        this.decision.set('none');
        this.reason.set('');
        this.notes.set('');

        this.toast.show(
          'Decision recorded',
          outcome.message ?? 'The organisation has been updated.',
          'success');

        this.load();
      },
      error: (error: unknown) => this.failDecision(error),
    });
  }

  private failDecision(error: unknown): void {
    this.deciding.set(false);
    this.errorMessage.set(apiErrorMessage(error, 'That decision could not be recorded.'));
  }

  // =========================================================================================
  // Documents
  // =========================================================================================

  beginDocumentReview(documentId: string): void {
    this.reviewingDocument.set(documentId);
    this.documentNotes.set('');
  }

  cancelDocumentReview(): void {
    this.reviewingDocument.set(null);
    this.documentNotes.set('');
  }

  /**
   * Accepts or refuses one certificate.
   *
   * Reviewed one at a time rather than all-or-nothing, because a submission is usually right in
   * most respects and wrong in one — and "your documents were rejected" with no indication of
   * which is not something anybody can act on.
   */
  decideDocument(documentId: string, status: TenantDocumentStatus): void {
    const organisation = this.organisation();

    if (!organisation?.id || this.deciding()) {
      return;
    }

    if (status === 'rejected' && this.documentNotes().trim().length < 5) {
      this.errorMessage.set('Say what is wrong with the document, so it can be corrected.');
      return;
    }

    this.deciding.set(true);
    this.errorMessage.set('');

    this.api
      .reviewDocument(organisation.id, {
        documentId,
        accepted: status === 'accepted',
        notes: this.documentNotes().trim() || null,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.deciding.set(false);
          this.reviewingDocument.set(null);
          this.documentNotes.set('');
          this.toast.show(
            'Document reviewed',
            status === 'accepted' ? 'The document was accepted.' : 'The document was sent back.',
            status === 'accepted' ? 'success' : 'info');
          this.load();
        },
        error: (error: unknown) => this.failDecision(error),
      });
  }

  // =========================================================================================
  // Display helpers
  // =========================================================================================

  statusClass(status: string | undefined): string {
    switch (status) {
      case 'active':
      case 'approved':
        return 'is-good';
      case 'submitted':
      case 'underReview':
      case 'resubmitted':
        return 'is-warn';
      case 'rejected':
      case 'suspended':
        return 'is-error';
      case 'archived':
        return 'is-muted';
      default:
        return 'is-info';
    }
  }

  documentStatusClass(status: string | undefined): string {
    switch (status) {
      case 'accepted': return 'is-good';
      case 'rejected': return 'is-error';
      case 'superseded': return 'is-muted';
      case 'underReview': return 'is-warn';
      default: return 'is-info';
    }
  }

  /** How long a submission has been waiting, which is the thing a reviewer triages on. */
  waitingDays(since: string | null | undefined): number {
    if (!since) {
      return 0;
    }

    return Math.max(0, Math.floor((Date.now() - Date.parse(since)) / 86_400_000));
  }
}
