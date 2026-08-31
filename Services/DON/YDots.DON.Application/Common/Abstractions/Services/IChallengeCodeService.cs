namespace YDots.DON.Application.Common.Abstractions.Services;

/// <summary>
/// Creates and checks the identity verification codes used by DON-UI-07.
///
/// The plain code is returned once so it can be delivered to the donor; only the hash is ever
/// stored. That way somebody who can read the table still cannot pass another person's challenge.
/// </summary>
public interface IChallengeCodeService
{
    /// <summary>Returns the code to deliver and the hash to store.</summary>
    (string Code, string CodeHash) Create();

    /// <summary>Constant-time comparison of a supplied code against the stored hash.</summary>
    bool Verify(string code, string? storedHash);

    /// <summary>Turns "+919876543210" into "+91******3210" for display.</summary>
    string MaskDestination(string? destination);
}
