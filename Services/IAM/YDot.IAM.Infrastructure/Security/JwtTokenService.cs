using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Security;

/// <summary>
/// Signs and reads the tokens. IAM is the only service in the solution that implements this;
/// every other one only validates what this class produced.
///
/// WHAT GOES INTO AN ACCESS TOKEN, AND WHY EACH PIECE IS THERE:
///
/// <code>
/// sub, user_code, display_name       who
/// tenant_id + organisation_id        which Organisation this token operates in
/// business_unit_id                   the root boundary
/// scope = Global | Tenant            may this caller cross an Organisation boundary at all
/// tenant_mode                        is a Global caller currently acting inside one
/// permission (one per code)          what they may do, checked with no database call
/// data_scope (one per assignment)    which records, within the Organisation
/// session_id                         the row that can be revoked
/// security_stamp                     invalidates the token the moment access changes
/// host_name                          binds the token to the host it was issued for
/// token_type                         stops an MFA-pending token being used as a real one
/// </code>
///
/// THE TWO CLAIMS THAT DO THE HEAVY LIFTING are <c>security_stamp</c> and <c>session_id</c>.
/// A JWT cannot be un-issued, so without them "sign out on my lost phone" would be a
/// fifteen-minute promise. With them, the next request carrying that token is refused even
/// though its signature and expiry are both still perfectly valid.
///
/// <c>organisation_id</c> IS WRITTEN ALONGSIDE <c>tenant_id</c>, with the same value. The
/// Donors service reads <c>organisation_id</c> and treats it as its isolation boundary;
/// emitting both means DON keeps working unchanged while IAM uses the newer vocabulary, and
/// neither service has to be deployed in lockstep with the other.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtSettings> jwtOptions) : IJwtTokenService
{
    private readonly JwtSettings _jwt = jwtOptions.Value;
    private static readonly JwtSecurityTokenHandler Handler = new();

    public IssuedToken CreateAccessToken(
        User user,
        Tenant? operatingTenant,
        BusinessUnit businessUnit,
        Guid sessionId,
        AccessScopeType scope,
        EffectiveAccess access,
        ClientType clientType,
        string? hostName,
        string? deviceIdentifier,
        bool mfaCompleted)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(businessUnit);
        ArgumentNullException.ThrowIfNull(access);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_jwt.AccessTokenMinutes);

        var claims = BuildCoreClaims(user, operatingTenant, businessUnit, scope, hostName, now);

        claims.Add(new Claim(ClaimTypeNames.SessionId, sessionId.ToString()));
        claims.Add(new Claim(ClaimTypeNames.TokenType, TokenType.Access.ToString()));
        claims.Add(new Claim(ClaimTypeNames.ClientType, clientType.ToString()));
        claims.Add(new Claim(ClaimTypeNames.MfaCompleted, mfaCompleted ? "true" : "false"));

        if (!string.IsNullOrWhiteSpace(deviceIdentifier))
        {
            claims.Add(new Claim(ClaimTypeNames.DeviceIdentifier, deviceIdentifier));
        }

        if (user.DepartmentId.HasValue)
        {
            claims.Add(new Claim(ClaimTypeNames.DepartmentId, user.DepartmentId.Value.ToString()));
        }

        if (user.OrganisationUnitId.HasValue)
        {
            claims.Add(new Claim(ClaimTypeNames.OrganisationUnitId, user.OrganisationUnitId.Value.ToString()));
        }

        // ---- Roles ------------------------------------------------------------------------
        foreach (var role in access.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Code));
        }

        // ---- Permissions ---------------------------------------------------------------------
        //
        // One claim per code. That is what lets a sibling service authorise a request with a
        // claim lookup and no cross-service call on the hot path.
        //
        // SuperAdmin is the exception: writing every permission in the catalogue into their
        // token would make it enormous for no benefit, because the scope claim already says
        // they are unrestricted. The permission handler checks scope first.
        if (!user.IsSuperAdmin)
        {
            foreach (var permission in access.PermissionCodes)
            {
                claims.Add(new Claim(ClaimTypeNames.Permission, permission));
            }
        }

        // ---- Data scopes -----------------------------------------------------------------------
        //
        // "{ScopeType}:{ScopeValue}", which is the exact shape the Donors service parses.
        foreach (var dataScope in access.DataScopes)
        {
            claims.Add(new Claim(ClaimTypeNames.DataScope, dataScope));
        }

        // ---- Role claims -------------------------------------------------------------------------
        foreach (var claim in access.Claims)
        {
            claims.Add(new Claim(claim.Key, claim.Value));
        }

        return Sign(claims, expiresAt, TokenType.Access);
    }

    /// <summary>
    /// The half-authenticated token issued between password and second factor.
    ///
    /// It carries NO permissions and NO session, and its <c>token_type</c> says MfaPending —
    /// which the authorization policy checks. Without that claim a half-authenticated token
    /// would be indistinguishable from a real one and the MFA step could simply be skipped.
    /// </summary>
    public IssuedToken CreateMfaPendingToken(
        User user, Tenant? tenant, BusinessUnit businessUnit, string? hostName)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(businessUnit);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_jwt.MfaPendingTokenMinutes);

        var claims = BuildCoreClaims(
            user, tenant, businessUnit,
            user.IsSuperAdmin ? AccessScopeType.Global : AccessScopeType.Tenant,
            hostName, now);

        claims.Add(new Claim(ClaimTypeNames.TokenType, TokenType.MfaPending.ToString()));
        claims.Add(new Claim(ClaimTypeNames.MfaCompleted, "false"));

        return Sign(claims, expiresAt, TokenType.MfaPending);
    }

    /// <summary>
    /// Issued to SuperAdmin after the password check but before they have chosen an
    /// Organisation.
    ///
    /// Global scope, no tenant_id, no Tenant permissions. The only things it can reach are the
    /// Organisation list and the select-tenant endpoint, which is exactly the surface that
    /// screen needs.
    /// </summary>
    public IssuedToken CreateTenantSelectionToken(User user, BusinessUnit businessUnit, string? hostName)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(businessUnit);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_jwt.TenantSelectionTokenMinutes);

        var claims = BuildCoreClaims(user, null, businessUnit, AccessScopeType.Global, hostName, now);

        claims.Add(new Claim(ClaimTypeNames.TokenType, TokenType.TenantSelectionPending.ToString()));
        claims.Add(new Claim(ClaimTypeNames.Permission, PermissionCodes.Platform.TenantsView));
        claims.Add(new Claim(ClaimTypeNames.Permission, PermissionCodes.Platform.TenantsSelect));

        return Sign(claims, expiresAt, TokenType.TenantSelectionPending);
    }

    /// <summary>
    /// A refresh token and its storable hash.
    ///
    /// 256 bits from the OS random source, never <c>System.Random</c>: a predictable refresh
    /// token is a permanent session for whoever guesses it. Only the hash is ever persisted.
    /// </summary>
    public (string Token, string TokenHash, DateTimeOffset ExpiresAtUtc) CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncoder.Encode(bytes);

        return (token, Sha256(token), DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenDays));
    }

    /// <summary>
    /// Reads a token that may well have expired, for the refresh path.
    ///
    /// ONLY the lifetime check is skipped. Signature, issuer and audience are all still
    /// validated, so an unsigned or tampered token is rejected here exactly as it would be
    /// anywhere else — the point is to read the claims of a legitimately expired token, not
    /// to accept a forged one.
    /// </summary>
    public ClaimsPrincipal? ReadExpiredToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        try
        {
            return Handler.ValidateToken(accessToken, BuildValidationParameters(validateLifetime: false), out _);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            return Handler.ValidateToken(token, BuildValidationParameters(validateLifetime: true), out _);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The claims every token carries, whatever its type.
    ///
    /// Note that <c>tenant_id</c> comes from <paramref name="operatingTenant"/> and NOT from
    /// <c>user.TenantId</c>. For an ordinary user the two are the same. For SuperAdmin the
    /// user value is null forever and the operating value is whichever Organisation they
    /// selected — which is precisely the distinction section 4.1 of the brief insists on.
    /// </summary>
    private List<Claim> BuildCoreClaims(
        User user,
        Tenant? operatingTenant,
        BusinessUnit businessUnit,
        AccessScopeType scope,
        string? hostName,
        DateTimeOffset issuedAt)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypeNames.UserCode, user.Code),
            new(ClaimTypeNames.DisplayName, user.DisplayName),
            new(ClaimTypeNames.Username, user.UserName ?? string.Empty),
            new(ClaimTypeNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypeNames.UserStatus, user.Status.ToString()),
            new(ClaimTypeNames.AccountCategory, user.AccountCategory.ToString()),
            new(ClaimTypeNames.PrivilegeLevel, user.PrivilegeLevel.ToString()),
            new(ClaimTypeNames.Scope, scope.ToString()),
            new(ClaimTypeNames.IsSuperAdmin, user.IsSuperAdmin ? "true" : "false"),
            new(ClaimTypeNames.IsTenantAdmin, user.IsTenantAdmin ? "true" : "false"),
            new(ClaimTypeNames.BusinessUnitId, businessUnit.Id.ToString()),
            new(ClaimTypeNames.BusinessUnitCode, businessUnit.Code),

            // Any credential, role or status change rotates this, and a token whose stamp no
            // longer matches the stored one is refused. This is what makes revocation immediate.
            new(ClaimTypeNames.SecurityStamp, user.SecurityStamp ?? string.Empty),

            new(ClaimTypeNames.AuthenticatedAt, issuedAt.ToUnixTimeSeconds().ToString(
                System.Globalization.CultureInfo.InvariantCulture))
        };

        if (operatingTenant is not null)
        {
            claims.Add(new Claim(ClaimTypeNames.TenantId, operatingTenant.Id.ToString()));

            // Same value, second name. DON reads organisation_id as its isolation boundary,
            // so emitting both keeps that service working without a lockstep deployment.
            claims.Add(new Claim(ClaimTypeNames.OrganisationId, operatingTenant.Id.ToString()));

            claims.Add(new Claim(ClaimTypeNames.TenantCode, operatingTenant.Code));
            claims.Add(new Claim(ClaimTypeNames.TenantName, operatingTenant.Name));

            // True when a Global caller is acting inside an Organisation, which the audit
            // trail and the client "acting as" banner both key off.
            claims.Add(new Claim(
                ClaimTypeNames.TenantMode,
                scope == AccessScopeType.Global ? "true" : "false"));
        }

        // Binds the token to the host it was minted for, so one issued for ten1 cannot simply
        // be replayed against ten2 even though the signature is fine.
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            claims.Add(new Claim(ClaimTypeNames.HostName, hostName));
        }

        return claims;
    }

    private IssuedToken Sign(List<Claim> claims, DateTimeOffset expiresAt, TokenType tokenType)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var encoded = Handler.WriteToken(token);
        var seconds = (int)Math.Max(0, (expiresAt - DateTimeOffset.UtcNow).TotalSeconds);

        return new IssuedToken(encoded, expiresAt, seconds, tokenType);
    }

    private TokenValidationParameters BuildValidationParameters(bool validateLifetime) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = validateLifetime,
        ValidateIssuerSigningKey = true,
        ValidIssuer = _jwt.Issuer,
        ValidAudience = _jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)),
        ClockSkew = TimeSpan.FromSeconds(_jwt.ClockSkewSeconds),
        RoleClaimType = ClaimTypes.Role,
        NameClaimType = ClaimTypes.NameIdentifier
    };

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
