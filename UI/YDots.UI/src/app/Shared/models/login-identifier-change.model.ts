export interface UserInfo {
  reference: string;
  displayName: string;
  currentLoginEmail: string;
  currentUsername: string;
}

export interface ChangeStatus {
  verificationState: string;
  notificationState: string;
  approver: string;
  effectiveTime: string;
  sessionRevocation: string;
}

export interface ValidationMessages {
  duplicateResultText: string;
  reservedNameResultText: string;
}

export interface LoginIdentifierChangeData {
  user: UserInfo;
  changeStatus: ChangeStatus;
  validationMessages: ValidationMessages;
}