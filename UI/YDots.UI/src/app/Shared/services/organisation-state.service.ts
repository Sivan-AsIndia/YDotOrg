import { Injectable, computed, effect, inject, signal } from '@angular/core';

import { ToastService } from './toast.service';
import { OrganisationRecord, OrganisationCompliance, OrganisationStatus, buildDocumentChecklist, ORG_STATUS_BADGE_CLASS, OrganisationDocument, OrganisationAuditEntry, ORG_STATUS_TRANSITIONS } from '../models/organisation.model';
import { OrganisationApiService } from '../../Service/organisation-api.service';
import { OrganisationListItemResponse } from '../models/iam-contract.model';
import { OrganisationScopeService } from './organisation-scope.service';


export interface CreateOrganisationInput {
  name: string;
  organisationType: OrganisationRecord['organisationType'];
  legalStructure: OrganisationRecord['legalStructure'];
  registrationNumber: string;
  registrationDate: string;
  addressLine1: string;
  addressLine2: string;
  country: string;
  state: string;
  city: string;
  pinCode: string;
  email: string;
  phone: string;
  alternatePhone: string;
  website: string;
  panTaxId: string;
  compliance: OrganisationCompliance;
  ownerName: string;
  ownerEmail: string;
  ownerMobile: string;
  ownerDesignation: string;
}

export type EditableOrganisationFields = Partial<
  Pick<
    OrganisationRecord,
    | 'name'
    | 'organisationType'
    | 'legalStructure'
    | 'registrationNumber'
    | 'registrationDate'
    | 'addressLine1'
    | 'addressLine2'
    | 'country'
    | 'state'
    | 'city'
    | 'pinCode'
    | 'email'
    | 'phone'
    | 'alternatePhone'
    | 'website'
    | 'panTaxId'
    | 'ownerName'
    | 'ownerEmail'
    | 'ownerMobile'
    | 'ownerDesignation'
  >
>;

/**
 * Single shared source of truth for the Organisation Master Record (Section 12).
 * Every Super Admin Organisation screen — Directory, Create, Owner Login Entry,
 * Details, Verification & Approval — reads and writes this same service, keyed by
 * Organisation ID, so a status change or edit made on one screen is immediately
 * visible on every other screen (Section 9/18).
 */
@Injectable({ providedIn: 'root' })
export class OrganisationStateService {
  private readonly toast = inject(ToastService);
  // Bumped to v2 when Address was split into addressLine1/addressLine2 — prevents old-shape
  // cached records (missing the new fields) from crashing the app with `.trim()` on undefined.
  private static readonly storageKey = 'ydot-organisation-master-v2';

