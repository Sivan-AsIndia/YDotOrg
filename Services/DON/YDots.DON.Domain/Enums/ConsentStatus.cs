namespace YDots.DON.Domain.Enums;

/// <summary>
/// Module-defined lifecycle enum for Consent (section 3.3). Consent is append only:
/// a correction supersedes the previous row rather than editing it.
/// </summary>
public enum ConsentStatus
{
    Active = 1,
    Withdrawn = 2,
    Expired = 3,
    Superseded = 4
}
