// ================= Inventory shared models (Section 10 — YDot INV) =================
// Dark Meadow v1.2 — deep olive, calm blue, warm ivory, restrained antique gold.

/** UI states used across inventory components (4.1.4 / 7.x.4). */
export type InventoryUiState =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

/** Dark Meadow semantic status tones — colour never carries meaning alone (1.2). */
export type StatusTone = 'info' | 'success' | 'warning' | 'danger' | 'muted' | 'gold';

/** A scope-aware selector option with stable reference and disambiguating context. */
export interface ScopeAwareOption {
  readonly reference: string;
  readonly name: string;
  readonly context: string;
  readonly initials: string;
  readonly tone: string;
}

/** Read-only status badge with text + icon (colour alone never carries meaning). */
export interface StatusBadge {
  readonly label: string;
  readonly tone: StatusTone;
  readonly icon: string;
}

/** Related / history row. */
export interface InventoryHistoryRow {
  readonly primary: string;
  readonly secondary: string;
  readonly meta: string;
}

/** Related tab. */
export interface InventoryRelatedTab {
  readonly key: string;
  readonly label: string;
  readonly rows: readonly InventoryHistoryRow[];
}

/** Persistent outcome (4.1.1 Persistent outcome). */
export interface InventoryPersistentOutcome {
  readonly reference: string;
  readonly state: string;
  readonly effectiveTime: string;
  readonly downstreamStatus: string;
  readonly owner: string;
  readonly nextAction: string;
}

// ================= SCR-INV-001 — Inventory overview =================
/** One inventory overview row (4.1.2 field contract — all read-only). */
export interface InventoryOverviewRow {
  readonly itemOrSku: string;
  readonly itemName: string;
  readonly warehouse: string;
  readonly location: string;
  readonly batch: string;
  readonly stockState: string;
  readonly onHandQuantity: number;
  readonly reservedQuantity: number;
  readonly availableQuantity: number;
  readonly quarantinedQuantity: number;
  readonly inTransitQuantity: number;
  readonly damagedQuantity: number;
  readonly lowStockThreshold: number;
  readonly lastMovementTime: string;
  readonly statusTone: StatusTone;
}

/** Inventory overview permissions (4.1.3). */
export interface InventoryOverviewPermissions {
  readonly view: boolean;
  readonly export: boolean;
}

// ================= SCR-INV-002 — Batch ledger =================
/** One batch ledger row (4.2.2 field contract — immutable, read-only). */
export interface BatchLedgerRow {
  readonly itemReference: string;
  readonly itemName: string;
  readonly batchReference: string;
  readonly specificationVersion: string;
  readonly warehouseAndBin: string;
  readonly qualityState: StatusBadge;
  readonly expiryDate: string;
  readonly onHandBalance: number;
  readonly reservedBalance: number;
  readonly availableBalance: number;
  readonly movementReference: string;
  readonly movementType: string;
  readonly businessReference: string;
  readonly quantity: number;
  readonly runningBalance: number;
  readonly actorAndTime: string;
}

/** Batch ledger permissions (4.2.3). */
export interface BatchLedgerPermissions {
  readonly view: boolean;
  readonly reservation: boolean;
  readonly transfer: boolean;
  readonly issue: boolean;
  readonly returnItem: boolean;
}

// ================= SCR-INV-003 — Stock movement form =================
export type MovementType =
  | 'Goods Receipt'
  | 'Goods Issue'
  | 'Transfer In'
  | 'Transfer Out'
  | 'Adjustment In'
  | 'Adjustment Out'
  | 'Return to Vendor'
  | 'Return from Recipient';

export interface StockMovementPermissions {
  readonly view: boolean;
  readonly validate: boolean;
  readonly confirm: boolean;
  readonly post: boolean;
}

// ================= SCR-INV-004 — Reservation manager =================
export type ReservationState = 'Draft' | 'Pending' | 'Reserved' | 'Released' | 'Expired' | 'Consumed';

export interface ReservationRecord {
  readonly reservationReference: string;
  readonly eventOrAllocation: string;
  readonly reservationState: ReservationState;
  readonly approvedAllocationOrEvent: string;
  readonly item: string;
  readonly itemName: string;
  readonly eligibleBatch: string;
  readonly warehouse: string;
  readonly quantity: number;
  readonly requiredByDate: string;
  readonly availableQuantity: number;
  readonly reservedQuantity: number;
  readonly expiryTime: string;
  readonly releaseReason: string;
  readonly consumptionReference: string;
  readonly statusHistory: StatusBadge;
}