  private buildSeed(): OrganisationRecord[] {
    const compliance = (twelveA: boolean, eightyG: boolean, fcra: boolean, gst: boolean): OrganisationCompliance => ({
      twelveA: { applicable: twelveA, number: twelveA ? '12A-2024-0091' : '' },
      eightyG: { applicable: eightyG, number: eightyG ? '80G-2024-0091' : '' },
      fcra: { applicable: fcra, number: fcra ? 'FCRA-2024-0091' : '' },
      gst: { applicable: gst, number: gst ? '29ABCDE1234F1Z5' : '' },
    });

    const base = (
      id: string,
      name: string,
      status: OrganisationStatus,
      overrides: Partial<OrganisationRecord> = {},
    ): OrganisationRecord => {
      const comp = overrides.compliance ?? compliance(true, true, false, true);
      const documents = buildDocumentChecklist(comp);
      return {
        id,
        name,
        organisationType: 'Non-Profit / NGO',
        legalStructure: 'Trust',
        registrationNumber: `REG-${id.slice(4)}`,
        registrationDate: '2024-04-01',
        addressLine1: '12 MG Road',
        addressLine2: '',
        country: 'India',
        state: 'Maharashtra',
        city: 'Mumbai',
        pinCode: '400001',
        email: `contact@${name.toLowerCase().replace(/[^a-z]+/g, '')}.org`,
        phone: '+91 98765 43210',
        alternatePhone: '',
        website: '',
        panTaxId: 'AAACT1234F',
        compliance: comp,
        ownerId: `OWN-${id.slice(4)}`,
        ownerName: 'Owner Pending',
        ownerEmail: `owner@${name.toLowerCase().replace(/[^a-z]+/g, '')}.org`,
        ownerMobile: '+91 90000 00000',
        ownerDesignation: 'Trustee',
        ownerAccountStatus: 'Pending Setup',
        status,
        statusClass: ORG_STATUS_BADGE_CLASS[status],
        documents,
        createdBy: 'Super Admin',
        createdDate: '2026-01-10',
        updatedDate: '2026-01-10',
        ...overrides,
      };
    };

    const uploadedDocs = (docs: OrganisationDocument[]): OrganisationDocument[] =>
      docs.map((d) => (d.required ? { ...d, status: 'Uploaded', uploadedDate: '2026-06-01', uploadedBy: 'Organisation Owner' } : d));

    const acceptedDocs = (docs: OrganisationDocument[]): OrganisationDocument[] =>
      docs.map((d) => (d.required ? { ...d, status: 'Accepted', uploadedDate: '2026-05-01', uploadedBy: 'Organisation Owner' } : d));

    const seedActive = base('ORG-000001', 'Green Earth Foundation', 'Active', {
      ownerName: 'Sarah Johnson',
      ownerEmail: 'sarah.johnson@greenearth.org',
      ownerAccountStatus: 'Active',
      verifiedBy: 'Super Admin',
      verifiedDate: '2026-06-15',
      updatedDate: '2026-06-15',
    });
    seedActive.documents = acceptedDocs(seedActive.documents);

    const seedPendingVerification = base('ORG-000002', 'Helping Hands Trust', 'Pending Verification', {
      ownerName: 'Priya Singh',
      ownerEmail: 'priya.singh@helpinghands.org',
      ownerAccountStatus: 'Active',
      updatedDate: '2026-08-10',
    });
    seedPendingVerification.documents = uploadedDocs(seedPendingVerification.documents);

    const seedIncompletePendingVerification = base('ORG-000003', 'Bright Future Initiative', 'Pending Verification', {
      ownerName: 'Michael Lee',
      ownerEmail: 'michael.lee@brightfuture.org',
      ownerAccountStatus: 'Active',
      updatedDate: '2026-08-15',
      // Required documents intentionally left Pending — demonstrates the approval-blocking rule (Section 8/15).
    });

    const seedChangesRequested = base('ORG-000004', 'Women Empowerment Org.', 'Changes Requested', {
      ownerName: 'Anjali Rao',
      ownerEmail: 'anjali.rao@womenempowerment.org',
      ownerAccountStatus: 'Active',
      changeRequestReason: 'PAN document is unclear. Please upload a readable copy.',
      requestedBy: 'Super Admin',
      requestedDate: '2026-07-20',
      updatedDate: '2026-07-20',
    });

    const seedDraft = base('ORG-000005', 'Care & Share Foundation', 'Draft', {
      ownerName: 'Ravi Menon',
      ownerEmail: 'ravi.menon@careandshare.org',
    });

    const seedSuspended = base('ORG-000006', 'Community Support Group', 'Suspended', {
      ownerName: 'David Fernandes',
      ownerEmail: 'david.fernandes@communitysupport.org',
      ownerAccountStatus: 'Active',
      verifiedBy: 'Super Admin',
      verifiedDate: '2025-12-01',
      updatedDate: '2026-03-10',
    });
    seedSuspended.documents = acceptedDocs(seedSuspended.documents);

    return [seedActive, seedPendingVerification, seedIncompletePendingVerification, seedChangesRequested, seedDraft, seedSuspended];
  }

  private readonly api = inject(OrganisationApiService);
  private readonly organisationScope = inject(OrganisationScopeService);

  /**
   * The organisations.
   *
   * WHAT CHANGED, AND WHY IT MATTERED. This was `signal(this.buildSeed())` - six invented
   * organisations with invented owners, registration numbers, documents and verification dates,
   * compiled into the bundle. Three consequences followed:
   *
   *   - A PLATFORM ADMINISTRATOR SAW SIX CHARITIES THAT DO NOT EXIST, and could approve, suspend
   *     or archive them. The screen reported success every time.
   *   - THE REAL ORGANISATIONS WERE INVISIBLE. Whatever IAM actually held never appeared here.
   *   - THE TWO COPIES OF THIS FILE HAD DRIFTED APART, so three screens listed one set of
   *     organisations and a fourth listed another.
   *
   * It now reads `IAM /api/v1/organisations`. The synchronous signal surface is kept because the
   * screens read `records()` from templates and computed properties.
   */
  readonly records = signal<OrganisationRecord[]>([]);

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  /** The API id per organisation code, so a screen working in codes can still act. */
  private readonly idsByCode = new Map<string, string>();

