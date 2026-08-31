using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Settings;
using YDot.PAY.Infrastructure.Authorization;
using YDot.PAY.Infrastructure.Gateway;
using YDot.PAY.Infrastructure.Identity;
using YDot.PAY.Infrastructure.Multitenancy;
using YDot.PAY.Infrastructure.Persistence;
using YDot.PAY.Infrastructure.Persistence.ReadServices;
using YDot.PAY.Infrastructure.Persistence.Repositories;
using YDot.PAY.Infrastructure.Persistence.Seed;
using YDot.PAY.Infrastructure.Security;
using YDot.PAY.Infrastructure.Services;

namespace YDot.PAY.Infrastructure.ServiceContainer;

/// <summary>
/// Registers everything the infrastructure layer owns: the PostgreSQL DbContext, the
/// repositories, the read services, the tenancy primitives, the gateway adapter and the
/// authorization handlers.
///
/// THE API PROJECT NAMES NO REPOSITORY AND NO DbContext TYPE, which is what the layer boundary
/// is supposed to mean - and is why this file exists here rather than as thirty loose AddScoped
/// calls in Program.cs.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var databaseSettings = configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>()
                               ?? new DatabaseSettings();

        // ---- EF Core with PostgreSQL and snake_case column names -----------------------
        //
        // PAY SHARES THE DATABASE WITH IAM, DON AND CAM, so it needs its OWN migrations history
        // table. With the default __EFMigrationsHistory the four services would read and write
        // the same list: EF would see the other services' migration ids, report them as pending
        // or unknown, and "dotnet ef migrations list" would be wrong for all four. The tables
        // themselves never clash because IAM owns iam_* and gm_*, DON owns don_*, CAM owns cam_*
        // and PAY owns pay_*.
        services.AddDbContext<PaymentDbContext>(options =>
        {
            options.UseNpgsql(
                    databaseSettings.ConnectionString,
                    npgsql => npgsql
                        .CommandTimeout(databaseSettings.CommandTimeoutSeconds)
                        .MigrationsHistoryTable("__ef_migrations_history_pay"))
                .UseSnakeCaseNamingConvention();

            if (databaseSettings.EnableSensitiveDataLogging)
            {
                // NEVER IN PRODUCTION FOR THIS SERVICE. Sensitive data logging writes parameter
                // VALUES into the log, and the parameters here are donor names, e-mail addresses
                // and tax identifiers.
                options.EnableSensitiveDataLogging();
            }

            if (databaseSettings.EnableDetailedErrors)
            {
                options.EnableDetailedErrors();
            }
        });

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<PaymentDbContext>());

        // ---- Multi-tenancy -------------------------------------------------------------------
        //
        // TenantContext is SCOPED and single-assignment: one instance per request, filled in once
        // by the middleware, readable by everything downstream. It is registered concretely as
        // well as by interface so the middleware can call the internal setters - which is also
        // what stops an application handler reaching them.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());

        // ---- Repositories ------------------------------------------------------------------------
        services.AddScoped<IDonationRepository, DonationRepository>();
        services.AddScoped<IPaymentEventRepository, PaymentEventRepository>();
        services.AddScoped<IReceiptRepository, ReceiptRepository>();
        services.AddScoped<IRefundRepository, RefundRepository>();
        services.AddScoped<IGatewayAccountRepository, GatewayAccountRepository>();

        // ---- Read services -----------------------------------------------------------------------------
        services.AddScoped<IDonationIntentReadService, DonationIntentReadService>();
        services.AddScoped<IDonationReadService, DonationReadService>();
        services.AddScoped<IPaymentEventReadService, PaymentEventReadService>();
        services.AddScoped<IReceiptReadService, ReceiptReadService>();
        services.AddScoped<IRefundReadService, RefundReadService>();

        // ---- Security ------------------------------------------------------------------------------------
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        // ---- Cross-service seams ---------------------------------------------------------------------------
        //
        // Scoped, not singleton: each reads through the request's own DbContext connection, so a
        // lookup taken inside a donation transaction sees that transaction's writes and a donor
        // created alongside a donation commits with it.
        services.AddScoped<IDonorDirectory, DonorDirectory>();
        services.AddScoped<ICampaignDirectory, CampaignDirectory>();
        services.AddScoped<IIdentityAccountService, IdentityAccountService>();

        // ---- Supporting services ---------------------------------------------------------------------------
        //
        // The clock, the CSV writer and the reference generator hold no per-request state, so
        // they are singletons. The audit writer is SCOPED because it writes into the request's
        // DbContext - the audit row has to commit in the same transaction as the change it
        // records, or a refund can happen with no record of who approved it.
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<ICsvExportService, CsvExportService>();
        services.AddSingleton<IReferenceGenerator, ReferenceGenerator>();

        // ONE SENDER, an SMTP relay - Elastic Email's by default, matching IAM so that one set of
        // environment variables configures mail for the whole platform. The Gmail App Password
        // and the Resend HTTPS sender that preceded it are gone, along with the
        // EmailSettings:Provider switch that chose between them.
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        services.AddSingleton<IReceiptDocumentStore, FileSystemReceiptDocumentStore>();
        services.AddSingleton<IReceiptDocumentService, ReceiptDocumentService>();
        services.AddScoped<IAuditWriter, AuditWriter>();

        // ---- Payment gateway ---------------------------------------------------------------------------------
        //
        // The credential resolver is a SINGLETON reading configuration; the gateway itself is too,
        // because it holds no per-request state and takes its HttpClient from the factory.
        //
        // AddHttpClient registers the factory and, more importantly, gives both clients a
        // rotating handler pool: a raw `new HttpClient()` per call exhausts sockets under load,
        // and a static one never notices DNS changing - which on a payment provider's endpoint
        // means every donation failing until the process is restarted.
        services.AddSingleton<IGatewayCredentialResolver, ConfigurationGatewayCredentialResolver>();
        services.AddHttpClient(HostedCheckoutGateway.HttpClientName);
        services.AddHttpClient(RazorpayGateway.HttpClientName);
        services.AddHttpClient(IdentityAccountService.HttpClientName, client =>
        {
            var identity = configuration.GetSection(IdentityIntegrationSettings.SectionName)
                               .Get<IdentityIntegrationSettings>()
                           ?? new IdentityIntegrationSettings();

            if (!string.IsNullOrWhiteSpace(identity.BaseUrl))
            {
                client.BaseAddress = new Uri(
                    identity.BaseUrl.EndsWith('/') ? identity.BaseUrl : identity.BaseUrl + "/");
            }

            client.Timeout = TimeSpan.FromSeconds(
                identity.TimeoutSeconds > 0 ? identity.TimeoutSeconds : 10);
        });
        // ONE INTERFACE, SEVERAL PROVIDERS, CHOSEN PER ORGANISATION. The concrete adapters are
        // registered by their own type and the ROUTER is what the handlers receive; it reads
        // `PaymentGatewayAccount.GatewayName` and dispatches. Registering a provider directly as
        // IPaymentGateway - which is what this line used to do - makes every organisation on the
        // platform speak that one provider's protocol whatever their account says.
        services.AddSingleton<HostedCheckoutGateway>();
        services.AddSingleton<RazorpayGateway>();
        services.AddSingleton<IPaymentGateway, PaymentGatewayRouter>();

        // ---- Authorization -----------------------------------------------------------------------------------
        //
        // The policy PROVIDER is what turns [HasPermission("pay.refunds.approve")] into a policy
        // on demand, so a new permission never needs a startup registration.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, TenantContextAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, SuperAdminAuthorizationHandler>();

        // ---- Seeding -------------------------------------------------------------------------------------------
        services.AddScoped<PaymentDbSeeder>();

        return services;
    }
}
