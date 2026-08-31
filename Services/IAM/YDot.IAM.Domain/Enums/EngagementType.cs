namespace YDot.IAM.Domain.Enums;

/// <summary>How a person is engaged by the Organisation. Drives which profile fields are required.</summary>
public enum EngagementType
{
    FullTime = 0,
    PartTime = 1,
    Contract = 2,
    Volunteer = 3,
    Intern = 4,
    External = 5
}
