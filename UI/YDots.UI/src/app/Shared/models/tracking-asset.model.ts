// ================= Tracking Asset related shared models =================

/** Asset lifecycle status — current catalogue values only. */
export type AssetStatus = 'Draft' | 'Submitted' | 'Approved' | 'Active' | 'Inactive' | 'Paused' | 'Disabled';

/** Approval state — read-only, server-derived badge. */
export type ApprovalState = 'Not required' | 'Pending review' | 'Approved' | 'Rejected';

/** Last test result — read-only, server-derived badge. */
export type TestResult = 'Passed' | 'Failed' | 'Not tested';

/** Effective permission set for the acting Campaign / Fundraising Manager. */
export interface TrackingAssetPermissions {
  readonly view: boolean;
  readonly generate: boolean;
  readonly test: boolean;
  readonly approve: boolean;
  readonly disable: boolean;
  readonly replace: boolean;
}

/** A scope-aware campaign option with a stable reference and disambiguating context. */
export interface CampaignOption {
  readonly reference: string;
  readonly name: string;
  readonly context: string;
}

/** A single free-form named field attached to an on-ground place (place-level "customisable add fields"). */
export interface PlaceCustomField {
  readonly key: string;
  readonly value: string;
}

/** A single tracking asset row. */
export interface TrackingAsset {
  readonly trackingReference: string;
  readonly assetType: string;
  readonly channel: string;
  readonly destination: string;
  readonly campaignRef: string;
  readonly source: string;
  readonly medium: string;
  readonly contentTag: string;
  readonly activeFrom: string;
  readonly activeTo: string;
  readonly generatedUrl: string;
  readonly isQr: boolean;
  readonly lastTestResult: TestResult;
  readonly approvalState: ApprovalState;
  readonly usageCount: number;
  /** Total money received (in ₹) attributed to this asset — shown in the asset detail below usage count. */
  readonly amountReceived?: number;
  readonly assetStatus: AssetStatus;
  readonly hasDownstreamReference: boolean;
  /** For an on-ground event asset, the specific physical place this QR/link belongs to — lets a
   *  multi-location campaign track which place is driving the most contributions (4.4.2 extension). */
  readonly place?: string;
  /** Place-level City / State — fetched from the selected campaign (captured at campaign
   *  creation), not typed by hand — plus any further custom fields the creator attached to
   *  this physical place (4.4.2 extension). */
  readonly placeCity?: string;
  readonly placeState?: string;
  readonly placeCustomFields?: readonly PlaceCustomField[];
  /** Not a displayed field — supports the "cannot approve an asset you created" segregation-of-duty rule (4.4.3 Approve). */
  readonly createdByRef?: string;
  /** Not a displayed field — records the independent approver's identity for the Approve decision (4.4.3). */
  readonly approvedByRef?: string;
  /** Not a displayed field — records the Approve decision time (4.4.3). */
  readonly approvedAt?: string;
  /** Not a displayed field — increments on every store mutation; detects "record changed after you opened it" (4.4.4 Conflict). */
  readonly version?: number;
}

/** Detail tab for tracking asset detail panel. */
export interface TrackingDetailTab {
  readonly key: string;
  readonly label: string;
}

/** Saved view option. */
export interface SavedViewOption {
  readonly key: string;
  readonly label: string;
}

/** Filter chip for active filter summary. */
export interface FilterChip {
  readonly key: string;
  readonly label: string;
}

/** Related tab with rows for tracking asset detail panel. */
export interface TrackingRelatedTab {
  readonly key: string;
  readonly label: string;
  readonly rows: readonly { primary: string; secondary: string; meta: string }[];
}