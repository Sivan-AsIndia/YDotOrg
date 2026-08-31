import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import {
  DocumentDecision,
  DocumentDownloadLink,
  DocumentSubmission,
  DocumentSubmissionApiService,
  DocumentUploadPolicy,
  SubmissionFile,
} from '../../Service/document-submission-api.service';

/** Which side of the desk this component is drawn for. */
export type DocumentSubmissionsMode = 'tenant' | 'review';

/**
 * Grouped document submissions: uploading them, and deciding on them.
 *
 * ONE COMPONENT FOR BOTH AUDIENCES, because they are the same object seen from two sides and
 * two components would drift. `mode` picks the API surface and which controls appear; everything
 * else — the grouping, the preview, the metadata — is identical, and identical is the point.
 * A reviewer should see exactly what the uploader sees.
 *
 * IT NEVER DECIDES WHAT IS ALLOWED. Every button is drawn from `permittedActions`, which the
 * server computes from the same state machine it will consult when the button is pressed. A rule
 * re-implemented here in TypeScript would drift from the handler, and the visible result is a
 * button that produces a 409.
 *
 * THE LIMITS COME FROM THE SERVER TOO. `policy` is fetched, not hard-coded, so the sentence above
 * the drop zone and the rule that refuses the file are the same number.
 */
@Component({
  selector: 'app-document-submissions',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './document-submissions.html',
  styleUrl: './document-submissions.css',
})
export class DocumentSubmissionsComponent implements OnInit {
  private readonly api = inject(DocumentSubmissionApiService);
  private readonly sanitizer = inject(DomSanitizer);

  /** 'tenant' assembles and sends; 'review' decides. */
  readonly mode = input.required<DocumentSubmissionsMode>();

  /** Required in review mode; ignored in tenant mode, where the server knows the Organisation. */
  readonly tenantId = input<string | null>(null);

  /** Raised after anything that changes state, so the host screen can refresh its own summary. */
  readonly changed = output<void>();

  readonly submissions = signal<DocumentSubmission[]>([]);
  readonly policy = signal<DocumentUploadPolicy | null>(null);
  readonly loading = signal(true);
  readonly errorMessage = signal('');
  readonly busy = signal(false);

  /** Which submission's drop zone is lit up, so only the one under the pointer highlights. */
  readonly dragOver = signal<string | null>(null);

  /** Per-file progress, keyed by submission id. */
  readonly uploading = signal<{ submissionId: string; fileName: string; percent: number } | null>(null);

  readonly preview = signal<{ link: DocumentDownloadLink; safeUrl: SafeResourceUrl } | null>(null);

  // ---- The new-submission form -------------------------------------------------------------

  readonly showNewForm = signal(false);
  readonly newDocumentType = signal('RegistrationCertificate');
  readonly newTitle = signal('');
  readonly newNotes = signal('');

  /** Kept in step with TenantDocumentType on the server. */
  readonly documentTypes = [
    { value: 'RegistrationCertificate', label: 'Registration certificate' },
    { value: 'TaxExemptionCertificate', label: 'Tax exemption certificate' },
    { value: 'PanCard', label: 'PAN card' },
    { value: 'GstCertificate', label: 'GST certificate' },
    { value: 'AddressProof', label: 'Address proof' },
    { value: 'BankProof', label: 'Bank proof' },
    { value: 'TrustDeed', label: 'Trust deed' },
    { value: 'AnnualReport', label: 'Annual report' },
    { value: 'AuthorisedSignatoryProof', label: 'Authorised signatory proof' },
    { value: 'Other', label: 'Other' },
  ];

  // ---- The decision form -------------------------------------------------------------------

  readonly decidingOn = signal<string | null>(null);
  readonly decision = signal<DocumentDecision>('Approve');
  readonly decisionNotes = signal('');

  readonly isReview = computed(() => this.mode() === 'review');

  /** "PDF, PNG, JPEG or Word, up to 5 MB" — assembled from what the server actually accepts. */
  readonly acceptSummary = computed(() => {
    const policy = this.policy();
    if (!policy) {
      return '';
    }

    const kinds = policy.allowedExtensions
      .map((extension) => extension.replace('.', '').toUpperCase())
      .join(', ');

    return `${kinds} · up to ${policy.maximumFileSizeMegabytes} MB each`;
  });

  /** The `accept` attribute for the file picker, so the dialog filters before a person chooses. */
  readonly acceptAttribute = computed(() =>
    (this.policy()?.allowedExtensions ?? []).join(','));

  readonly pendingCount = computed(() =>
    this.submissions().filter((submission) =>
      submission.status === 'submitted' || submission.status === 'underReview').length);

  ngOnInit(): void {
    this.api.getPolicy().subscribe({
      next: (policy) => this.policy.set(policy),
      // A missing policy is not fatal: the server still enforces every limit. The drop zone
      // simply cannot name them until the call succeeds.
      error: () => undefined,
    });

    this.load();
  }

