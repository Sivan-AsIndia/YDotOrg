using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Settings;
using YDots.CAM.Infrastructure.Authorization;
using YDots.CAM.Infrastructure.Multitenancy;
using YDots.CAM.Infrastructure.Persistence;
using YDots.CAM.Infrastructure.Persistence.ReadServices;
using YDots.CAM.Infrastructure.Persistence.Repositories;
using YDots.CAM.Infrastructure.Persistence.Seed;
using YDots.CAM.Infrastructure.Security;
using YDots.CAM.Infrastructure.Services;

namespace YDots.CAM.Infrastructure.ServiceContainer;

/// <summary>
/// Registers everything the infrastructure layer owns: the PostgreSQL DbContext, the
/// repositories, the read services, the tenancy primitives and the authorization handlers.
///
/// THIS USED TO LIVE IN Program.cs as twelve loose <c>AddScoped</c> calls between the CORS
/// setup and the middleware. Moving it here means the API project no longer names a single
/// repository or DbContext type, which is what the layer boundary was supposed to mean, and it
/// puts the file where IAM and DON keep theirs.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var databaseSettings = configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>()
                               ?? new DatabaseSettings();

        // ---- EF Core with PostgreSQL and snake_case column names -----------------------
        //
        // CAM SHARES THE DATABASE WITH IAM AND DON, so it needs its OWN migrations history
        // table. With the default __EFMigrationsHistory the three services would read and write
        // the same list: EF would see the other services' migration ids, report them as pending
        // or unknown, and "dotnet ef migrations list" would be wrong for all three. The tables
        // themselves never clash because IAM owns iam_* and gm_*, DON owns don_* and CAM owns
        // cam_*.
        services.AddDbContext<CampaignDbContext>(options =>
        {
            options.UseNpgsql(
                    databaseSettings.ConnectionString,
                    npgsql => npgsql
                        .CommandTimeout(databaseSettings.CommandTimeoutSeconds)
                        .MigrationsHistoryTable("__ef_migrations_history_cam"))
                .UseSnakeCaseNamingConvention();

            if (databaseSettings.EnableSensitiveDataLogging)
            {
                options.EnableSensitiveDataLogging();
            }

            if (databaseSettings.EnableDetailedErrors)
            {
                options.EnableDetailedErrors();
            }
        });

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<CampaignDbContext>());

        // ---- Multi-tenancy -------------------------------------------------------------------
        //
        // TenantContext is SCOPED and single-assignment: one instance per request, filled in
        // once by the middleware, readable by everything downstream. It is registered
        // concretely as well as by interface so the middleware can call the internal setter.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());

        // ---- Repositories ------------------------------------------------------------------------
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<ITrackingAssetRepository, TrackingAssetRepository>();
        services.AddScoped<ICampaignReadinessRepository, CampaignReadinessRepository>();
        services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();
        services.AddScoped<IBudgetTargetPlanRepository, BudgetTargetPlanRepository>();
        services.AddScoped<IAttributionCorrectionRepository, AttributionCorrectionRepository>();

        // ---- Read services -----------------------------------------------------------------------------
        services.AddScoped<ICampaignReadService, CampaignReadService>();
        services.AddScoped<ITrackingAssetReadService, TrackingAssetReadService>();
        services.AddScoped<ICampaignReadinessReadService, CampaignReadinessReadService>();
        services.AddScoped<IBudgetTargetPlanReadService, BudgetTargetPlanReadService>();
        services.AddScoped<IAttributionReadService, AttributionReadService>();

        // ---- Security ------------------------------------------------------------------------------------
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        // ---- Supporting services ---------------------------------------------------------------------------
        //
        // The clock, the CSV writer and the reference generator hold no per-request state, so
        // they are singletons. The audit writer is scoped because it writes into the request's
        // DbContext - the audit row has to commit in the same transaction as the change it
        // records, or the two can disagree.
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<ICsvExportService, CsvExportService>();
        services.AddSingleton<ITrackingReferenceGenerator, TrackingReferenceGenerator>();
        services.AddScoped<IAuditWriter, AuditWriter>();

        // SCOPED, because it reads on the request's own DbContext connection and inside its
        // transaction. A singleton would open a second connection and read outside any transaction
        // in progress, so a figure read here would not see writes the same request had just made.
        services.AddScoped<IFinancialDirectory, FinancialDirectory>();
        services.AddScoped<IPeopleDirectory, PeopleDirectory>();
        services.AddScoped<IGeographyDirectory, GeographyDirectory>();

        // ---- Authorization -----------------------------------------------------------------------------------
        //
        // The policy PROVIDER is what turns [HasPermission("cam.campaigns.approve")] into a
        // policy on demand, so a new permission never needs a startup registration.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, TenantContextAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, SuperAdminAuthorizationHandler>();

        // ---- Background work -----------------------------------------------------------------------------------
        //
        // THE THING THAT MAKES "STARTS ON ITS START DATE" TRUE. LifecycleActivation.Auto has been
        // on the campaign and on the wizard since the module was written and nothing acted on it,
        // so a scheduled campaign sat in Scheduled until somebody noticed. It takes its own scope
        // per sweep - the DbContext is scoped, and a singleton holding one would accumulate every
        // entity it ever tracked.
        services.AddHostedService<CampaignActivationService>();

        // ---- Seeding -------------------------------------------------------------------------------------------
        services.AddScoped<CampaignDbSeeder>();

        return services;
    }
}
