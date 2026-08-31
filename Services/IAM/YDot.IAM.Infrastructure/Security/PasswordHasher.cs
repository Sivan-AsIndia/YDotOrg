using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Domain.Entities;
using IApplicationPasswordHasher = YDot.IAM.Application.Common.Abstractions.Security.IPasswordHasher;

namespace YDot.IAM.Infrastructure.Security;

/// <summary>
/// Password hashing and policy.
///
/// THE HASHING IS THE FRAMEWORK, DELIBERATELY. It delegates to ASP.NET Core Identity
/// <see cref="PasswordHasher{TUser}"/>, so the algorithm, iteration count and salt handling
/// are Microsoft problem rather than ours — and they improve when the framework does, which a
/// hand-rolled implementation never would. Rolling your own password hashing is the classic
/// way to end up with something that looks fine and is not.
///
/// <see cref="PasswordVerificationOutcome.SucceededRehashNeeded"/> is what makes that upgrade
/// path real: when the stored hash used older parameters the caller rehashes with the current
/// ones, so an account silently strengthens the next time its owner signs in.
///
/// THE POLICY IS OURS, and it reports EVERY failure at once rather than the first. Being told
/// "needs a digit", fixing it, then being told "needs a symbol" is how people end up choosing
/// Password1! — the annoyance produces a worse password, not a better one.
/// </summary>
public sealed class PasswordHasher(IOptions<SecuritySettings> securityOptions) : IApplicationPasswordHasher
{
    private readonly SecuritySettings _security = securityOptions.Value;
    private static readonly PasswordHasher<User> Framework = new();

    /// <summary>
    /// The alphabet for a generated temporary password. Excludes the characters people misread
    /// when one is dictated: 0/O, 1/l/I. A temporary password gets read aloud more often than
    /// anybody would like.
    /// </summary>
    private const string UpperCharacters = "ABCDEFGHJKMNPQRSTUVWXYZ";
    private const string LowerCharacters = "abcdefghijkmnpqrstuvwxyz";
    private const string DigitCharacters = "23456789";
    private const string SymbolCharacters = "!@#$%^&*-_=+";

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        // The framework hasher takes a user only to satisfy its generic signature; the
        // default implementation never reads it.
        return Framework.HashPassword(new User(), password);
    }

    public PasswordVerificationOutcome Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password))
        {
            return PasswordVerificationOutcome.Failed;
        }

        return Framework.VerifyHashedPassword(new User(), hash, password) switch
        {
            PasswordVerificationResult.Success => PasswordVerificationOutcome.Succeeded,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationOutcome.SucceededRehashNeeded,
            _ => PasswordVerificationOutcome.Failed
        };
    }

    /// <summary>
    /// Checks the password against the policy, returning every failure at once.
    ///
    /// <paramref name="minimumLength"/> is passed in rather than read from settings, because
    /// an Organisation may set a stricter minimum than the platform floor and the caller has
    /// already resolved which applies.
    /// </summary>
    public IReadOnlyList<string> ValidatePolicy(string password, int minimumLength)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(password))
        {
            failures.Add("Enter a password.");
            return failures;
        }

        var effectiveMinimum = Math.Max(minimumLength, _security.PasswordMinimumLength);

        if (password.Length < effectiveMinimum)
        {
            failures.Add($"Use at least {effectiveMinimum} characters.");
        }

        if (password.Length > _security.PasswordMaximumLength)
        {
            failures.Add($"Use no more than {_security.PasswordMaximumLength} characters.");
        }

        if (_security.PasswordRequireUppercase && !password.Any(char.IsUpper))
        {
            failures.Add("Include at least one capital letter.");
        }

        if (_security.PasswordRequireLowercase && !password.Any(char.IsLower))
        {
            failures.Add("Include at least one lower-case letter.");
        }

        if (_security.PasswordRequireDigit && !password.Any(char.IsDigit))
        {
            failures.Add("Include at least one number.");
        }

        if (_security.PasswordRequireNonAlphanumeric && password.All(char.IsLetterOrDigit))
        {
            failures.Add("Include at least one symbol.");
        }

        // A password made of one repeated character passes every rule above and is still
        // worthless, so it is caught explicitly.
        if (password.Length > 0 && password.Distinct().Count() == 1)
        {
            failures.Add("Do not use the same character repeated.");
        }

        return failures;
    }

    /// <summary>
    /// A random password that satisfies the policy by construction.
    ///
    /// One character is taken from each required class FIRST, then the remainder is filled at
    /// random, then the whole thing is shuffled. Generating randomly and re-testing would
    /// occasionally loop for a long time; this cannot fail to satisfy the rules.
    /// </summary>
    public string GenerateTemporaryPassword(int length = 16)
    {
        var target = Math.Clamp(
            Math.Max(length, _security.PasswordMinimumLength), 12, _security.PasswordMaximumLength);

        var characters = new List<char>(target)
        {
            UpperCharacters[RandomNumberGenerator.GetInt32(UpperCharacters.Length)],
            LowerCharacters[RandomNumberGenerator.GetInt32(LowerCharacters.Length)],
            DigitCharacters[RandomNumberGenerator.GetInt32(DigitCharacters.Length)],
            SymbolCharacters[RandomNumberGenerator.GetInt32(SymbolCharacters.Length)]
        };

        var all = UpperCharacters + LowerCharacters + DigitCharacters + SymbolCharacters;

        while (characters.Count < target)
        {
            characters.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);
        }

        // Fisher-Yates with the cryptographic generator, so the guaranteed characters are not
        // always the first four.
        for (var index = characters.Count - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (characters[index], characters[swap]) = (characters[swap], characters[index]);
        }

        return new string([.. characters]);
    }
}
