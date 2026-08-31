/**
 * Receipt correction and reissue — PAY-UI-08 model types.
 *
 * Faithful implementation of §4.8 of the YDot PAY Practical UI/UX Generation
 * Specification (v1.2, Dark Meadow).
 */

/** Correction category — approved catalogue (§4.8.2). */
export type RcrCorrectionCategory =
  | 'Amount Correction'
  | 'Donor Name Correction'
  | 'Receipt Date Correction'
  | 'Reissue (Duplicate)'
  | 'Reissue (Lost Receipt)';

/** Correction status — lifecycle catalogue (§4.8.1 / §5.5). */
export type RcrCorrectionStatus = 'Draft' | 'Pending Approval' | 'Approved' | 'Completed' | 'Rejected';

/** UI state — controlled catalogue (§4.8.4). */
export type RcrUiState = 'ready' | 'loading' | 'success' | 'no-access' | 'empty' | 'duplicate' | 'conflict' | 'dependency-failure';

/** History entry — audit chronology row (§4.8.1 Related and history). */
export interface RcrHistoryEntry {
  label: string;
  detail: string;
  meta: string;
}

/** Linked record — related record reference (§4.8.1). */
export interface RcrLinkedRecord {
  reference: string;
  kind: string;
}

/** Document — supporting evidence (§4.8.1). */
export interface RcrDocumentItem {
  name: string;
  classification: string;
}

/** Supporting evidence — uploaded file with scan/link status (§4.8.2). */
export interface RcrEvidenceItem {
  name: string;
  classification: string;
  status: string;
}

/** Integration status — provider state (§4.8.1). */
export interface RcrIntegrationStatus {
  provider: string;
  state: string;
}

/** Support correlation — open case reference (§4.8.1). */
export interface RcrSupportCorrelation {
  reference: string;
  state: string;
}

/** Correction request — a receipt-correction record (§4.8.2). */
export interface RcrCorrectionRequest {
  /**
   * The receipt's identifier, which is what every write to the API is addressed to.
   *
   * IT IS NOT RENDERED. `requestReference` is what an operator reads and quotes; this is the
   * value the route takes. Keeping both means the screen never has to put a GUID in front of
   * somebody in order to be able to call the server.
   */
  receiptId: string;
  requestReference: string;
  correctionCategory: RcrCorrectionCategory;
  receiptReference: string;
  newReceiptReference: string;
  donationReference: string;
  donorName: string;
  currentValue: string;
  proposedValue: string;
  currentVersion: number;
  status: RcrCorrectionStatus;
  requestedAtIso: string;
  requestedAtLabel: string;
  requestedBy: string;
  reason: string;
  supportingEvidence: RcrEvidenceItem[];
  approver: string;
  deliveryChannel: string;
  version: number;
  hasDownstreamReference: boolean;
  downstreamStatus: string;
  history: RcrHistoryEntry[];
  linkedRecords: RcrLinkedRecord[];
  documents: RcrDocumentItem[];
  integrationStatus: RcrIntegrationStatus;
  supportCorrelation: RcrSupportCorrelation;
}

/** Correction permissions — effective permissions (§4.8.3). */
export interface RcrCorrectionPermissions {
  view: boolean;
  requestCorrection: boolean;
  reviewDifference: boolean;
  approveReissue: boolean;
  deliverCorrectedReceipt: boolean;
  rejectRequest: boolean;
}

/** Persistent outcome — confirmed result shown persistently (§4.8.1). */
export interface RcrPersistentOutcome {
  reference: string;
  state: string;
  effectiveTime: string;
  downstreamStatus: string;
  owner: string;
  nextAction: string;
}