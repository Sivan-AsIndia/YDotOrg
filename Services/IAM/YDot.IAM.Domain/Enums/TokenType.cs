namespace YDot.IAM.Domain.Enums;

/// <summary>
/// Written into every JWT as the <c>token_type</c> claim.
///
/// This is what stops a token issued for one job being replayed for another. The clearest
/// case is <see cref="MfaPending"/>: it is a real signed token, but it carries no
/// permissions and is accepted by exactly one endpoint — the MFA verification one. Without
/// this claim, a half-authenticated token would look identical to a fully authenticated
/// one and MFA could be walked straight past.
/// </summary>
public enum TokenType
{
    /// <summary>The ordinary bearer token used for API calls.</summary>
    Access = 0,

    /// <summary>Long-lived, single-use, rotated on every refresh.</summary>
    Refresh = 1,

    /// <summary>Password accepted, second factor still outstanding. No permissions.</summary>
    MfaPending = 2,

    /// <summary>Issued after a step-up, entitles the bearer to one privileged action.</summary>
    StepUp = 3,

    /// <summary>SuperAdmin has authenticated but has not yet selected an Organisation.</summary>
    TenantSelectionPending = 4
}
