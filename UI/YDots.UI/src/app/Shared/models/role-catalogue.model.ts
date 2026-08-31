export interface RoleItem {
  reference: string;
  roleName: string;
  roleCode: string;
  purpose: string;
  roleType: string;
  owningFunction: string;
  permissionBundle: string;
  excludedPermissions?: string;
  defaultScopeType?: string;
  incompatibleRoles?: string;
  assignmentPrerequisites?: string;
  maximumDuration?: string;
  reviewInterval?: string;
  privilegeClassification: string;
  roleVersion: string;
  approvalState: string;
  approvalStateClass: string;
  assignedUserCount: number;
  effectiveDate: string;
  retirementReason?: string;
}

export interface RoleCatalogueData {
  roles: RoleItem[];
  totalCount: number;
  filters: {
    statuses: { value: string; label: string }[];
    roleTypes: { value: string; label: string }[];
    functions: { value: string; label: string }[];
  };
}