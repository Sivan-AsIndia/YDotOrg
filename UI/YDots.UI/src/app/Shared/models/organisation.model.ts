// ================= Status =================

/** Single authoritative status field — Section 11's lifecycle. "Verification Status" shown in the UI is a derived label, not a second stored field (avoids the two ever contradicting each other). */
export type OrganisationStatus =
  | 'Draft'
  | 'Submitted'
  | 'Pending Verification'
  | 'Changes Requested'
  | 'Verified'
  | 'Active'
  | 'Suspended'
  | 'Deactivated';

export const ORG_STATUS_TRANSITIONS: Record<OrganisationStatus, OrganisationStatus[]> = {
  Draft: ['Submitted'],
  Submitted: ['Pending Verification'],
  'Pending Verification': ['Verified', 'Changes Requested'],
  'Changes Requested': ['Submitted'],
  Verified: ['Active'],
  Active: ['Suspended', 'Deactivated'],
  Suspended: ['Active'],
  Deactivated: [],
};

export const ORG_STATUS_BADGE_CLASS: Record<OrganisationStatus, string> = {
  Draft: 'org-badge-muted',
  Submitted: 'org-badge-blue',
  'Pending Verification': 'org-badge-warn',
  'Changes Requested': 'org-badge-error',
  Verified: 'org-badge-meadow',
  Active: 'org-badge-good',
  Suspended: 'org-badge-warn',
  Deactivated: 'org-badge-muted',
};

/** Section 6/7's "Verification Status" column/field, derived from the single status so it can never drift out of sync. */
export function verificationStatusLabel(status: OrganisationStatus): 'Not Submitted' | 'Pending Verification' | 'Changes Requested' | 'Verified' {
  switch (status) {
    case 'Draft':
      return 'Not Submitted';
    case 'Submitted':
    case 'Pending Verification':
      return 'Pending Verification';
    case 'Changes Requested':
      return 'Changes Requested';
    default:
      // Verified, Active, Suspended, Deactivated all passed verification once.
      return 'Verified';
  }
}

/** Fixed semantic tone for the derived verification label — kept independent of the app's chosen accent/theme colour, since good/warn/error must read the same regardless of branding. */
export function verificationBadgeClass(status: OrganisationStatus): string {
  switch (verificationStatusLabel(status)) {
    case 'Not Submitted':
      return 'org-badge-muted';
    case 'Pending Verification':
      return 'org-badge-warn';
    case 'Changes Requested':
      return 'org-badge-error';
    case 'Verified':
      return 'org-badge-good';
  }
}

// ================= Reference catalogues =================
export const ORGANISATION_TYPES = [
  'Non-Profit / NGO',
  'Charitable Organisation',
  'Foundation',
  'Community Organisation',
  'Educational Organisation',
  'Healthcare Organisation',
  'Religious / Faith-Based Organisation',
  'Social Welfare Organisation',
  'International / Foreign Organisation',
  'Other',
] as const;
export type OrganisationType = (typeof ORGANISATION_TYPES)[number];

export const LEGAL_STRUCTURES = ['Trust', 'Society', 'Section 8 Company', 'Non-Profit Company', 'Other'] as const;
export type LegalStructure = (typeof LEGAL_STRUCTURES)[number];

export type OwnerAccountStatus = 'Pending Setup' | 'Invited' | 'Active';

// ================= Compliance =================
export interface ComplianceItem {
  applicable: boolean;
  number: string;
}

export interface OrganisationCompliance {
  twelveA: ComplianceItem;
  eightyG: ComplianceItem;
  fcra: ComplianceItem;
  gst: ComplianceItem;
}

// ================= Documents (Section 7 checklist) =================
export type DocumentStatus = 'Pending' | 'Uploaded' | 'Under Review' | 'Accepted' | 'Rejected' | 'Not Applicable';

export interface OrganisationDocument {
  id: string;
  name: string;
  required: boolean;
  status: DocumentStatus;
  uploadedDate?: string;
  uploadedBy?: string;
  version?: number;
  fileName?: string;
}

/** The fixed checklist from Section 7 — required/applicable documents depend on the organisation's compliance flags, not a blanket "everything mandatory" rule. */
export function buildDocumentChecklist(compliance: OrganisationCompliance): OrganisationDocument[] {
  const mk = (id: string, name: string, required: boolean, status: DocumentStatus = 'Pending'): OrganisationDocument => ({ id, name, required, status });
  return [
    mk('DOC-REG', 'Registration Certificate', true),
    mk('DOC-PAN', 'PAN / Tax Document', true),
    mk('DOC-DEED', 'Trust Deed / MOA / AOA / By-laws', true),
    mk('DOC-12A', '12A / 12AB Certificate', compliance.twelveA.applicable, compliance.twelveA.applicable ? 'Pending' : 'Not Applicable'),
    mk('DOC-80G', '80G Certificate', compliance.eightyG.applicable, compliance.eightyG.applicable ? 'Pending' : 'Not Applicable'),
    mk('DOC-FCRA', 'FCRA Certificate', compliance.fcra.applicable, compliance.fcra.applicable ? 'Pending' : 'Not Applicable'),
    mk('DOC-ANNUAL', 'Annual Report', false),
    mk('DOC-BANK', 'Bank Details Proof', true),
    mk('DOC-GST', 'GST Certificate', compliance.gst.applicable, compliance.gst.applicable ? 'Pending' : 'Not Applicable'),
    mk('DOC-OTHER', 'Other Supporting Documents', false),
  ];
}

// ================= Organisation Master Record (Section 12) =================
export interface OrganisationRecord {
  id: string; // ORG-000001

  name: string;
  organisationType: OrganisationType;
  legalStructure: LegalStructure;
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

  ownerId: string;
  ownerName: string;
  ownerEmail: string;
  ownerMobile: string;
  ownerDesignation: string;
  ownerAccountStatus: OwnerAccountStatus;

  status: OrganisationStatus;
  statusClass: string;

  documents: OrganisationDocument[];

  changeRequestReason?: string;
  requestedBy?: string;
  requestedDate?: string;

  createdBy: string;
  createdDate: string;
  updatedBy?: string;
  updatedDate: string;
  verifiedBy?: string;
  verifiedDate?: string;
}

// ================= Audit (Section 14) =================
export interface OrganisationAuditEntry {
  id: string;
  organisationId: string;
  action: string;
  oldValue?: string;
  newValue?: string;
  performedBy: string;
  performedRole: 'Super Admin';
  timestamp: string;
  reason?: string;
}