  load(): void {
    this.loading.set(true);

    const request = this.isReview()
      ? this.api.getForOrganisation(this.tenantId()!)
      : this.api.getMine();

    request.subscribe({
      next: (submissions) => {
        this.submissions.set(submissions);
        this.loading.set(false);
      },
      error: (error) => {
        this.errorMessage.set(error?.message ?? 'The submissions could not be loaded.');
        this.loading.set(false);
      },
    });
  }

  can(submission: DocumentSubmission, action: string): boolean {
    return submission.permittedActions.includes(action);
  }

  // ---- Creating -----------------------------------------------------------------------------

  createSubmission(): void {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.errorMessage.set('');

    this.api.createMine({
      documentType: this.newDocumentType(),
      title: this.newTitle().trim() || null,
      notes: this.newNotes().trim() || null,
    }).subscribe({
      next: (submission) => {
        this.submissions.update((current) => [submission, ...current]);
        this.showNewForm.set(false);
        this.newTitle.set('');
        this.newNotes.set('');
        this.busy.set(false);
        this.changed.emit();
      },
      error: (error) => {
        this.errorMessage.set(error?.message ?? 'The submission could not be started.');
        this.busy.set(false);
      },
    });
  }

  // ---- Uploading ----------------------------------------------------------------------------

  onDragOver(event: DragEvent, submissionId: string): void {
    event.preventDefault();
    this.dragOver.set(submissionId);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(null);
  }

  onDrop(event: DragEvent, submission: DocumentSubmission): void {
    event.preventDefault();
    this.dragOver.set(null);

    const files = Array.from(event.dataTransfer?.files ?? []);
    this.queue(submission, files);
  }

  onFilePicked(event: Event, submission: DocumentSubmission): void {
    const input = event.target as HTMLInputElement;
    this.queue(submission, Array.from(input.files ?? []));

    // Cleared so choosing the same file twice in a row still raises a change event.
    input.value = '';
  }

  /**
   * Uploads files one after another.
   *
   * SEQUENTIALLY, not in parallel. Each response carries the whole submission, and firing three
   * at once means three responses each describing a submission that was true before the other
   * two landed — the last to arrive wins and the list loses files.
   */
  private queue(submission: DocumentSubmission, files: File[]): void {
    const policy = this.policy();
    if (files.length === 0 || this.busy()) {
      return;
    }

    // Checked here purely so the person is told immediately rather than after the bytes have
    // gone up. The server checks it again and its answer is the one that counts.
    const tooBig = policy
      ? files.find((file) => file.size > policy.maximumFileSizeBytes)
      : undefined;

    if (tooBig && policy) {
      const megabytes = (tooBig.size / 1024 / 1024).toFixed(1);
      this.errorMessage.set(
        `"${tooBig.name}" is ${megabytes} MB. The limit is ${policy.maximumFileSizeMegabytes} MB.`);
      return;
    }

    this.errorMessage.set('');
    this.uploadNext(submission.id, files, 0);
  }

  private uploadNext(submissionId: string, files: File[], index: number): void {
    if (index >= files.length) {
      this.uploading.set(null);
      this.busy.set(false);
      this.changed.emit();
      return;
    }

    this.busy.set(true);

    const file = files[index];
    this.uploading.set({ submissionId, fileName: file.name, percent: 0 });

    this.api.uploadFile(submissionId, file).subscribe({
      next: (event) => {
        if (event?.kind === 'progress') {
          this.uploading.set({ submissionId, fileName: file.name, percent: event.percent });
        } else if (event?.kind === 'done') {
          this.replace(event.submission);
          this.uploadNext(submissionId, files, index + 1);
        }
      },
      error: (error) => {
        this.errorMessage.set(error?.message ?? `"${file.name}" could not be uploaded.`);
        this.uploading.set(null);
        this.busy.set(false);
      },
    });
  }

  removeFile(submission: DocumentSubmission, file: SubmissionFile): void {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);

