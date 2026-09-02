namespace YDots.DON.Application.Common.Constants;

/// <summary>
/// The seven action types every permission code in the platform falls under.
///
/// MIRRORED FROM <c>YDot.IAM.Domain/Enums/PermissionAction.cs</c>, where the same seven values
/// decide which of the three tenant roles a code lands in. DON declares the action for each of
/// its own codes in <see cref="PermissionCodes.Catalogue"/> so the two services classify the
/// same code the same way - a code DON thinks is an Operate and IAM thinks is an Approve would
/// be granted to INITIATOR by one service and withheld by the other, and the only symptom would
/// be a 403 on an endpoint whose attribute looks correct.
///
/// <see cref="Operate"/> IS TWO DIFFERENT THINGS WEARING ONE NAME, which is why
/// <see cref="PermissionCodes.PostDecisionOperations"/> exists. Merging two donor records and
/// escalating an identity check for review are both Operate, and only one of them belongs to a
/// checker: the merge joins two donors' donations, receipts and consent history irreversibly.
/// </summary>
public enum PermissionAction
{
    View = 0,
    Create = 1,
    Edit = 2,
    Submit = 3,
    Approve = 4,
    Operate = 5,
    Export = 6
}
