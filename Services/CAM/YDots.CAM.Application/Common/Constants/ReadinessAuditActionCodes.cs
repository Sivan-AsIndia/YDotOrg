namespace YDots.CAM.Application.Common.Constants;

public static class ReadinessAuditActionCodes
{
    public const string Created = "READINESS_CHECK_CREATED";
    public const string Updated = "READINESS_CHECK_UPDATED";
    public const string Passed = "READINESS_CHECK_PASSED";
    public const string Failed = "READINESS_CHECK_FAILED";
    public const string BlockerAssigned = "READINESS_BLOCKER_ASSIGNED";
    public const string BlockerResolved = "READINESS_BLOCKER_RESOLVED";
    public const string Deleted = "READINESS_CHECK_DELETED";
    public const string ApprovalRequested = "CAMPAIGN_READINESS_APPROVAL_REQUESTED";
    public const string Approved = "CAMPAIGN_READINESS_APPROVED";
    public const string ReturnedToDraft = "CAMPAIGN_READINESS_RETURNED_TO_DRAFT";
}
