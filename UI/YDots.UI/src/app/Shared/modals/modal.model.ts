/**
 * Shared modal data models for YDot.
 * All modal-related JSON/variable definitions go here.
 */

/** Configuration for a confirmation dialog. */
export interface ConfirmDialogConfig {
  readonly title: string;
  readonly message: string;
  readonly confirmLabel: string;
  readonly cancelLabel: string;
  readonly confirmTone: 'primary' | 'danger' | 'warning';
  readonly showReason?: boolean;
  readonly reasonLabel?: string;
  readonly reasonMin?: number;
  readonly reasonMax?: number;
  readonly requireReason?: boolean;
}

/** State tracking for a modal dialog. */
export interface ModalState {
  readonly open: boolean;
  readonly data?: unknown;
}

/** Export confirmation data. */
export interface ExportConfirmData {
  readonly classification: string;
  readonly purpose: string;
  readonly scope: string;
  readonly rowFileCount: string;
  readonly expiry: string;
  readonly auditReference: string;
}

/** Delete confirmation data. */
export interface DeleteConfirmData {
  readonly targetName: string;
  readonly targetReference: string;
  readonly consequence: string;
  readonly resultingState: string;
}

/** Create dialog data. */
export interface CreateDialogData {
  readonly name: string;
  readonly reference: string;
}

/** Compare dialog data. */
export interface CompareDialogData<T> {
  readonly rows: readonly T[];
  readonly columns: readonly { key: string; label: string }[];
}