export interface ReservationPermissions {
  readonly view: boolean;
  readonly reserve: boolean;
  readonly release: boolean;
  readonly expire: boolean;
  readonly consume: boolean;
}

// ================= SCR-INV-005 — Stock count session =================
export interface StockCountRecord {
  readonly countSessionReference: string;
  readonly warehouseAndZone: string;
  readonly countDate: string;
  readonly countTeam: string;
  readonly blindCountSetting: boolean;
  readonly expectedItems: number;
  readonly countedItemAndBatch: string;
  readonly countedQuantity: number;
  readonly systemQuantity: number;
  readonly variance: number;
  readonly recountQuantity: number;
  readonly varianceReason: string;
  readonly evidence: string;
  readonly adjustmentProposal: string;
  readonly approvalState: StatusBadge;
}

export interface StockCountPermissions {
  readonly view: boolean;
  readonly freezeControl: boolean;
  readonly count: boolean;
  readonly recount: boolean;
  readonly approveAdjustment: boolean;
}

// ================= SCR-INV-006 — Inventory exception queue =================
export type InventoryExceptionType =
  | 'Negative stock risk'
  | 'Quantity mismatch'
  | 'Overdue in-transit'
  | 'Damaged stock'
  | 'Expiry threshold'
  | 'Missing reserve';

export interface InventoryExceptionRecord {
  readonly exceptionReference: string;
  readonly exceptionType: InventoryExceptionType;
  readonly warehouse: string;
  readonly age: string;
  readonly severity: 'High' | 'Medium' | 'Low';
  readonly itemAndBatch: string;
  readonly detectedRisk: StatusBadge;
  readonly expectedValue: number;
  readonly observedValue: number;
  readonly affectedBusinessRecord: string;
  readonly owner: string;
  readonly investigation: string;
  readonly resolutionAction: string;
  readonly evidence: string;
  readonly status: StatusBadge;
  readonly escalationState: StatusBadge;
}

export interface InventoryExceptionPermissions {
  readonly view: boolean;
  readonly assign: boolean;
  readonly evidence: boolean;
  readonly resolve: boolean;
  readonly escalate: boolean;
}

// ================= INV-UI-07 — Warehouse transfer =================
export interface WarehouseTransferRecord {
  readonly transferReference: string;
  readonly fromWarehouse: string;
  readonly fromBin: string;
  readonly toWarehouse: string;
  readonly toBin: string;
  readonly item: string;
  readonly itemName: string;
  readonly batch: string;
  readonly quantity: number;
  readonly availableQuantity: number;
  readonly custodian: string;
  readonly dispatchTime: string;
  readonly receiptTime: string;
  readonly evidence: string;
  readonly variance: number;
  readonly status: StatusBadge;
}

export interface WarehouseTransferPermissions {
  readonly view: boolean;
  readonly validateAvailability: boolean;
  readonly dispatch: boolean;
  readonly receive: boolean;
  readonly recordVariance: boolean;
  readonly cancelDraft: boolean;
}

// ================= INV-UI-08 — Stock adjustment approval =================
export interface StockAdjustmentRecord {
  readonly adjustmentReference: string;
  readonly itemAndBatch: string;
  readonly itemName: string;
  readonly warehouse: string;
  readonly systemQuantity: number;
  readonly observedQuantity: number;
  readonly difference: number;
  readonly reasonCategory: string;
  readonly detailedReason: string;
  readonly evidence: string;
  readonly proposer: string;
  readonly independentApprover: string;
  readonly resultingBalance: StatusBadge;
}

export interface StockAdjustmentPermissions {
  readonly view: boolean;
  readonly submitAdjustment: boolean;
  readonly approve: boolean;
  readonly reject: boolean;
  readonly postCorrection: boolean;
  readonly requestRecount: boolean;
}

// ================= Guided flow steps (Section 5) =================
export interface GuidedFlowStep {
  readonly stepNumber: number;
  readonly title: string;
  readonly content: string;
  readonly continueGate: string;
}
