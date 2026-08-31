namespace YDot.IAM.Domain.Enums;

/// <summary>How a brand-new user gets their first password.</summary>
public enum CredentialSetupMethod
{
    /// <summary>They receive a link and choose their own. The default and the safest.</summary>
    InvitationLink = 0,

    /// <summary>An administrator sets one and must hand it over out of band.</summary>
    AdministratorSet = 1,

    /// <summary>Generated, mailed, and must be changed at first sign-in.</summary>
    TemporaryPassword = 2
}