  /** Reloads from IAM. */
  refresh(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    this.api.search({ pageSize: 200 }).subscribe({
      next: (page) => {
        this.idsByCode.clear();

        for (const item of page.items ?? []) {
          if (item.code && item.id) {
            this.idsByCode.set(item.code, item.id);
          }
        }

        this.records.set((page.items ?? []).map((item) => this.toRecord(item)));
        this.isLoading.set(false);
      },
      error: () => {
        this.records.set([]);
        this.isLoading.set(false);
        this.loadError.set('The organisations could not be loaded.');
      },
    });
  }

  /**
   * One API organisation as the screens read it.
   *
   * THE FIELDS THE LIST PROJECTION DOES NOT CARRY ARE LEFT EMPTY rather than filled with
   * plausible values. The documents, the registration number and the verification trail come from
   * the detail endpoint; a screen that needs them asks for them.
   */
  private toRecord(item: OrganisationListItemResponse): OrganisationRecord {
    return {
      id: item.code ?? item.id ?? '',
      name: item.name ?? '',
      status: (item.statusDisplay ?? item.status ?? 'Draft') as OrganisationStatus,
      // THE LIST PROJECTION CARRIES THE ADMIN'S E-MAIL AND NOTHING ELSE ABOUT THEM. The owner's
      // name, their account status, the registration number and the verification trail are on the
      // DETAIL endpoint, and a screen that needs them asks for them. Filling them with plausible
      // values here is what the seeded version did.
      ownerName: '',
      ownerEmail: item.adminEmail ?? '',
      ownerAccountStatus: '',
      registrationNumber: '',
      createdDate: (item.createdAtUtc ?? '').slice(0, 10),
      updatedDate: (item.updatedAtUtc ?? item.createdAtUtc ?? '').slice(0, 10),
      verifiedBy: '',
      verifiedDate: '',

      // `isAwaitingReview` is the server's own answer to "does this need somebody to look at it",
      // which is the question the review queue is filtering on.
      awaitingReview: item.isAwaitingReview === true,

      documents: [],
    } as unknown as OrganisationRecord;
  }
  readonly auditLog = signal<OrganisationAuditEntry[]>([]);

  readonly totalOrganisations = computed(() => this.records().length);
  readonly activeOrganisations = computed(() => this.records().filter((r) => r.status === 'Active').length);
  readonly pendingVerificationCount = computed(() => this.records().filter((r) => r.status === 'Pending Verification').length);
  readonly changesRequestedCount = computed(() => this.records().filter((r) => r.status === 'Changes Requested').length);

  constructor() {
    this.refresh();
    this.hydrateFromStorage();
    effect(() => {
      try {
        sessionStorage.setItem(
          OrganisationStateService.storageKey,
          JSON.stringify({ records: this.records(), auditLog: this.auditLog() }),
        );
      } catch {
        // Storage unavailable (private browsing, quota) — falls back to in-memory-only for this session.
      }
    });

    /**
     * THIS LIST BELONGS TO NOBODY IN PARTICULAR AND THAT IS THE PROBLEM.
     *
     * It is the PLATFORM directory — every Organisation on the platform, each with its
     * administrator's e-mail address — and it was the one Organisation-sensitive cache in the app
     * that neither cleared on sign-out nor reloaded on a switch. Two consequences followed, and
     * the second is the one that matters:
     *
     *   - IT WENT STALE ACROSS A SWITCH. Approve an Organisation from inside another one and the
     *     directory still showed the status it had when the tab was opened.
     *   - IT SURVIVED SIGN-OUT. `AuthTokenService.clear()` removes the `ydot.*` keys and knew
     *     nothing about this one, so a snapshot of every Organisation stayed in `sessionStorage`
     *     for whoever signed in next in the same tab.
     *
     * Registering here means the cache is now keyed to the session like everything else: dropped
     * when nobody is signed in, refetched whenever the operating Organisation changes. The API is
     * still the authority — a caller without `platform.organisations.view` gets a 403 and an empty
     * list, exactly as they did before.
     */
    this.organisationScope.onOrganisationChange((scope) => {
      if (scope === null) {
        this.discard();
        return;
      }

      this.refresh();
    });
  }

  /**
   * Forgets the directory, including the stored copy.
   *
   * Called on sign-out rather than reloading: there is no longer anybody to load it for, and
   * leaving the snapshot behind is what let the next person in the tab start with it.
   */
  private discard(): void {
    this.records.set([]);
    this.auditLog.set([]);
    this.idsByCode.clear();
    this.loadError.set(null);

    try {
      sessionStorage.removeItem(OrganisationStateService.storageKey);
    } catch {
      // Storage unavailable — the signals above are cleared either way.
    }
  }

