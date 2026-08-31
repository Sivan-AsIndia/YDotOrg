using System.Security.Claims;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Abstractions.Security;

/// <summary>
/// Mints and reads the tokens. IAM is the only service in the solution that implements this.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// The ordinary access token.
    ///
    /// <paramref name="operatingTenantId"/> is what makes one token type serve both kinds of
    /// caller. For a Tenant user it is their own Organisation and equals their
    /// <c>User.TenantId</c>. For SuperAdmin it is whichever Organisation they selected, while
    /// their stored TenantId stays null. Either way the resulting token carries one
    /// <c>tenant_id</c> claim, so every downstream service applies the same rule to both.
    /// </summary>
    IssuedToken CreateAccessToken(
        User user,
        Tenant? operatingTenant,
        BusinessUnit businessUnit,
        Guid sessionId,
        AccessScopeType scope,
        EffectiveAccess access,
        ClientType clientType,
        string? hostName,
        string? deviceIdentifier,
        bool mfaCompleted);

    /// <summary>
    /// The half-authenticated token issued between password and second factor. Carries no
    /// permission claims and is accepted only by the MFA verification endpoint, which is what
    /// stops it being used to walk past the challenge.
    /// </summary>
    IssuedToken CreateMfaPendingToken(User user, Tenant? tenant, BusinessUnit businessUnit, string? hostName);

    /// <summary>
    /// Issued to SuperAdmin after a successful password check but before they have chosen an
    /// Organisation. Carries Global scope, no tenant_id, and no Tenant permissions, so the
    /// only thing it can do is list Organisations and select one.
    /// </summary>
    IssuedToken CreateTenantSelectionToken(User user, BusinessUnit businessUnit, string? hostName);

    /// <summary>A cryptographically random refresh token plus its storable hash.</summary>
    (string Token, string TokenHash, DateTimeOffset ExpiresAtUtc) CreateRefreshToken();

    /// <summary>
    /// Reads a token that may well be expired, for the refresh path. Signature, issuer and
    /// audience are still validated — only the lifetime check is skipped — so an unsigned or
    /// tampered token is rejected here exactly as it would be anywhere else.
    /// </summary>
    ClaimsPrincipal? ReadExpiredToken(string accessToken);

    /// <summary>Validates a token in full. Returns null when it fails for any reason.</summary>
    ClaimsPrincipal? ValidateToken(string token);
}

/// <summary>A minted token and the facts the caller needs about it.</summary>
public sealed record IssuedToken(
    string Token,
    DateTimeOffset ExpiresAtUtc,
    int ExpiresInSeconds,
    TokenType TokenType);
