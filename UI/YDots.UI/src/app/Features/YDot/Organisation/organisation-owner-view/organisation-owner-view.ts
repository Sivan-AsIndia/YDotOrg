import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OrganisationStateService, EditableOrganisationFields } from '../../../../Service/organisation-state.service';
import { OwnerSessionService } from '../../../../Service/owner-session.service';
import { verificationStatusLabel, verificationBadgeClass, OrganisationDocument } from '../../../../Shared/models/organisation.model';


type OwnerTab = 'info' | 'documents' | 'review';

const ALLOWED_FILE_TYPES = ['application/pdf', 'image/jpeg', 'image/png'];
const MAX_FILE_SIZE_BYTES = 5 * 1024 * 1024;

@Component({
  selector: 'app-organisation-owner-view',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './organisation-owner-view.html',
  styleUrl: './organisation-owner-view.css',
})
export class OrganisationOwnerViewComponent {
  private readonly session = inject(OwnerSessionService);
  protected readonly orgState = inject(OrganisationStateService);

  // Section 18 — the Owner is never routed by Organisation ID; this is the only lookup used.
  protected readonly organisation = computed(() => this.orgState.getById(this.session.currentOrganisationId()));
  protected readonly notFound = computed(() => !this.organisation());

  protected readonly activeTab = signal<OwnerTab>('info');

  protected verificationLabel(): string {
    const org = this.organisation();
    return org ? verificationStatusLabel(org.status) : '';
  }
  protected verificationClass(): string {
    const org = this.organisation();
    return org ? verificationBadgeClass(org.status) : 'org-badge-muted';
  }

  /** Section 3/9 — permitted fields are only editable while Draft or Changes Requested; everything else is read-only. */
  protected canEdit(): boolean {
    const org = this.organisation();
    return org?.status === 'Draft' || org?.status === 'Changes Requested';
  }

  /** Owner can only upload documents; organisation detail fields are read-only regardless of status. */
  protected canEditDetails(): boolean {
    return false;
  }

  // ================= Organisation Information tab =================
  protected readonly infoDraft = signal<EditableOrganisationFields>({});
  private loadedForId = '';

  constructor() {
    // Populate the editable draft once when the Owner's organisation first becomes available
    // (and again only if it changes) — never on unrelated mutations, so an in-progress edit
    // on this tab isn't clobbered by e.g. a document upload elsewhere in the record.
    effect(() => {
      const org = this.organisation();
      if (org && this.loadedForId !== org.id) {
        this.infoDraft.set({
          name: org.name,
          organisationType: org.organisationType,
          legalStructure: org.legalStructure,
          registrationNumber: org.registrationNumber,
          registrationDate: org.registrationDate,
          addressLine1: org.addressLine1,
          addressLine2: org.addressLine2,
          country: org.country,
          state: org.state,
          city: org.city,
          pinCode: org.pinCode,
          email: org.email,
          phone: org.phone,
          alternatePhone: org.alternatePhone,
          website: org.website,
          panTaxId: org.panTaxId,
        });
        this.loadedForId = org.id;
      }
    });
  }

  protected setField<K extends keyof EditableOrganisationFields>(key: K, value: EditableOrganisationFields[K]): void {
    this.infoDraft.update((v) => ({ ...v, [key]: value }));
  }

  protected readonly infoSaved = signal(false);
  protected saveInfo(): void {
    const org = this.organisation();
    const draft = this.infoDraft();
    if (!org || !this.canEdit() || !draft.name?.trim()) return;
    this.orgState.update(org.id, draft, this.session.currentOwnerName());
    this.infoSaved.set(true);
    setTimeout(() => this.infoSaved.set(false), 2500);
  }

  // ================= Documents tab =================
  protected uploadTargetId: string | null = null;

  protected canUpload(doc: OrganisationDocument): boolean {
    return this.canEdit() && doc.status !== 'Not Applicable' && doc.status !== 'Accepted';
  }
  protected actionLabel(doc: OrganisationDocument): string {
    return doc.status === 'Rejected' || doc.status === 'Uploaded' || doc.status === 'Under Review' ? 'Replace' : 'Upload';
  }

  protected readonly uploadError = signal<Record<string, string>>({});

  protected triggerUpload(documentId: string, input: HTMLInputElement): void {
    if (!this.canEdit()) return;
    this.uploadTargetId = documentId;
    input.click();
  }

  protected onFileSelected(documentId: string, event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    if (!ALLOWED_FILE_TYPES.includes(file.type)) {
      this.uploadError.update((e) => ({ ...e, [documentId]: 'Please upload a PDF, JPG or PNG file.' }));
      return;
    }
    if (file.size > MAX_FILE_SIZE_BYTES) {
      this.uploadError.update((e) => ({ ...e, [documentId]: 'File is too large. Maximum size is 5 MB.' }));
      return;
    }
    this.uploadError.update((e) => {
      const next = { ...e };
      delete next[documentId];
      return next;
    });

    const org = this.organisation();
    if (!org) return;
    this.orgState.uploadDocument(org.id, documentId, file.name, this.session.currentOwnerName());
  }

