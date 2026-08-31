using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using YDots.DON.Api.Filters;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Infrastructure.ServiceContainer;

namespace YDots.DON.Api.ServiceContainer;

/// <summary>
/// Registers everything the API layer owns: controllers with the validation filter, JWT bearer
/// authentication, the authorization policies, CORS and Swagger.
///
/// The key point about this file: DON validates a token it did not issue. Issuer, audience and
/// signing key all have to match the IAM values exactly, and there is no sign-in endpoint here
/// at all — the browser gets its token from IAM and simply sends it on to DON.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException($"Missing configuration section '{JwtSettings.SectionName}'.");

        if (string.IsNullOrWhiteSpace(jwtSettings.SigningKey) || jwtSettings.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "JwtSettings:SigningKey must be at least 32 characters and must match the key IAM signs with.");
        }

        services.AddControllers(options => options.Filters.Add<FluentValidationFilter>())
            .AddJsonOptions(options =>
            {
                // JSON is camelCase, and enums travel as readable names rather than integers.
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        // A body that cannot even be bound uses the same envelope as everything else, so the
        // Angular error interceptor never needs a second code path.
        services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .SelectMany(entry => entry.Value!.Errors.Select(error =>
                        new ValidationError(entry.Key, string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "The value could not be read."
                            : error.ErrorMessage)))
                    .ToList();

                var failure = Error.Validation("The request could not be read.", errors);
                var correlationId = context.HttpContext.Items["CorrelationId"] as string ?? context.HttpContext.TraceIdentifier;

                return new Microsoft.AspNetCore.Mvc.ObjectResult(ApiResponse.Fail(failure, correlationId))
                {
                    StatusCode = failure.StatusCode
                };
            };
        });

        // ---- JWT bearer authentication -------------------------------------------------------
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(jwtSettings.ClockSkewSeconds),
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                    NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier
                };

                // A rejected token returns the same envelope as every other failure.
                options.Events = new JwtBearerEvents
                {
                    // No token, or the token was not accepted at all.
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();

                        if (context.Response.HasStarted)
                        {
                            return;
                        }

                        var error = Error.Unauthorised();
                        var correlationId = context.HttpContext.Items["CorrelationId"] as string ?? context.HttpContext.TraceIdentifier;

                        context.Response.StatusCode = error.StatusCode;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(error, correlationId));
                    },

                    // The token is valid but the caller lacks the permission the endpoint asks for.
                    OnForbidden = async context =>
                    {
                        if (context.Response.HasStarted)
                        {
                            return;
                        }

                        var error = Error.Forbidden();
                        var correlationId = context.HttpContext.Items["CorrelationId"] as string ?? context.HttpContext.TraceIdentifier;

                        context.Response.StatusCode = error.StatusCode;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(error, correlationId));
                    },

                    // Expired or tampered. The Angular interceptor reads AUTHENTICATION_REQUIRED
                    // and calls the IAM refresh endpoint before retrying.
                    OnAuthenticationFailed = async context =>
                    {
                        if (context.Response.HasStarted)
                        {
                            return;
                        }

                        var error = Error.Unauthorised("The token is invalid or expired.");
                        var correlationId = context.HttpContext.Items["CorrelationId"] as string ?? context.HttpContext.TraceIdentifier;

                        context.Response.StatusCode = error.StatusCode;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(error, correlationId));
                    }
                };
            });

        // ---- Authorization: deny by default, plus the named policies ---------------------------
        services.AddAuthorizationBuilder();
        services.AddAuthorization(options => options.AddDonPolicies());

        // ---- CORS for the web and mobile clients -------------------------------------------------
        // AllowCredentials cannot be combined with the "*" wildcard, so the browser origins are
        // listed explicitly in ClientAppSettings.
        var clientApp = configuration.GetSection(ClientAppSettings.SectionName).Get<ClientAppSettings>() ?? new ClientAppSettings();
        var allowedOrigins = clientApp.AllowedOrigins.Length > 0 ? clientApp.AllowedOrigins : [clientApp.BaseUrl];

        services.AddCors(options => options.AddPolicy("YDotClients", policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders("X-Correlation-Id", "X-Export-Reference")));

        // ---- Swagger with a Bearer token box -------------------------------------------------------
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "YDot Donors API",
                Version = "v1",
                Description = "Section 04 - Donors and leads: lead capture, work queue, Donor 360, "
                              + "duplicate review, consent, assignment, identity verification and follow-ups."
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the access token returned by the IAM API sign-in endpoint. "
                              + "Swagger adds the Bearer prefix. This service issues no tokens of its own."
            });

            options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer")] = []
            });
        });

        return services;
    }
}
