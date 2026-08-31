using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Services;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Infrastructure.Authorization;
using YDot.IAM.Infrastructure.Multitenancy;
using YDot.IAM.Infrastructure.Persistence;
using YDot.IAM.Infrastructure.Persistence.ReadServices;
using YDot.IAM.Infrastructure.Persistence.Repositories;
using YDot.IAM.Infrastructure.Persistence.Seed;
using YDot.IAM.Infrastructure.Security;
using YDot.IAM.Infrastructure.Services;
using YDot.IAM.Infrastructure.Services.Email;

namespace YDot.IAM.Infrastructure.ServiceContainer;

/// <summary>
/// Registers everything the infrastructure layer owns: the PostgreSQL DbContext, the
/// repositories, the read services, the security primitives, and the authorization handlers.
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
        // IAM SHARES THE DATABASE WITH DON, so it needs its OWN migrations history table.
        // With the default __EFMigrationsHistory the two services would read and write the
        // same list: EF would see the other service migration ids, report them as pending or
        // unknown, and "dotnet ef migrations list" would be wrong for both. The tables
        // themselves never clash because IAM owns iam_* and DON owns don_*.
        services.AddDbContext<IamDbContext>(options =>
        {
            options.UseNpgsql(
                    databaseSettings.ConnectionString,
                    npgsql => npgsql
                        .CommandTimeout(databaseSettings.CommandTimeoutSeconds)
                        .MigrationsHistoryTable("__ef_migrations_history_iam"))
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

        // A FACTORY as well as the scoped context.
        //
        // TenantResolver runs in the middleware pipeline BEFORE the request scope has a
        // resolved Organisation - it is the thing that resolves it. Injecting the scoped
        // IamDbContext there would be circular, because that context needs ITenantContext to
        // build its query filters. The factory gives the resolver a short-lived context of its
        // own with no such dependency.
        services.AddDbContextFactory<IamDbContext>(options =>
            options.UseNpgsql(
                    databaseSettings.ConnectionString,
                    npgsql => npgsql
                        .CommandTimeout(databaseSettings.CommandTimeoutSeconds)
                        .MigrationsHistoryTable("__ef_migrations_history_iam"))
                .UseSnakeCaseNamingConvention(),
            lifetime: ServiceLifetime.Scoped);

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<IamDbContext>());

        // ---- Multi-tenancy -------------------------------------------------------------------
        //
        // TenantContext is SCOPED and single-assignment: one instance per request, filled in
        // once by the middleware, and readable by everything downstream. It is registered
        // concretely as well as by interface so the middleware can call the internal setter.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());
        services.AddScoped<TenantResolver>();
        services.AddMemoryCache();

        // ---- Repositories ------------------------------------------------------------------------
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IBusinessUnitRepository, BusinessUnitRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<ISecurityRepository, SecurityRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IMenuRepository, MenuRepository>();
        services.AddScoped<IOrganisationStructureRepository, OrganisationStructureRepository>();
        services.AddScoped<IGovernanceRepository, GovernanceRepository>();
        services.AddScoped<IBulkOperationRepository, BulkOperationRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<ILookupRepository, LookupRepository>();

        // The five global masters, migrated in from the standalone GlobalMaster service. One
        // repository for all of them - the generic half is closed over GlobalMasterEntity, so
        // every master gets the same scope rule with no per-entity copy to get wrong.
        services.AddScoped<IGlobalMasterRepository, GlobalMasterRepository>();

        // ---- Read services -----------------------------------------------------------------------------
        services.AddScoped<IUserReadService, UserReadService>();
        services.AddScoped<IOrganisationReadService, OrganisationReadService>();
        services.AddScoped<IRoleReadService, RoleReadService>();
        services.AddScoped<IAuditReadService, AuditReadService>();
        services.AddScoped<IGovernanceReadService, GovernanceReadService>();
        services.AddScoped<IBulkOperationReadService, BulkOperationReadService>();
        services.AddScoped<IGlobalMasterReadService, GlobalMasterReadService>();

        // ---- Security ------------------------------------------------------------------------------------
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        // Stateless, so singletons. The JWT service reads its settings through IOptions, which
        // is itself singleton-safe.
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<ITokenHasher, TokenHasher>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IExportService, CsvExportService>();
        services.AddSingleton<IUserAgentParser, UserAgentParser>();

        // ---- Application services ---------------------------------------------------------------------------
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IEffectiveAccessService, EffectiveAccessService>();
        services.AddScoped<ISessionTokenService, SessionTokenService>();
        services.AddScoped<IMfaChallengeService, MfaChallengeService>();
        services.AddScoped<IMfaEnrolmentService, MfaEnrolmentService>();
        services.AddScoped<IMenuBuilderService, MenuBuilderService>();

        // SINGLETON, unlike everything around it. The MinIO client holds a pooled HttpClient,
        // and building one per request is the classic way to exhaust sockets under load. It
        // carries no per-request state, so there is nothing to keep scoped.
        services.AddSingleton<IObjectStorage, MinioObjectStorage>();

        // ---- E-mail -------------------------------------------------------------------------------------------
        //
        // ONE POLICY, ONE TRANSPORT. EmailNotificationService owns the decisions that must not
        // vary by provider - suppressed when disabled, redirected to a single mailbox outside
        // production, retried, and never allowed to throw into a committed operation. Only the
        // wire protocol is chosen here.
        //
        // THAT TRANSPORT IS AN SMTP RELAY - Hostinger's by default, on port 465 with implicit TLS.
        // The Gmail App Password, the Elastic Email credential and the Resend API key that each
        // held this slot before are gone, and so is the EmailSettings:Provider switch that chose
        // between them: a second code path nothing configures is a second code path nobody tests.
        // Every one of those moves was configuration only. Point EmailSettings:SmtpHost at any
        // other relay to move providers again.
        //
        // PAY'S SENDER IS THE SAME TRANSPORT NOW, and had to become it for this move: one set of
        // environment variables configures both services, and System.Net.Mail cannot open an
        // implicit-TLS connection, so a 465 relay sent invitations and stalled every receipt.
        services.AddSingleton<EmailTemplateRenderer>();
        services.AddScoped<IEmailTransport, SmtpEmailTransport>();
        services.AddScoped<INotificationService, EmailNotificationService>();

        // ---- Authorization -------------------------------------------------------------------------------------
        //
        // The policy provider is what manufactures a policy for any permission code on demand,
        // so a new permission never needs a startup registration.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, SuperAdminAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, TenantAdminAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, TenantContextAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, RecentReauthenticationHandler>();
        services.AddScoped<IAuthorizationHandler, FullAccessTokenHandler>();
        services.AddScoped<IAuthorizationHandler, IndependentActorHandler>();

        // ---- Seeder ------------------------------------------------------------------------------------------------
        services.AddScoped<IamDbSeeder>();

        // Runs immediately after IamDbSeeder and depends on it: the platform masters need the
        // BusinessUnit that seeder creates.
        services.AddScoped<GlobalMasterSeeder>();

        return services;
    }

    /// <summary>
    /// The fixed named policies.
    ///
    /// Permission policies are NOT listed here — <see cref="PermissionPolicyProvider"/>
    /// creates them on demand from the <c>[HasPermission]</c> attribute, which is what keeps
    /// a hundred and thirty codes from becoming a hundred and thirty lines of configuration.
    /// </summary>
    public static AuthorizationOptions AddIamPolicies(this AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // DENY BY DEFAULT. Every endpoint needs an authenticated caller unless it opts out
        // with [AllowAnonymous], so forgetting an attribute fails closed rather than open.
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new FullAccessTokenRequirement())
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

        options.AddPolicy(PolicyNames.TenantAdminOnly, policy =>
            policy.RequireAuthenticatedUser()
                .AddRequirements(new TenantAdminRequirement()));

        options.AddPolicy(PolicyNames.MfaCompleted, policy =>
            policy.RequireAuthenticatedUser()
                .RequireClaim(ClaimTypeNames.MfaCompleted, "true"));

        options.AddPolicy(PolicyNames.RecentlyReauthenticated, policy =>
            policy.RequireAuthenticatedUser()
                .AddRequirements(new RecentReauthenticationRequirement()));

        options.AddPolicy(PolicyNames.IndependentApprover, policy =>
            policy.RequireAuthenticatedUser()
                .AddRequirements(new IndependentActorRequirement("id")));

        options.AddPolicy(PolicyNames.FullAccessToken, policy =>
            policy.RequireAuthenticatedUser()
                .AddRequirements(new FullAccessTokenRequirement()));

        return options;
    }
}