  // ----- View document (metadata only — no real file storage in this environment) -----
  protected readonly viewingDocument = signal<OrganisationDocument | null>(null);
  protected viewDocument(doc: OrganisationDocument): void {
    this.viewingDocument.set(doc);
  }
  protected closeViewDocument(): void {
    this.viewingDocument.set(null);
  }

  protected documentStatusClass(status: OrganisationDocument['status']): string {
    switch (status) {
      case 'Pending':
        return 'org-badge-muted';
      case 'Not Applicable':
        return 'org-badge-muted';
      case 'Uploaded':
      case 'Under Review':
        return 'org-badge-blue';
      case 'Accepted':
        return 'org-badge-good';
      case 'Rejected':
        return 'org-badge-error';
    }
  }

  // ================= Review & Submit tab =================
  protected readonly orgInfoComplete = computed(() => {
    const org = this.organisation();
    return !!(org && org.name.trim() && org.organisationType && org.legalStructure);
  });
  protected readonly ownerInfoComplete = computed(() => {
    const org = this.organisation();
    return !!(org && org.ownerName.trim() && org.ownerEmail.trim() && org.ownerMobile.trim());
  });
  /** Address is optional — never blocks submission — so this only reflects whether anything was entered, for the Owner's own information. */
  protected readonly addressProvided = computed(() => {
    const org = this.organisation();
    return !!(org && (org.addressLine1.trim() || org.country.trim() || org.state.trim() || org.city.trim() || org.pinCode.trim()));
  });
  protected readonly contactComplete = computed(() => {
    const org = this.organisation();
    return !!(org && org.email.trim() && org.phone.trim());
  });
  protected readonly complianceComplete = computed(() => {
    const org = this.organisation();
    return !!(org && org.panTaxId.trim());
  });
  protected readonly requiredDocuments = computed(() => this.organisation()?.documents.filter((d) => d.required) ?? []);
  protected readonly missingDocumentCount = computed(
    () => this.requiredDocuments().filter((d) => d.status !== 'Uploaded' && d.status !== 'Under Review' && d.status !== 'Accepted').length,
  );
  protected readonly documentsComplete = computed(() => this.missingDocumentCount() === 0);

  protected readonly checklistItems = computed(() => {
    const missing = this.missingDocumentCount();
    return [
      { icon: 'ri-building-4-line', label: 'Organisation Information', complete: this.orgInfoComplete(), detail: this.orgInfoComplete() ? 'Complete' : 'Incomplete' },
      { icon: 'ri-user-3-line', label: 'Owner Information', complete: this.ownerInfoComplete(), detail: this.ownerInfoComplete() ? 'Complete' : 'Incomplete' },
      { icon: 'ri-map-pin-line', label: 'Address (optional)', complete: true, detail: this.addressProvided() ? 'Provided' : 'Not provided' },
      { icon: 'ri-phone-line', label: 'Contact Information', complete: this.contactComplete(), detail: this.contactComplete() ? 'Complete' : 'Incomplete' },
      { icon: 'ri-shield-check-line', label: 'Compliance', complete: this.complianceComplete(), detail: this.complianceComplete() ? 'Complete' : 'Incomplete' },
      { icon: 'ri-file-list-3-line', label: 'Required Documents', complete: this.documentsComplete(), detail: this.documentsComplete() ? 'Complete' : `${missing} Missing` },
    ];
  });
  protected readonly checklistProgress = computed(() => {
    const items = this.checklistItems();
    const completed = items.filter((i) => i.complete).length;
    return { completed, total: items.length, percent: Math.round((completed / items.length) * 100) };
  });

  protected readonly declarationChecked = signal(false);

  protected readonly readyToSubmit = computed(() => {
    const org = this.organisation();
    if (!org) return false;
    return this.orgState.canApprove(org).ok;
  });

  protected readonly submitBlockedReason = computed(() => {
    const org = this.organisation();
    if (!org) return '';
    return this.orgState.canApprove(org).reason ?? '';
  });

  protected readonly submitting = signal(false);
  protected readonly submitError = signal('');

  protected submit(): void {
    const org = this.organisation();
    if (!org || !this.canEdit() || !this.readyToSubmit() || !this.declarationChecked() || this.submitting()) return;
    this.submitting.set(true);
    this.submitError.set('');
    setTimeout(() => {
      const result = this.orgState.submitForVerification(org.id, this.session.currentOwnerName());
      this.submitting.set(false);
      if (!result.ok) {
        this.submitError.set(result.message ?? 'Unable to submit this organisation.');
        return;
      }
      this.declarationChecked.set(false);
    }, 600);
  }
}
