export interface MfaMethod {
  method: string;
  icon: string;
  iconColor: string;
  status: string;
  statusClass: string;
  enrolledOn: string;
  lastUsed: string;
}

export interface ActiveSession {
  device: string;
  deviceIcon: string;
  browser: string;
  ipAddress: string;
  lastActive: string;
}

export interface TrustedDevice {
  device: string;
  deviceIcon: string;
  type: string;
  trustedOn: string;
  expiry: string;
}

export interface FailedSignins {
  last24Hours: number;
  last7Days: number;
  total: number;
}

export interface RecentEvent {
  event: string;
  icon: string;
  iconColor: string;
  details: string;
  dateTime: string;
  status: string;
  statusClass: string;
}

export interface VerifiedContact {
  icon: string;
  value: string;
  status: string;
}

export interface UserInfo {
  reference: string;
  displayName: string;
  passwordLastChanged: string;
  mfaRequirement: string;
  recoveryCodeStatus: string;
  riskFlags: string;
}

export interface UserSecurityData {
  user: UserInfo;
  mfaMethods: MfaMethod[];
  activeSessions: ActiveSession[];
  trustedDevices: TrustedDevice[];
  failedSignins: FailedSignins;
  recentEvents: RecentEvent[];
  verifiedContacts: VerifiedContact[];
}