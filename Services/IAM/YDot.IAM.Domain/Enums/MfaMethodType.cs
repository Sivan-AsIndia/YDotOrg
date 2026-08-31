namespace YDot.IAM.Domain.Enums;

/// <summary>Section 3.6: AuthenticatorApp|Sms|Email|SecurityKey.</summary>
public enum MfaMethodType
{
    AuthenticatorApp = 0,
    Sms = 1,
    Email = 2,
    SecurityKey = 3
}
