namespace YDot.IAM.Domain.Enums;

/// <summary>
/// Section 3.1: Employee|Volunteer|Partner|Auditor|Support|DonorPortal.
///
/// <c>DonorPortal</c> is the category the future Lead/Donor flow will use: a lead scans a
/// QR code, pays, and IAM creates a DonorPortal user and mails them an activation link.
/// It is listed here now so that flow does not need a schema change later.
/// </summary>
public enum UserAccountCategory
{
    Employee = 0,
    Volunteer = 1,
    Partner = 2,
    Auditor = 3,
    Support = 4,
    DonorPortal = 5
}
