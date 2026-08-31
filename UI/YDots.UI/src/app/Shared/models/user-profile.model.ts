export interface RoleAssignmentItem {
  role: string;
  assignmentType: string;
  scope: string;
  permissions: number;
  term: string;
}

export interface UserProfileData {
  reference: string;
  displayName: string;
  loginEmail: string;
  username: string;
  mobileNumber: string;
  password: string;
  employeeId: string;
  accountCategory: string;
  accountStatus: string;
  accountStatusClass: string;
  invitationStatus: string;
  invitationStatusClass: string;
  organisationUnit: string;
  department: string;
  designation: string;
  manager: string;
  workLocation: string;
  preferredLanguage: string;
  timeZone: string;
  roleAssignments: RoleAssignmentItem[];
  dataScopes: string;
  accessStartDate: string;
  accessEndDate: string;
  accessReviewDue: string;
  mfaStatus: string;
  mfaStatusClass: string;
  activeSessions: number;
  activeSessionDevices: number;
  trustedDevices: number;
  lastSignIn: string;
  failedSignins: { last24Hours: number; last7Days: number; total: number };
  concurrencyVersion: string;
}