using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Infrastructure.Authorization;
using YDots.DON.Infrastructure.Observability;
using YDots.DON.Infrastructure.Multitenancy;
using YDots.DON.Infrastructure.Persistence;
using YDots.DON.Infrastructure.Persistence.ReadServices;
using YDots.DON.Infrastructure.Persistence.Repositories;
using YDots.DON.Infrastructure.Persistence.Seed;
using YDots.DON.Infrastructure.Security;
using YDots.DON.Infrastructure.Services;

namespace YDots.DON.Infrastructure.ServiceContainer;

/// <summary>
/// Registers everything the infrastructure layer owns: the PostgreSQL DbContext, the
/// repositories, the supporting services and the authorization handlers.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseSettings = configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>() ?? new DatabaseSettings();

        // ---- EF Core with PostgreSQL and snake_case column names ---------------------------
        // DON shares the ydot database with IAM, so it needs its OWN migrations history table.
        // With the default __EFMigrationsHistory the two services would read and write the same
        // list: EF would see the other service's migration ids, report them as pending or
        // unknown, and "dotnet ef migrations list" would be wrong for both. The tables
        // themselves never clash because IAM owns iam_* and DON owns don_*.
        services.AddDbContext<DonDbContext>(options =>
            options.UseNpgsql(
                    databaseSettings.ConnectionString,
                    npgsql => npgsql
                        .CommandTimeout(databaseSettings.CommandTimeoutSeconds)
                        .MigrationsHistoryTable("__ef_migrations_history_don"))
                .UseSnakeCaseNamingConvention());

        // ---- Multi-tenancy -------------------------------------------------------------
        //
        // TenantContext is SCOPED and single-assignment: one instance per request, filled
        // in once by OrganisationResolutionMiddleware, readable by everything downstream.
        // It is registered concretely as well as by interface so the middleware can call
        // the internal setter, and so the DbContext can close over it for the query
        // filters.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<DonDbContext>());

        // ---- Repositories and read services -----------------------------------------------------
        services.AddScoped<IDonorRepository, DonorRepository>();
        services.AddScoped<IDonorReadService, DonorReadService>();
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<CampaignProjection>();
        services.AddScoped<PeopleDirectory>();
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<IConsentRepository, ConsentRepository>();
        services.AddScoped<IDonorMergeCaseRepository, DonorMergeCaseRepository>();
        services.AddScoped<IVerificationRepository, VerificationRepository>();
        services.AddScoped<IFollowUpRepository, FollowUpRepository>();
        services.AddScoped<IDonor360Repository, Donor360Repository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();

        // ---- Security and supporting services -------------------------------------------------------
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IExportService, CsvExportService>();
        services.AddSingleton<IChallengeCodeService, ChallengeCodeService>();
        // Singleton: a Meter is meant to live for the life of the process, not per request.
        services.AddSingleton<IDonorMetrics, DonorMetrics>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IReferenceNumberGenerator, ReferenceNumberGenerator>();

        // ---- Authorization: permission, claim and policy based --------------------------------------
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, SameOrganisationHandler>();
        services.AddScoped<IAuthorizationHandler, SegregationOfDutiesHandler>();

        // ---- Seeder ------------------------------------------------------------------------------------
        services.AddScoped<DonDbSeeder>();

        return services;
    }

    /// <summary>
    /// The fixed named policies. Permission policies are created on demand by
    /// <see cref="PermissionPolicyProvider"/>, so only these have to be listed.
    /// </summary>
    public static AuthorizationOptions AddDonPolicies(this AuthorizationOptions options)
    {
        // Deny by default: every endpoint needs an authenticated caller unless it opts out
        // with [AllowAnonymous]. Forgetting an attribute therefore fails closed, not open.
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        options.AddPolicy(PolicyNames.ActiveUserOnly, policy =>
            policy.RequireClaim(ClaimTypeNames.UserStatus, "Active"));

        options.AddPolicy(PolicyNames.SameOrganisation, policy =>
            policy.RequireAuthenticatedUser().AddRequirements(new SameOrganisationRequirement()));

        options.AddPolicy(PolicyNames.IndependentApprover, policy =>
            policy.RequireAuthenticatedUser().AddRequirements(new SegregationOfDutiesRequirement("id")));

        return options;
    }
}
