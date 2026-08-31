namespace YDot.IAM.Application.Common.Abstractions.Security;

/// <summary>
/// Generates the secrets that travel in links and cookies, and hashes them for storage.
///
/// EVERY SECRET IS STORED AS A HASH, never as itself. Invitation tokens, reset tokens,
/// refresh tokens, session tokens, device tokens and recovery codes all go through here, so
/// a stolen database yields nothing usable. The plaintext exists only in the e-mail or the
/// cookie held by the person it was issued to.
/// </summary>
public interface ITokenHasher
{
    /// <summary>
    /// A cryptographically random, URL-safe secret. Uses the OS random source, never
    /// System.Random, because a predictable invitation token is a way into somebody account.
    /// </summary>
    string GenerateToken(int byteLength = 32);

    /// <summary>SHA-256 of the token, hex-encoded. Deterministic, so the lookup is an index seek.</summary>
    string Hash(string token);

    /// <summary>
    /// Constant-time comparison. A naive string equality leaks how many leading characters
    /// matched through its timing, which is enough to reconstruct a token byte by byte.
    /// </summary>
    bool Verify(string token, string expectedHash);

    /// <summary>A short, non-secret, human-readable reference such as INV-7K2M9X.</summary>
    string GenerateReference(string prefix);

    /// <summary>A numeric one-time code of the given length, from the OS random source.</summary>
    string GenerateNumericCode(int digits = 6);
}
