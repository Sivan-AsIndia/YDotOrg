export interface MySecurityData {
  displayName: string;
  loginEmail: string;
  username: string;
  verifiedMobile: string;
  passwordLastChanged: string;
  mfaRequirement: string;
  mfaMethods: { method: string; icon: string; status: string; enrolledOn: string }[];
  recoveryCodeStatus: string;
  recoveryCodeStatusClass: string;
  recoveryMethods: string;
  activeSessions: { device: string; browser: string; ipAddress: string; lastActive: string }[];
  trustedDevices: { device: string; type: string; trustedOn: string }[];
  recentActivity: { event: string; details: string; dateTime: string; icon: string }[];
  signInAlerts: string;
}