  private hydrateFromStorage(): void {
    let raw: string | null;
    try {
      raw = sessionStorage.getItem(OrganisationStateService.storageKey);
    } catch {
      return;
    }
    if (!raw) return;
    try {
      const parsed = JSON.parse(raw);
      if (Array.isArray(parsed?.records) && parsed.records.length) this.records.set(parsed.records);
      if (Array.isArray(parsed?.auditLog)) this.auditLog.set(parsed.auditLog);
    } catch {
      // Corrupt/incompatible stored snapshot — keep the seeded defaults already assigned above.
    }
  }

  getById(id: string | null | undefined): OrganisationRecord | undefined {
    if (!id) return undefined;
    return this.records().find((r) => r.id === id);
  }

  canTransition(from: OrganisationStatus, to: OrganisationStatus): boolean {
    return ORG_STATUS_TRANSITIONS[from]?.includes(to) ?? false;
  }

  isDuplicate(name: string, registrationNumber: string, excludeId?: string): boolean {
    const n = name.trim().toLowerCase();
    const reg = registrationNumber.trim().toLowerCase();
    return this.records().some(
      (r) => r.id !== excludeId && (r.name.trim().toLowerCase() === n || (reg && r.registrationNumber.trim().toLowerCase() === reg)),
    );
  }

  private nextId(): string {
    const maxSeq = this.records().reduce((max, r) => {
      const n = Number(r.id.replace('ORG-', ''));
      return Number.isFinite(n) ? Math.max(max, n) : max;
    }, 0);
    return `ORG-${String(maxSeq + 1).padStart(6, '0')}`;
  }

  private addAudit(organisationId: string, action: string, performedBy: string, opts?: { oldValue?: string; newValue?: string; reason?: string }): void {
    this.auditLog.update((list) => [
      {
        id: `AUD-${Date.now()}-${Math.round(Math.random() * 999)}`,
        organisationId,
        action,
        performedBy,
        performedRole: 'Super Admin',
        timestamp: new Date().toISOString(),
        ...opts,
      },
      ...list,
    ]);
  }

  auditFor(organisationId: string): OrganisationAuditEntry[] {
    return this.auditLog().filter((a) => a.organisationId === organisationId);
  }

  /** Section 4 — creates the Organisation Master Record. Status=Draft, Verification=Not Submitted. */
  create(input: CreateOrganisationInput, actor: string): OrganisationRecord {
    const timestamp = new Date().toISOString();
    const record: OrganisationRecord = {
      id: this.nextId(),
      name: input.name.trim(),
      organisationType: input.organisationType,
      legalStructure: input.legalStructure,
      registrationNumber: input.registrationNumber.trim(),
      registrationDate: input.registrationDate,
      addressLine1: input.addressLine1.trim(),
      addressLine2: input.addressLine2.trim(),
      country: input.country.trim(),
      state: input.state.trim(),
      city: input.city.trim(),
      pinCode: input.pinCode.trim(),
      email: input.email.trim(),
      phone: input.phone.trim(),
      alternatePhone: input.alternatePhone.trim(),
      website: input.website.trim(),
      panTaxId: input.panTaxId.trim(),
      compliance: input.compliance,
      ownerId: '',
      ownerName: input.ownerName.trim(),
      ownerEmail: input.ownerEmail.trim(),
      ownerMobile: input.ownerMobile.trim(),
      ownerDesignation: input.ownerDesignation.trim(),
      ownerAccountStatus: 'Pending Setup',
      status: 'Draft',
      statusClass: ORG_STATUS_BADGE_CLASS.Draft,
      documents: buildDocumentChecklist(input.compliance),
      createdBy: actor,
      createdDate: timestamp.slice(0, 10),
      updatedDate: timestamp.slice(0, 10),
    };
    record.ownerId = `OWN-${record.id.slice(4)}`;

    this.records.update((list) => [record, ...list]);
    this.addAudit(record.id, 'Organisation Created', actor, { newValue: 'Status: Draft' });
    this.addAudit(record.id, 'Owner Assigned', actor, { newValue: `${record.ownerName} <${record.ownerEmail}>` });
    this.toast.show('Organisation Created', `${record.name} has been created as Draft.`, 'success');
    return record;
  }

