// ================= SCR-FIN-001 — Finance workbench permission matrix =================
// Sourced from DOC-07 §4.1.3 (Actions, eligibility and result) and DOC-00 Table 7
// (role catalogue — R06/R07/R01/R15 rows only), Table 8 (Money menu group) and
// §4/Table 10 (universal permission model / action-code semantics).
// Page-scoped: this file governs SCR-FIN-001 only, not any other Finance screen.

import { FinanceWorkbenchPermissions } from '../../../../Shared/models/finance.model';

/** Roles in scope for this screen (DOC-00 Table 7, restricted to the 4 named rows). */
export type FinanceWorkbenchRoleCode = 'R06' | 'R07' | 'R01' | 'R15';

export interface FinanceWorkbenchRoleEntry {
  readonly code: FinanceWorkbenchRoleCode;
  /** DOC-00 Table 7 "Professional role profile". */
  readonly label: string;
}

export const FINANCE_WORKBENCH_ROLES: readonly FinanceWorkbenchRoleEntry[] = [
  { code: 'R06', label: 'Finance Maker' },
  { code: 'R07', label: 'Finance Checker / Approver' },
  { code: 'R01', label: 'Executive Sponsor' },
  { code: 'R15', label: 'Auditor / Compliance Reviewer' },
];

/** DOC-00 §4 / Table 10 action-code semantics used on this screen only (V, O, A). */
export type FinanceWorkbenchActionCode = 'V' | 'O' | 'A';

export interface FinanceWorkbenchPermissionEntry {
  readonly permission: string;
  readonly actionCode: FinanceWorkbenchActionCode;
  readonly eligibleRoles: readonly FinanceWorkbenchRoleCode[];
  /** DOC-07 §4.1.3 "Allowed state" column, verbatim. */
  readonly allowedState: string;
}

/**
 * Filter = V (View — re-run/change the authorised query; DOC-00 Table 8 grants R01/R15
 * "authorised views" of the Money group, so view/filter extends to all four roles).
 * Match = O (Operate). Verify = A (Approve/verify — independent decision, restricted to
 * R07 per DOC-00 Table 7 "Independently verifies and decides"; R06's role purpose is
 * "Prepares and submits; cannot approve own controlled work"). Escalate = O (Operate).
 */
export const FINANCE_WORKBENCH_PERMISSION_MATRIX: readonly FinanceWorkbenchPermissionEntry[] = [
  {
    permission: 'fin.finance-workbench.view',
    actionCode: 'V',
    eligibleRoles: ['R06', 'R07', 'R01', 'R15'],
    allowedState: 'Any authorised state',
  },
  {
    permission: 'fin.finance-workbench.match',
    actionCode: 'O',
    eligibleRoles: ['R06', 'R07'],
    allowedState: 'Permitted lifecycle state',
  },
  {
    permission: 'fin.finance-workbench.verify',
    actionCode: 'A',
    eligibleRoles: ['R07'],
    allowedState: 'Submitted / Pending review',
  },
  {
    permission: 'fin.finance-workbench.escalate',
    actionCode: 'O',
    eligibleRoles: ['R06', 'R07'],
    allowedState: 'Permitted lifecycle state',
  },
];

export function hasFinanceWorkbenchPermission(role: FinanceWorkbenchRoleCode, permission: string): boolean {
  const entry = FINANCE_WORKBENCH_PERMISSION_MATRIX.find((p) => p.permission === permission);
  return !!entry && entry.eligibleRoles.includes(role);
}

/** Role → effective permission decision. The server re-evaluates the same decision (DOC-00 §4.6). */
export function computeFinanceWorkbenchPermissions(role: FinanceWorkbenchRoleCode): FinanceWorkbenchPermissions {
  return {
    view: hasFinanceWorkbenchPermission(role, 'fin.finance-workbench.view'),
    match: hasFinanceWorkbenchPermission(role, 'fin.finance-workbench.match'),
    verify: hasFinanceWorkbenchPermission(role, 'fin.finance-workbench.verify'),
    escalate: hasFinanceWorkbenchPermission(role, 'fin.finance-workbench.escalate'),
  };
}
