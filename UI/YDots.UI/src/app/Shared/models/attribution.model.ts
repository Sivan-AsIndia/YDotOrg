
export interface AttributionPermissions {
  readonly view: boolean;
  readonly requestCorrection: boolean;
  readonly deleteDraft: boolean;
}

/** Related record row. */
export interface AttributionRelatedRow {
  readonly primary: string;
  readonly secondary: string;
  readonly meta: string;
}

/** Related tab definition. */
export interface AttributionRelatedTab {
  readonly key: string;
  readonly label: string;
  readonly rows?: readonly AttributionRelatedRow[];
}

/** Trace field copy candidate. */
export interface TraceField {
  readonly key: string;
  readonly label: string;
  readonly value: string;
  readonly copyable?: boolean;
}

/** Trace group definition. */
export interface TraceGroup {
  readonly key: string;
  readonly step: string;
  readonly title: string;
  readonly caption: string;
  readonly fields: readonly TraceField[];
}

/** Donation record in scope (4.5.1). */
export interface DonationRecord {
  readonly reference: string;
  readonly campaign: string;
  readonly trackingAsset: string;
  readonly source: string;
  readonly medium: string;
  readonly leadOrDonor: string;
  readonly intentCreated: string;
  readonly paymentCaptured: string;
  readonly settlementRecon: string;
  /** Captured time the settled payment reconciled to the ledger — shown as its own trace step,
   *  separate from settlement (settlementRecon holds the settlement captured time). */
  readonly reconciliationCaptured: string;
  readonly reconciliation: string;
  readonly attributionSnapshot: string;
  readonly correctionRequest: string;
  readonly auditChain: string;
  readonly lifecycle: string;
  readonly owner: string;
  readonly downstreamStatus: string;
  readonly amount: string;
  readonly restricted: boolean;
  readonly hasOpenCorrection: boolean;
  readonly isDraft: boolean;
  readonly hasDownstreamReference: boolean;
  readonly staleAfterOpen: boolean;
  readonly dependencyWillFail: boolean;
}