  /** Section 6 Edit — updates the existing record; never creates a new one. */
  update(id: string, patch: EditableOrganisationFields, actor: string): boolean {
    const record = this.getById(id);
    if (!record) return false;
    const timestamp = new Date().toISOString();
    this.records.update((list) => list.map((r) => (r.id === id ? { ...r, ...patch, updatedBy: actor, updatedDate: timestamp.slice(0, 10) } : r)));
    this.addAudit(id, 'Organisation Updated', actor, { newValue: Object.keys(patch).join(', ') });
    this.toast.show('Organisation Updated', `${patch.name ?? record.name} has been updated.`, 'success');
    return true;
  }

  /** Section 3 administrative actions — Activate / Suspend / Deactivate. Only valid transitions per Section 11 are allowed. */
  changeAdminStatus(id: string, newStatus: Extract<OrganisationStatus, 'Active' | 'Suspended' | 'Deactivated'>, actor: string, reason?: string): boolean {
    const record = this.getById(id);
    if (!record) return false;
    if (!this.canTransition(record.status, newStatus)) {
      this.toast.show('Invalid Action', `${record.name} cannot move from ${record.status} to ${newStatus}.`, 'error');
      return false;
    }
    const timestamp = new Date().toISOString();
    const previousStatus = record.status;
    this.records.update((list) =>
      list.map((r) => (r.id === id ? { ...r, status: newStatus, statusClass: ORG_STATUS_BADGE_CLASS[newStatus], updatedBy: actor, updatedDate: timestamp.slice(0, 10) } : r)),
    );
    this.addAudit(id, `Organisation ${newStatus}`, actor, { oldValue: previousStatus, newValue: newStatus, reason });
    this.toast.show('Status Updated', `${record.name} is now ${newStatus}.`, 'success');
    return true;
  }

  /** Section 7/8 — required info + required documents must be complete before approval is allowed. */
  canApprove(org: OrganisationRecord): { ok: boolean; reason?: string } {
    // Address (Address Line 1/2, Country, State, City, PIN) is optional — not part of the completeness gate.
    const requiredFieldsPresent = !!(
      org.name.trim() &&
      org.email.trim() &&
      org.phone.trim() &&
      org.ownerName.trim() &&
      org.ownerEmail.trim() &&
      org.ownerMobile.trim()
    );
    if (!requiredFieldsPresent) {
      return { ok: false, reason: 'Organisation cannot be approved because required information or applicable documents are incomplete.' };
    }
    const requiredDocs = org.documents.filter((d) => d.required);
    const allUploaded = requiredDocs.every((d) => d.status === 'Uploaded' || d.status === 'Under Review' || d.status === 'Accepted');
    if (!allUploaded) {
      return { ok: false, reason: 'Organisation cannot be approved because required information or applicable documents are incomplete.' };
    }
    return { ok: true };
  }

  /** Section 8 — Approve. Sets Verification=Verified and Status=Active together, atomically, guarded by the current status so a duplicate/second Approve click is a no-op. */
  approve(id: string, actor: string): { ok: boolean; message?: string } {
    const record = this.getById(id);
    if (!record) return { ok: false, message: 'Organisation not found.' };
    if (record.status !== 'Pending Verification') {
      return { ok: false, message: 'This organisation has already been processed. Refreshing the record.' };
    }
    const check = this.canApprove(record);
    if (!check.ok) {
      return { ok: false, message: check.reason };
    }
    const timestamp = new Date().toISOString();
    this.records.update((list) =>
      list.map((r) =>
        r.id === id
          ? { ...r, status: 'Active', statusClass: ORG_STATUS_BADGE_CLASS.Active, verifiedBy: actor, verifiedDate: timestamp.slice(0, 10), updatedBy: actor, updatedDate: timestamp.slice(0, 10) }
          : r,
      ),
    );
    this.addAudit(id, 'Organisation Approved', actor, { oldValue: 'Pending Verification', newValue: 'Verification: Verified · Status: Active' });
    this.addAudit(id, 'Organisation Activated', actor, { newValue: 'Active' });
    this.toast.show('Organisation Approved', `${record.name} is now Verified and Active.`, 'success');
    return { ok: true };
  }

