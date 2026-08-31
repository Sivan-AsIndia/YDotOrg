// ================= Procurement shared models (YDot PRC) =================
// Design language — "Indigo" theme tokens shared with Inventory screens.
// Colour never carries meaning alone: every status pairs a label with an icon,
// so tone values always travel with human-readable text from the JSON models.

import { StatusTone } from './inventory.model';

/** UI states used across procurement components (mirrors INV 4.1.4). */
export type ProcurementUiState =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

// ================= SCR-PRC-001 — Purchase requisition register =================
/** Lifecycle of a requisition before it becomes a purchase order. */
export type RequisitionStatus =
  | 'Draft'
  | 'Pending Approval'
  | 'Approved'
  | 'Rejected'
  | 'Converted to PO';

export interface RequisitionRow {
  readonly requisitionReference: string;
  readonly title: string;
  readonly requestedBy: string;
  readonly department: string;
  readonly costCentre: string;
  readonly requiredByDate: string;
  readonly priority: 'Low' | 'Medium' | 'High' | 'Critical';
  readonly status: RequisitionStatus;
  readonly itemCount: number;
  readonly estimatedValue: number;
  readonly currency: string;
  readonly suggestedVendor: string;
  readonly approvalStage: string;
  readonly raisedOn: string;
  readonly lastAction: string;
  readonly statusTone: StatusTone;
}

export interface RequisitionPermissions {
  readonly view: boolean;
  readonly raise: boolean;
  readonly approveOrReject: boolean;
  readonly convertToPo: boolean;
  readonly export: boolean;
}

// ================= SCR-PRC-002 — Purchase order workbench =================
export type PurchaseOrderStatus =
  | 'Draft'
  | 'In Approval'
  | 'Issued'
  | 'Acknowledged'
  | 'Partially Received'
  | 'Closed'
  | 'Cancelled';

export interface PurchaseOrderRow {
  readonly orderReference: string;
  readonly vendorName: string;
  readonly vendorCode: string;
  readonly buyer: string;
  readonly orderDate: string;
  readonly expectedDelivery: string;
  readonly currency: string;
  readonly orderValue: number;
  readonly itemCount: number;
  readonly paymentTerms: string;
  readonly deliveryWarehouse: string;
  readonly status: PurchaseOrderStatus;
  readonly dispatchState: string;
  readonly acknowledgement: string;
  readonly lastEvent: string;
  readonly isOverdue: boolean;
  readonly statusTone: StatusTone;
}

export interface PurchaseOrderPermissions {
  readonly view: boolean;
  readonly raise: boolean;
  readonly approveAndIssue: boolean;
  readonly cancelDraft: boolean;
  readonly export: boolean;
}

// ================= SCR-PRC-003 — Vendor directory =================
export type VendorStatus = 'Active' | 'On Hold' | 'Blacklisted';

export interface VendorRow {
  readonly vendorCode: string;
  readonly legalName: string;
  readonly category: string;
  readonly city: string;
  readonly country: string;
  readonly taxId: string;
  readonly complianceState: string;
  readonly complianceTone: StatusTone;
  readonly rating: number;
  readonly onTimeRate: number;
  readonly qualityRate: number;
  readonly activeOrders: number;
  readonly spendYtd: number;
  readonly currency: string;
  readonly empanelledOn: string;
  readonly status: VendorStatus;
  readonly statusTone: StatusTone;
}

export interface VendorPermissions {
  readonly view: boolean;
  readonly addVendor: boolean;
  readonly requestComplianceReview: boolean;
  readonly holdOrRelease: boolean;
  readonly export: boolean;
}

// ================= SCR-PRC-004 — Goods receipt & invoice match =================
/** Three-way match outcome between PO, goods receipt and invoice. */
export type MatchState =
  | 'Matched'
  | 'Quantity Variance'
  | 'Price Variance'
  | 'Pending Match';

export interface GoodsReceiptRow {
  readonly receiptReference: string;
  readonly poReference: string;
  readonly vendorName: string;
  readonly receivedOn: string;
  readonly warehouse: string;
  readonly itemAndBatch: string;
  readonly orderedQuantity: number;
  readonly receivedQuantity: number;
  readonly acceptedQuantity: number;
  readonly rejectedQuantity: number;
  readonly invoiceReference: string;
  readonly invoiceAmount: number;
  readonly poAmount: number;
  readonly currency: string;
  readonly matchState: MatchState;
  readonly disposition: string;
  readonly recordedBy: string;
  readonly statusTone: StatusTone;
}

export interface GoodsReceiptPermissions {
  readonly view: boolean;
  readonly recordReceipt: boolean;
  readonly runThreeWayMatch: boolean;
  readonly raiseVarianceCase: boolean;
  readonly approveForPayment: boolean;
}

// ================= SCR-PRC-005 — Item catalogue =================
/** Lifecycle of a purchasable item offered to requesters. */
export type CatalogueStatus = 'Active' | 'Draft' | 'Inactive';

export interface CatalogueItem {
  readonly skuCode: string;
  readonly itemName: string;
  readonly category: string;
  readonly uom: string;
  readonly specification: string;
  readonly estimatedCost: number;
  readonly currency: string;
  readonly status: CatalogueStatus;
  readonly addedOn: string;
  readonly statusTone: StatusTone;
}

// ================= SCR-PRC-006 — Request for quotation =================
/** An RFQ asks one approved supplier for a price against one approved PR (email-based; no portal). */
export type RfqStatus = 'Draft' | 'Sent' | 'Closed';

export interface RfqRow {
  readonly rfqReference: string;
  readonly sourcePrRef: string;
  readonly supplierName: string;
  readonly supplierCode: string;
  readonly itemSummary: string;
  readonly quantity: number;
  readonly currency: string;
  readonly dueDate: string;
  readonly terms: string;
  readonly status: RfqStatus;
  readonly sentOn: string;
  readonly statusTone: StatusTone;
}

// ================= SCR-PRC-007 — Quotation =================
/** Supplier reply entered manually by the procurement user from the supplier's email. */
export type QuotationStatus = 'Received' | 'Recommended';

export interface QuotationRow {
  readonly quoteReference: string;
  readonly rfqReference: string;
  readonly sourcePrRef: string;
  readonly supplierName: string;
  readonly supplierCode: string;
  readonly price: number;
  readonly taxPercent: number;
  readonly deliveryDays: number;
  readonly validTill: string;
  readonly paymentTerms: string;
  readonly currency: string;
  readonly receivedOn: string;
  readonly status: QuotationStatus;
  readonly recommendationReason?: string;
  readonly statusTone: StatusTone;
}

// ================= Guided helpers shared with inventory screens =================
export interface ProcurementFlowStep {
  readonly stepNumber: number;
  readonly title: string;
  readonly content: string;
  readonly continueGate: string;
}
