namespace YDot.IAM.Application.Common.Abstractions.Security;

/// <summary>
/// Password hashing and policy.
///
/// The implementation delegates to the ASP.NET Core Identity PasswordHasher, so the
/// algorithm, iteration count and salt handling follow the framework rather than anything
/// hand-rolled, and improve when it does.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Verifies, and reports whether the stored hash used older parameters. When it did, the
    /// caller rehashes with the current ones, so an account silently strengthens the next
    /// time its owner signs in.
    /// </summary>
    PasswordVerificationOutcome Verify(string hash, string password);

    /// <summary>
    /// Checks the password against the policy. Returns every failure at once rather than the
    /// first, so somebody is not sent round the loop four times.
    /// </summary>
    IReadOnlyList<string> ValidatePolicy(string password, int minimumLength);

    /// <summary>A random password meeting the policy, for administrator-set credentials.</summary>
    string GenerateTemporaryPassword(int length = 16);
}

/// <summary>Result of a password check.</summary>
public enum PasswordVerificationOutcome
{
    Failed = 0,
    Succeeded = 1,

    /// <summary>Correct, but the stored hash should be upgraded to the current parameters.</summary>
    SucceededRehashNeeded = 2
}
