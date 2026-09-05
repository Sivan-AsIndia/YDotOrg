using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using YDot.IAM.Api.Filters;
using YDot.IAM.Api.Security;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Infrastructure.ServiceContainer;

namespace YDot.IAM.Api.ServiceContainer;

/// <summary>
/// Everything the API layer itself registers: MVC, the validation filter, JSON, JWT bearer
/// authentication, CORS and Swagger.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// The CORS policy name. Angular runs on a different origin in development and on a
    /// different SUBDOMAIN in production, so it is cross-origin either way.
    /// </summary>
    public const string CorsPolicyName = "YDotAngularClient";

    public static IServiceCollection AddApiServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddControllers(options =>
            {
                // Model-state validation runs through FluentValidation and returns the same
                // ApiResponse envelope as everything else — see the note on SuppressModelState
                // below.
                options.Filters.Add<FluentValidationFilter>();
            })
            .AddJsonOptions(options =>
            {
                // ENUMS ARE STRINGS ON THE WIRE. "Suspended" survives a reorder of the enum;
                // 4 does not, and the Angular client would silently mean something else after
                // a domain change.
                options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.DefaultIgnoreCondition =
                    JsonIgnoreCondition.WhenWritingNull;
            });

        // The framework's automatic 400 is turned OFF so a binding failure comes back in the
        // SAME envelope as a validation failure and an authorisation failure. A client that has
        // to parse two different error shapes ends up handling only one of them.
        services.Configure<ApiBehaviorOptions>(options =>
            options.SuppressModelStateInvalidFilter = true);

        services.AddScoped<RefreshTokenCookieWriter>();
        services.AddEndpointsApiExplorer();

        services.AddApiAuthentication(configuration);
        services.AddApiCors(configuration);
        services.AddApiSwagger();

        return services;
    }

    // =================================================================================
    // Authentication
    // =================================================================================

    private static IServiceCollection AddApiAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
                  ?? new JwtSettings();

        if (string.IsNullOrWhiteSpace(jwt.SigningKey))
        {
            // Failing at STARTUP rather than at the first sign-in. A missing signing key with a
            // silent fallback would mean tokens signed with a guessable secret, which is worse
            // than not booting.
            throw new InvalidOperationException(
                $"{JwtSettings.SectionName}:SigningKey is not configured. IAM cannot issue or " +
                "validate tokens without it.");
        }

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.SaveToken = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),

                    // The claim names in ClaimTypeNames are used VERBATIM. Without this, ASP.NET
                    // rewrites "sub" into the long SOAP-era URI and every lookup of "sub" in the
                    // code would come back empty.
                    NameClaimType = ClaimTypeNames.Username,
                    RoleClaimType = ClaimTypes.Role
                };

                options.MapInboundClaims = false;

                options.Events = new JwtBearerEvents
                {
                    // ---------------------------------------------------------------------
                    // SIGNING OUT HAS TO ACTUALLY SIGN YOU OUT.
                    //
                    // A JWT is self-contained: valid signature plus unexpired "exp" and the
                    // framework lets it through. Nothing above consults the session the token
                    // was issued against, so revoking that session - which is exactly what
                    // "Sign out" and "Sign out everywhere" do - changed a database row and
                    // nothing else. The token kept working for the rest of its life.
                    //
                    // WHAT THAT MEANT IN PRACTICE. Somebody whose laptop is stolen presses
                    // "Sign out everywhere", is told every session has ended, and the thief
                    // keeps full API access - reading the user directory, the audit trail, the
                    // donor records - for up to AccessTokenMinutes afterwards. That is 15
                    // minutes in the shipped configuration and 60 in Development. The one
                    // control offered for a compromised device did not do the thing its name
                    // promises, which is worse than not offering it.
                    //
                    // THE COST is one indexed primary-key lookup per authenticated request,
                    // paid so that revocation is immediate rather than eventual. If that ever
                    // shows up in a profile, cache the POSITIVE answer for a few seconds - but
                    // never the negative one, because a revoked session must stay revoked.
                    OnTokenValidated = async context =>
                    {
                        var raw = context.Principal?.FindFirst(ClaimTypeNames.SessionId)?.Value;

                        // Every token this service issues carries a session id. One without it
                        // was not issued here, whatever its signature says.
                        if (!Guid.TryParse(raw, out var sessionId))
                        {
                            context.Fail("The token names no session.");
                            return;
                        }

                        var services = context.HttpContext.RequestServices;
                        var security = services.GetRequiredService<ISecurityRepository>();
                        var clock = services.GetRequiredService<IDateTimeProvider>();

                        var active = await security.IsSessionActiveAsync(
                            sessionId, clock.UtcNow, context.HttpContext.RequestAborted);

                        if (!active)
                        {
                            context.Fail("That session has ended.");
                        }
                    },

                    // Both failure paths return the SAME envelope as every other response.
                    // The default handler writes an empty body with a WWW-Authenticate header,
                    // which the Angular error interceptor cannot read a message out of.
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();

                        if (context.Response.HasStarted)
                        {
                            return;
                        }

                        await WriteEnvelopeAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            Error.Unauthorised());
                    },

                    OnForbidden = async context =>
                    {
                        if (context.Response.HasStarted)
                        {
                            return;
                        }

                        await WriteEnvelopeAsync(
                            context.HttpContext,
                            StatusCodes.Status403Forbidden,
                            Error.Forbidden());
                    }
                };
            });

        services.AddAuthorization(options => options.AddIamPolicies());

        return services;
    }

    // =================================================================================
    // CORS
    // =================================================================================

    /// <summary>
    /// CORS for the Angular client.
    ///
    /// <b>AllowCredentials is mandatory here, and it is what forces the rest of the shape.</b>
    /// The refresh token lives in an HttpOnly cookie, so the browser must be permitted to send
    /// it — and a policy that allows credentials may NOT use a wildcard origin. Origins are
    /// therefore listed explicitly, from configuration.
    ///
    /// In development the tenant subdomains are also allowed, because ten1.localhost and
    /// localhost are different origins to a browser even though they are the same machine.
    /// </summary>
    private static IServiceCollection AddApiCors(
        this IServiceCollection services, IConfiguration configuration)
    {
        var client = configuration.GetSection(ClientAppSettings.SectionName).Get<ClientAppSettings>()
                     ?? new ClientAppSettings();

        var tenancy = configuration.GetSection(TenancySettings.SectionName).Get<TenancySettings>()
                      ?? new TenancySettings();

        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(client.BaseUrl))
        {
            origins.Add(client.BaseUrl.TrimEnd('/'));
        }

        foreach (var origin in client.AllowedOrigins.Where(o => !string.IsNullOrWhiteSpace(o)))
        {
            origins.Add(origin.TrimEnd('/'));
        }

        services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
        {
            policy
                .WithOrigins([.. origins])

                // Any subdomain of the root domain is an Organisation, and there is no list of
                // them at startup — they are created at runtime. Matching the pattern is the
                // only workable rule, and it is still far narrower than a wildcard.
                .SetIsOriginAllowedToAllowWildcardSubdomains()
                .SetIsOriginAllowed(origin => IsAllowedOrigin(origin, origins, tenancy))
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()

                // Correlation id and the pagination headers are custom, so the browser hides
                // them from JavaScript unless they are named here.
                .WithExposedHeaders(
                    "X-Correlation-Id",
                    "X-Total-Count",
                    "X-Page-Number",
                    "X-Page-Size",
                    "Content-Disposition");
        }));

        return services;
    }

    private static bool IsAllowedOrigin(
        string origin, HashSet<string> configured, TenancySettings tenancy)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        if (configured.Contains(origin.TrimEnd('/')))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;

        // The platform hosts themselves.
        if (tenancy.PlatformHosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // ten1.ngoplanet.com and friends: one label in front of the root domain, and no deeper.
        // "evil.ngoplanet.com.attacker.net" fails because the check is on the SUFFIX with the
        // separating dot, not on a substring.
        var suffix = "." + tenancy.RootDomain;

        return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
               && host.Length > suffix.Length
               && !host[..^suffix.Length].Contains('.', StringComparison.Ordinal);
    }

    // =================================================================================
    // Swagger
    // =================================================================================

    private static IServiceCollection AddApiSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "YDot IAM API",
                Version = "v1",
                Description =
                    "Identity and access management for the YDot platform.\n\n" +
                    "**Tenancy.** A BusinessUnit is the platform root; each Organisation is a " +
                    "Tenant on its own subdomain. The Organisation a request operates in comes " +
                    "from the signed token or the host name — never from a body, a query string " +
                    "or a header.\n\n" +
                    "**SuperAdmin** operates at global scope with no Organisation of its own and " +
                    "selects one per session; selecting does not change the account's ownership.\n\n" +
                    "**Envelope.** Every response is `{ success, message, data, errors, " +
                    "errorCode, timestamp }`, including failures."
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description =
                    "Paste the access token from POST /api/v1/users/sign-in. " +
                    "Swagger adds the \"Bearer \" prefix itself."
            });

            options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer")] = []
            });

            // The development Organisation header, offered on every operation so a SuperAdmin
            // can exercise a Tenant endpoint from Swagger without editing their hosts file.
            // The server ignores it off loopback, which is why advertising it here is safe.
            options.OperationFilter<TenantHeaderOperationFilter>();

            // Two controllers publish routes outside their own prefix (permissions, navigation,
            // business-units). Without a resolver Swagger sees two actions on one id and throws.
            options.CustomOperationIds(description =>
            {
                var routeValues = description.ActionDescriptor.RouteValues;

                return routeValues.TryGetValue("action", out var action) && action is not null
                    ? routeValues.TryGetValue("controller", out var controller) && controller is not null
                        ? $"{controller}_{action}"
                        : action
                    : null;
            });

            options.ResolveConflictingActions(descriptions => descriptions.First());
        });

        return services;
    }

    /// <summary>
    /// Writes an authentication or authorisation failure in the SAME envelope every other
    /// response uses, correlation id included, so the Angular error interceptor has one shape
    /// to handle rather than three.
    /// </summary>
    private static Task WriteEnvelopeAsync(HttpContext context, int statusCode, Error error)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var correlationId = context.Items.TryGetValue("CorrelationId", out var value)
            ? value as string
            : null;

        return context.Response.WriteAsJsonAsync(
            ApiResponse.Fail(error, correlationId),
            EnvelopeJsonOptions);
    }

    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
