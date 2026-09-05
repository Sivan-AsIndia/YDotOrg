namespace YDot.IAM.Domain.Enums;

/// <summary>
/// What happened to a payment gateway configuration, as written into its change log.
///
/// APPEND-ONLY, LIKE EVERY AUDIT VOCABULARY IN THIS SERVICE. A member may be added but never
/// renamed: the name is persisted as text, so renaming one orphans every historical row that
/// used it.
/// </summary>
public enum PaymentGatewayConfigurationAction
{
    Created = 0,

    Updated = 1,

    Activated = 2,

    Deactivated = 3,

    Deleted = 4,

    /// <summary>The Test button was pressed. Recorded whether it passed or failed.</summary>
    Tested = 5,

    /// <summary>
    /// A credential was replaced. Separated from <see cref="Updated"/> because it is the change
    /// that decides whose merchant account the money reaches.
    /// </summary>
    CredentialsRotated = 6
}
