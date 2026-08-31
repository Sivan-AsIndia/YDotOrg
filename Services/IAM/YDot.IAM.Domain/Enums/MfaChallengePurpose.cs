namespace YDot.IAM.Domain.Enums;

/// <summary>
/// Why a one-time code was issued. A challenge minted for one purpose must never satisfy
/// another: a code e-mailed to prove an enrolment is not a licence to re-authenticate into
/// a privileged action.
/// </summary>
public enum MfaChallengePurpose
{
    /// <summary>Second factor during sign-in.</summary>
    SignIn = 0,

    /// <summary>Proving a newly enrolled method actually works.</summary>
    Enrolment = 1,

    /// <summary>Step-up before a sensitive action.</summary>
    Reauthentication = 2,

    /// <summary>Confirming ownership of a new login e-mail or username.</summary>
    LoginIdentifierChange = 3,

    /// <summary>Password recovery when the account has MFA on it.</summary>
    PasswordRecovery = 4
}
