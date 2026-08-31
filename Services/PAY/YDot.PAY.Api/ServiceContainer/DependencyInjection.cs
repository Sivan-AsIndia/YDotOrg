using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using YDot.PAY.Api.Filters;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Common.Settings;
using YDot.PAY.Infrastructure.Authorization;

namespace YDot.PAY.Api.ServiceContainer;

/// <summary>
/// Registers everything the API layer owns: controllers and their JSON shape, JWT validation,
/// the authorization policies, CORS and Swagger.
///
/// SWAGGER REPLACES SCALAR. The brief asks for Swagger, and there is a practical reason beyond
/// that: IAM, DON and CAM all expose Swagger, and four services in one solution documented through
/// two different UIs is a small tax paid on every visit.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Named so <c>UseCors</c> and <c>AddCors</c> cannot drift apart.</summary>
    public const string CorsPolicyName = "YDotClient";

    public static IServiceCollection AddApiServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
        var client = configuration.GetSection(ClientAppSettings.SectionName).Get<ClientAppSettings>()
                     ?? new ClientAppSettings();

        // ---- Controllers and JSON ---------------------------------------------------------
        services
            .AddControllers(options =>
            {
                // Runs FluentValidation for every action argument that has a validator. A filter
                // rather than a call inside each handler, because a handler can forget.
                options.Filters.Add<FluentValidationFilter>();
            })
            .AddJsonOptions(options =>
            {
                // Enums as camelCase STRINGS, matching IAM and DON. A numeric enum on the wire
                // means the client hard-codes ordinals, and inserting a value into the middle of
                // an enum then silently re-points every stored number.
                options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.DefaultIgnoreCondition =
                    JsonIgnoreCondition.WhenWritingNull;
            })
            .ConfigureApiBehavior();

        services.AddScoped<FluentValidationFilter>();

        // ---- Authentication ------------------------------------------------------------------
        //
        // PAY NEVER ISSUES A TOKEN. It only validates the one IAM signed, which is why there is
        // no lifetime, refresh or rotation configuration here - none of those are PAY decisions.
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

                    // The framework default is FIVE MINUTES, which quietly extends every token
                    // past its stated life. Thirty seconds covers real drift between two
                    // containers and nothing more.
                    ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),

                    // The claim IAM writes the user id into. Without this the framework looks for
                    // its own long-form URI claim type and UserId comes back empty - which would
                    // put Guid.Empty on every audit row.
                    NameClaimType = ClaimTypeNames.UserId,
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };

                // The 401 and 403 bodies are written by hand so they carry the SAME six-key
                // envelope as every other response. Without this the framework returns an empty
                // body and the Angular interceptor has nothing to read.
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async challengeContext =>
                    {
                        challengeContext.HandleResponse();

                        await WriteEnvelopeAsync(
                            challengeContext.HttpContext,
                            Error.Unauthorised("Your session has ended. Sign in again."));
                    },

                    OnForbidden = forbiddenContext => WriteEnvelopeAsync(
                        forbiddenContext.HttpContext,
                        Error.Forbidden())
                };
            });

        // ---- Authorization ---------------------------------------------------------------------
        services.AddAuthorization(options =>
        {
            // DENY BY DEFAULT. Every endpoint needs an authenticated caller unless it opts out
            // with [AllowAnonymous], so a forgotten attribute fails closed rather than open.
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy(PolicyNames.ActiveUserOnly, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireClaim(ClaimTypeNames.UserStatus, "Active"));

            options.AddPolicy(PolicyNames.TenantContextRequired, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new TenantContextRequirement()));

            options.AddPolicy(PolicyNames.SuperAdminOnly, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new SuperAdminRequirement()));
        });

        // ---- CORS ---------------------------------------------------------------------------------
        //
        // AN EXPLICIT ORIGIN LIST, NOT AllowAnyOrigin. The client sends a bearer token and
        // credentials, and a browser refuses to send credentials to a wildcard origin - so the
        // previous AllowAnyOrigin policy could not have worked with authentication even if it
        // were safe. Being explicit is also what stops another site calling this API with a
        // token it managed to obtain.
        services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
        {
            if (client.AllowedOrigins.Count > 0)
            {
                policy.WithOrigins([.. client.AllowedOrigins])
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
                    .WithExposedHeaders("X-Correlation-Id", "X-Export-Reference");
            }
        }));

        // ---- Swagger -------------------------------------------------------------------------------
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "YDot Donations and Payments API",
                Version = "v1",
                Description =
                    "Donation intents, payments, receipts, refunds and chargebacks.\n\n"
                    + "MOST ENDPOINTS ARE SCOPED to the organisation named in the caller's "
                    + "token. The exceptions are under /api/public and /api/webhooks: those "
                    + "serve donors holding a payment link and payment providers posting "
                    + "callbacks, neither of which has a token, and both resolve their "
                    + "organisation from the unguessable reference they present."
            });

            // The Authorize button. Without it every protected endpoint answers 401 from the
            // Swagger UI and the page is useless for anything but reading the schema.
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the access token issued by the IAM sign-in endpoint."
            });

            // The 2.x reference shape. The 1.x OpenApiReference form this replaces does not
            // exist any more, and Microsoft.OpenApi 2.7 is what IAM and DON are on.
            options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer")] = []
            });

            var xmlPath = Path.Combine(
                AppContext.BaseDirectory,
                $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");

            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }
        });

        return services;
    }

    /// <summary>
    /// Replaces the framework's automatic 400 with the standard envelope.
    ///
    /// WITHOUT THIS, a malformed body - a string where a Guid was expected - produces a
    /// ValidationProblemDetails, which is a completely different shape from every other response
    /// and which the brief explicitly rules out for now.
    /// </summary>
    private static IMvcBuilder ConfigureApiBehavior(this IMvcBuilder builder) =>
        builder.ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .SelectMany(entry => entry.Value!.Errors.Select(error => new ValidationError(
                        ToCamelCase(entry.Key),
                        string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "That value is not in the expected format."
                            : error.ErrorMessage)))
                    .ToList();

                var correlationId = context.HttpContext.Items["CorrelationId"] as string
                                    ?? context.HttpContext.TraceIdentifier;

                var failure = Error.Validation("Some of the details are not valid.", errors);

                return new ObjectResult(ApiResponse.Fail(failure, correlationId))
                {
                    StatusCode = failure.StatusCode
                };
            };
        });

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) || char.IsLower(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>Writes an error envelope directly, for the two JWT events that bypass MVC.</summary>
    private static async Task WriteEnvelopeAsync(HttpContext context, Error error)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var correlationId = context.Items["CorrelationId"] as string ?? context.TraceIdentifier;

        context.Response.StatusCode = error.StatusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(
            ApiResponse.Fail(error, correlationId),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            }));
    }
}