  /** Section 9 — Request Changes. Requires a non-empty reason and only applies from Pending Verification. */
  requestChanges(id: string, actor: string, reason: string): { ok: boolean; message?: string } {
    const record = this.getById(id);
    if (!record) return { ok: false, message: 'Organisation not found.' };
    if (!reason.trim()) return { ok: false, message: 'A change reason is required.' };
    if (record.status !== 'Pending Verification') {
      return { ok: false, message: 'This organisation is no longer awaiting verification. Refreshing the record.' };
    }
    const timestamp = new Date().toISOString();
    this.records.update((list) =>
      list.map((r) =>
        r.id === id
          ? { ...r, status: 'Changes Requested', statusClass: ORG_STATUS_BADGE_CLASS['Changes Requested'], changeRequestReason: reason.trim(), requestedBy: actor, requestedDate: timestamp.slice(0, 10), updatedBy: actor, updatedDate: timestamp.slice(0, 10) }
          : r,
      ),
    );
    this.addAudit(id, 'Changes Requested', actor, { oldValue: 'Pending Verification', newValue: 'Changes Requested', reason: reason.trim() });
    this.toast.show('Changes Requested', `${record.name} has been sent back for corrections.`, 'success');
    return { ok: true };
  }

  // ================= Organisation Owner actions =================

  /** Section 4 — Owner uploads or replaces a document. Only valid while the record is editable (Draft / Changes Requested); previous status becomes the "was this a replace" signal used for the audit label. */
  uploadDocument(organisationId: string, documentId: string, fileName: string, actor: string): { ok: boolean; message?: string } {
    const record = this.getById(organisationId);
    if (!record) return { ok: false, message: 'Organisation not found.' };
    const doc = record.documents.find((d) => d.id === documentId);
    if (!doc) return { ok: false, message: 'Document not found.' };
    if (doc.status === 'Not Applicable') return { ok: false, message: 'This document is not applicable for this organisation.' };
    if (doc.status === 'Accepted') return { ok: false, message: 'This document has already been accepted and cannot be replaced.' };

    const isReplace = doc.status === 'Rejected' || doc.status === 'Uploaded' || doc.status === 'Under Review';
    const timestamp = new Date().toISOString();
    this.records.update((list) =>
      list.map((r) =>
        r.id === organisationId
          ? {
              ...r,
              updatedBy: actor,
              updatedDate: timestamp.slice(0, 10),
              documents: r.documents.map((d) =>
                d.id === documentId
                  ? { ...d, status: 'Uploaded', fileName, uploadedDate: timestamp.slice(0, 10), uploadedBy: actor, version: (d.version ?? 0) + 1 }
                  : d,
              ),
            }
          : r,
      ),
    );
    this.addAudit(organisationId, isReplace ? 'Document Replaced' : 'Document Uploaded', actor, { newValue: `${doc.name}: ${fileName}` });
    this.toast.show(isReplace ? 'Document Replaced' : 'Document Uploaded', `${doc.name} has been ${isReplace ? 'replaced' : 'uploaded'}.`, 'success');
    return { ok: true };
  }

  /** Section 5/9 — Owner's Submit / Resubmit. Only valid from Draft or Changes Requested; requires the same completeness bar as Super Admin's approval check. Moves through Submitted straight to Pending Verification so Super Admin's Verify screen (gated on Pending Verification) sees it immediately. */
  submitForVerification(organisationId: string, actor: string): { ok: boolean; message?: string } {
    const record = this.getById(organisationId);
    if (!record) return { ok: false, message: 'Organisation not found.' };
    if (record.status !== 'Draft' && record.status !== 'Changes Requested') {
      return { ok: false, message: 'This organisation has already been submitted. Refreshing the record.' };
    }
    const check = this.canApprove(record);
    if (!check.ok) {
      return { ok: false, message: check.reason };
    }
    const wasResubmission = record.status === 'Changes Requested';
    const timestamp = new Date().toISOString();
    this.records.update((list) =>
      list.map((r) =>
        r.id === organisationId
          ? { ...r, status: 'Submitted', statusClass: ORG_STATUS_BADGE_CLASS.Submitted, updatedBy: actor, updatedDate: timestamp.slice(0, 10) }
          : r,
      ),
    );
    this.addAudit(organisationId, wasResubmission ? 'Organisation Resubmitted' : 'Submission Completed', actor, { oldValue: record.status, newValue: 'Submitted' });

    // Section 13 — Submitted cascades straight into Pending Verification for Super Admin review.
    this.records.update((list) =>
      list.map((r) =>
        r.id === organisationId
          ? { ...r, status: 'Pending Verification', statusClass: ORG_STATUS_BADGE_CLASS['Pending Verification'] }
          : r,
      ),
    );
    this.addAudit(organisationId, 'Submitted for Verification', actor, { newValue: 'Pending Verification' });
    this.toast.show('Submitted', `${record.name} has been sent to Super Admin for verification.`, 'success');
    return { ok: true };
  }
}
