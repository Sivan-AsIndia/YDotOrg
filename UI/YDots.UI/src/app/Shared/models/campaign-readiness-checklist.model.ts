// ============================================================================
// Add Readiness Check — configuration model
// ============================================================================
//
// This is the *configuration* shape authored in the "Add readiness check"
// off-canvas (CAM-UI-07). It captures a single check — NOT a computed result.
// Individual check status remains system-derived; every check is created as
// `Pending` and there is no manual status control anywhere.
//

/** The six readiness-check categories, aligned to the page's launch dependencies. */
export type ReadinessCheckCategory =
  | 'Content'
  | 'Budget'
  | 'Tracking'
  | 'Payment'
  | 'Template'
  | 'Consent';

/** How a check is evaluated at run time (informational configuration only). */
export type ReadinessCheckType = 'Manual' | 'Automatic' | 'Derived' | 'External' | 'Hybrid';

/** The rule used to validate a check. */
export type ReadinessValidationMethod =
  | 'Manual confirmation'
  | 'Record exists'
  | 'Approval completed'
  | 'Count reached'
  | 'Integration verified'
  | 'Document uploaded'
  | 'Status matches'
  | 'Date condition'
  | 'External system validation'
  | 'Custom rule';

/** Where a check's evidence/state originates. */
export type ReadinessSourceType =
  | 'Internal'
  | 'External'
  | 'Derived'
  | 'Manual'
  | 'Stub'
  | 'Integration';

/** Supported evidence kinds — only relevant when a check requires evidence. */
export type ReadinessEvidenceType = 'Document' | 'URL' | 'Screenshot' | 'Record';

/**
 * A check's status is system-derived. On creation it is always `Pending`;
 * `Passed`/`Failed` are computed elsewhere and are never authored in the drawer.
 */
export type ReadinessCheckStatus = 'Pending' | 'Passed' | 'Failed';

/** A single configured readiness check. */
export interface ReadinessCheck {
  /** Stable client id for list operations (edit / delete / reorder). */
  id: string;
  name: string;
  description?: string;
  category: ReadinessCheckCategory;
  checkType: ReadinessCheckType;
  validationMethod: ReadinessValidationMethod;
  successCriteria: string;
  requiredForLaunch: boolean;
  ownerId?: string;
  dueDate?: string;
  sourceType: ReadinessSourceType;
  sourceReference?: string;
  evidenceRequired: boolean;
  evidenceType?: ReadinessEvidenceType;
  evidenceReference?: string;
  notes?: string;
  /** Always `Pending` on create; derived thereafter. */
  status: ReadinessCheckStatus;
}
