export interface AccessRequestItem {
  reference: string;
  requestType: string;
  user: string;
  currentRoleAndScope: string;
  requestedRole: string;
  scopeType: string;
  scopeValue: string;
  effectiveFrom: string;
  effectiveTo: string;
  businessJustification: string;
  requester?: string;
  requestedTime?: string;
  approverRoute: string;
  slaDue: string;
  approvalState: string;
  approvalStateClass?: string;
  decision?: string;
  decisionReason?: string;
  decisionActor?: string;
  decisionTime?: string;
}

export interface AccessRequestData {
  requests: AccessRequestItem[];
  totalCount: number;
  filters: {
    states: { value: string; label: string }[];
    requestTypes: { value: string; label: string }[];
  };
}