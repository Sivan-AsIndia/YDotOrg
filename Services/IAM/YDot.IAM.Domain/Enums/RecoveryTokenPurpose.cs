namespace YDot.IAM.Domain.Enums;

/// <summary>What a single-use recovery token entitles the bearer to do.</summary>
public enum RecoveryTokenPurpose
{
    PasswordReset = 0,
    EmailConfirmation = 1,
    InvitationAcceptance = 2,
    LoginIdentifierChange = 3,
    AccountUnlock = 4
}
