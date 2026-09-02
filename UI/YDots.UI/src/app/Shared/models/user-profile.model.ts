export interface RoleAssignmentItem {
  role: string;
  assignmentType: string;
  scope: string;
  permissions: number;
  term: string;
}

/**
 * What the profile screen renders.
 *
 * `workLocation` and `accessReviewDue` are GONE rather than blank. The server has never carried
 * either - there is no work-location column on a user, and an access review is a governance
 * record owned by the review campaign - so both were written as empty strings and rendered as
 * empty rows, which reads as missing data rather than as a field nobody collects. The Access
 * tab now points at the campaign screen for the review instead of showing a blank date.
 */
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
  preferredLanguage: string;
  timeZone: string;
  roleAssignments: RoleAssignmentItem[];
  dataScopes: string;
  accessStartDate: string;
  accessEndDate: string;
  joinedOn: string;
  mfaStatus: string;
  mfaStatusClass: string;
  activeSessions: number;
  activeSessionDevices: number;
  trustedDevices: number;
  lastSignIn: string;
  failedSignins: { last24Hours: number; last7Days: number; total: number };
  concurrencyVersion: string;
}