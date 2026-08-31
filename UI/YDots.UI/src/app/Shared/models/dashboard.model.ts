export interface DashboardSummary {
  users: DashboardUsersSummary;
  accessRequests: DashboardAccessRequestsSummary;
  roles: DashboardRolesSummary;
  security: DashboardSecuritySummary;
}

export interface DashboardUsersSummary {
  totalCount: number;
  activeCount: number;
  invitedCount: number;
  suspendedCount: number;
  deactivatedCount: number;
  mfaEnrolledCount: number;
  mfaNotEnrolledCount: number;
  byCategory: { category: string; count: number }[];
  byOrganisationUnit: { unit: string; count: number }[];
  riskFlags: { flag: string; count: number; class: string }[];
}

export interface DashboardAccessRequestsSummary {
  totalCount: number;
  submittedCount: number;
  pendingReviewCount: number;
  approvedCount: number;
  rejectedCount: number;
  slaDueCount: number;
  recentRequests: { reference: string; user: string; type: string; state: string; stateClass: string; slaDue: string }[];
}

export interface DashboardRolesSummary {
  totalCount: number;
  byType: { type: string; count: number }[];
  byClassification: { classification: string; count: number }[];
  totalAssignedUsers: number;
  topRoles: { name: string; assignedUsers: number }[];
}

export interface DashboardSecuritySummary {
  totalFailedSigninsLast24h: number;
  totalFailedSigninsLast7Days: number;
  totalFailedSigninsAllTime: number;
  activeSessionsCount: number;
  trustedDevicesCount: number;
}