    this.api.removeFile(submission.id, file.id).subscribe({
      next: (updated) => {
        this.replace(updated);
        this.busy.set(false);
        this.changed.emit();
      },
      error: (error) => {
        this.errorMessage.set(error?.message ?? 'The file could not be removed.');
        this.busy.set(false);
      },
    });
  }

  /**
   * Whether this submission can still be withdrawn.
   *
   * A DRAFT AND NOTHING ELSE, and only on the organisation's own screen. Once it has been sent,
   * somebody is deciding on it and pulling it out from under them is not an option a button
   * should offer; the server refuses it either way.
   */
  canDiscard(submission: DocumentSubmission): boolean {
    return !this.isReview() && submission.status === 'Draft';
  }

  /** Withdraws a draft. Asks first: it takes any attached files with it. */
  discardDraft(submission: DocumentSubmission): void {
    if (this.busy() || !this.canDiscard(submission)) {
      return;
    }

    const attached = submission.files?.length ?? 0;

    const question = attached > 0
      ? `Discard this draft and the ${attached} file${attached === 1 ? '' : 's'} attached to it?`
      : 'Discard this empty draft?';

    if (!window.confirm(question)) {
      return;
    }

    this.busy.set(true);

    this.api.discardMine(submission.id, submission.version).subscribe({
      next: () => {
        this.submissions.update((list) => list.filter((item) => item.id !== submission.id));
        this.busy.set(false);
        this.changed.emit();
      },
      error: (error) => {
        this.errorMessage.set(error?.message ?? 'The draft could not be discarded.');
        this.busy.set(false);
      },
    });
  }

  submitForReview(submission: DocumentSubmission): void {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);

    this.api.submitMine(submission.id, submission.version).subscribe({
      next: (updated) => {
        this.replace(updated);
        this.busy.set(false);
        this.changed.emit();
      },
      error: (error) => {
        this.errorMessage.set(error?.message ?? 'It could not be sent for review.');
        this.busy.set(false);
      },
    });
  }

  // ---- Opening files -------------------------------------------------------------------------

  /**
   * Opens a file in the preview pane, or saves it.
   *
   * THE LINK IS FETCHED AT THE MOMENT IT IS NEEDED, never held on the model. It expires in
   * minutes by design, so one obtained when the list was drawn would be dead by the time
   * somebody clicked it.
   */
  open(submission: DocumentSubmission, file: SubmissionFile, inline: boolean): void {
    const request = this.isReview()
      ? this.api.getReviewFileLink(this.tenantId()!, submission.id, file.id, inline)
      : this.api.getMyFileLink(submission.id, file.id, inline);

    request.subscribe({
      next: (link) => {
        if (inline && link.isPreviewable) {
          this.preview.set({
            link,
            // The URL is minted by our own API from our own object store. Angular cannot know
            // that, so it is marked trusted explicitly rather than being silently blocked.
            safeUrl: this.sanitizer.bypassSecurityTrustResourceUrl(link.url),
          });
          return;
        }

        window.open(link.url, '_blank', 'noopener');
      },
      error: (error) =>
        this.errorMessage.set(error?.message ?? 'That file could not be opened.'),
    });
  }

  closePreview(): void {
    this.preview.set(null);
  }

  // ---- Deciding -------------------------------------------------------------------------------

  startReview(submission: DocumentSubmission): void {
    this.busy.set(true);

    this.api.startReview(this.tenantId()!, submission.id).subscribe({
      next: (updated) => {
        this.replace(updated);
        this.busy.set(false);
        this.changed.emit();
      },
      error: (error) => {
        this.errorMessage.set(error?.message ?? 'The review could not be started.');
        this.busy.set(false);
      },
    });
  }

  beginDecision(submission: DocumentSubmission, decision: DocumentDecision): void {
    this.decidingOn.set(submission.id);
    this.decision.set(decision);
    this.decisionNotes.set('');
  }

  cancelDecision(): void {
    this.decidingOn.set(null);
    this.decisionNotes.set('');
  }

  /** A reason is required for anything but an approval — the same rule the server applies. */
  readonly decisionNeedsNotes = computed(() => this.decision() !== 'Approve');

  confirmDecision(submission: DocumentSubmission): void {
    if (this.busy()) {
      return;
    }

    if (this.decisionNeedsNotes() && !this.decisionNotes().trim()) {
      this.errorMessage.set('Give a reason, so the organisation knows what to change.');
      return;
    }

    this.busy.set(true);
    this.errorMessage.set('');

    this.api.decide(
      this.tenantId()!, submission.id, this.decision(), submission.version,
      this.decisionNotes().trim() || null,
    ).subscribe({
      next: (updated) => {
        this.replace(updated);
        this.decidingOn.set(null);
        this.decisionNotes.set('');
        this.busy.set(false);
        this.changed.emit();
      },
      error: (error) => {
        this.errorMessage.set(error?.message ?? 'The decision could not be recorded.');
        this.busy.set(false);
      },
    });
  }

  // ---- Presentation ---------------------------------------------------------------------------

  private replace(submission: DocumentSubmission): void {
    this.submissions.update((current) =>
      current.map((item) => (item.id === submission.id ? submission : item)));
  }

  /** Bytes as a person reads them. 556793 is not a file size anybody recognises. */
  size(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }

    if (bytes < 1024 * 1024) {
      return `${Math.round(bytes / 1024)} KB`;
    }

    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  statusClass(status: string): string {
    switch (status) {
      case 'approved': return 'is-approved';
      case 'rejected': return 'is-rejected';
      case 'reuploadRequested': return 'is-returned';
      case 'submitted':
      case 'underReview': return 'is-pending';
      default: return 'is-draft';
    }
  }

  fileIcon(contentType: string): string {
    if (contentType === 'application/pdf') {
      return 'ri-file-pdf-2-line';
    }

    if (contentType.startsWith('image/')) {
      return 'ri-image-line';
    }

    if (contentType.includes('sheet') || contentType.includes('excel')) {
      return 'ri-file-excel-2-line';
    }

    return 'ri-file-word-2-line';
  }

  trackById(_index: number, item: { id: string }): string {
    return item.id;
  }
}
