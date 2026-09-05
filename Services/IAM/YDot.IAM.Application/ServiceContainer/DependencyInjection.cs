using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.Features.Authentication.Commands.AcceptInvitation;
using YDot.IAM.Application.Features.Authentication.Commands.MfaVerification;
using YDot.IAM.Application.Features.Authentication.Commands.PasswordRecovery;
using YDot.IAM.Application.Features.Authentication.Commands.Reauthenticate;
using YDot.IAM.Application.Features.Authentication.Commands.SelectTenant;
using YDot.IAM.Application.Features.Authentication.Commands.SignIn;
using YDot.IAM.Application.Features.Authentication.Commands.Tokens;
using YDot.IAM.Application.Features.Authentication.Queries.AuthenticationViews;
using YDot.IAM.Application.Features.Configuration.PaymentGateways;
using YDot.IAM.Application.Features.Configuration.PaymentGateways.Commands.ManagePaymentGatewayConfiguration;
using YDot.IAM.Application.Features.Configuration.PaymentGateways.Queries;
using YDot.IAM.Application.Features.GlobalMasters.Commands;
using YDot.IAM.Application.Features.GlobalMasters.Commands.ManageCity;
using YDot.IAM.Application.Features.GlobalMasters.Commands.ManageCountry;
using YDot.IAM.Application.Features.GlobalMasters.Commands.ManageCurrency;
using YDot.IAM.Application.Features.GlobalMasters.Commands.ManageStateProvince;
using YDot.IAM.Application.Features.GlobalMasters.Commands.ManageTimeZone;
using YDot.IAM.Application.Features.GlobalMasters.Queries;
using YDot.IAM.Application.Features.Governance.Commands.AccessRequests;
using YDot.IAM.Application.Features.Governance.Commands.AccessReviews;
using YDot.IAM.Application.Features.Governance.Queries.GovernanceQueries;
using YDot.IAM.Application.Features.Menus.Commands.ManageMenu;
using YDot.IAM.Application.Features.Menus.Queries.Navigation;
using YDot.IAM.Application.Features.MyProfile;
using YDot.IAM.Application.Features.MySecurity;
using YDot.IAM.Application.Features.Organisations.Commands.ManageOrganisation;
using YDot.IAM.Application.Features.Organisations.Queries.OrganisationQueries;
using YDot.IAM.Application.Features.ReferenceData.Queries;
using YDot.IAM.Application.Features.Roles.Commands.ManageRole;
using YDot.IAM.Application.Features.Roles.Queries.RoleQueries;
using YDot.IAM.Application.Features.Users.Commands.BulkUserAdministration;
using YDot.IAM.Application.Features.Users.Commands.CreateUser;
using YDot.IAM.Application.Features.Users.Commands.LoginIdentifierChange;
using YDot.IAM.Application.Features.Users.Commands.UserAccess;
using YDot.IAM.Application.Features.Users.Commands.UserLifecycle;
using YDot.IAM.Application.Features.Users.Commands.UserSecurity;
using YDot.IAM.Application.Features.Users.Queries.UserQueries;

namespace YDot.IAM.Application.ServiceContainer;

/// <summary>
/// Registers everything the application layer owns: the option-pattern settings, the
/// FluentValidation validators, and every CQRS handler.
///
/// HANDLERS ARE REGISTERED BY HAND rather than by assembly scanning. It is a few more lines,
/// but the list below is also the inventory of what this service can do — and a handler
/// somebody forgot to wire up fails at STARTUP with a clear message rather than at the first
/// request with a container resolution error.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ---- Option pattern -------------------------------------------------------------
        services.Configure<DatabaseSettings>(configuration.GetSection(DatabaseSettings.SectionName));
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<SecuritySettings>(configuration.GetSection(SecuritySettings.SectionName));
        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        services.Configure<ClientAppSettings>(configuration.GetSection(ClientAppSettings.SectionName));
        services.Configure<TenancySettings>(configuration.GetSection(TenancySettings.SectionName));
        services.Configure<DocumentStorageSettings>(
            configuration.GetSection(DocumentStorageSettings.SectionName));
        services.Configure<SeedSettings>(configuration.GetSection(SeedSettings.SectionName));
        services.Configure<PaymentGatewaySettings>(
            configuration.GetSection(PaymentGatewaySettings.SectionName));

        // ---- FluentValidation ---------------------------------------------------------------
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        // ---- Authentication: IAM-AUTH-01 through IAM-AUTH-07 -----------------------------------
        services.AddScoped<SignInCommandHandler>();
        services.AddScoped<MfaVerificationCommandHandler>();
        services.AddScoped<TokenCommandHandler>();
        services.AddScoped<AcceptInvitationCommandHandler>();
        services.AddScoped<PasswordRecoveryCommandHandler>();
        services.AddScoped<ReauthenticationCommandHandler>();
        services.AddScoped<AuthenticationViewQueryHandler>();

        // ---- SuperAdmin organisation switching: section 13 ---------------------------------------
        services.AddScoped<SelectTenantCommandHandler>();

        // ---- Organisations and BusinessUnit: sections 8, 32, 33 -------------------------------------
        services.AddScoped<CreateOrganisationCommandHandler>();
        services.AddScoped<OrganisationLifecycleCommandHandler>();
        services.AddScoped<OrganisationAssetCommandHandler>();
        services.AddScoped<Features.Organisations.Commands.DocumentSubmissions.DocumentSubmissionCommandHandler>();
        services.AddScoped<Features.Organisations.Queries.DocumentSubmissions.DocumentSubmissionQueryHandler>();
        services.AddScoped<OrganisationQueryHandler>();
        services.AddScoped<OrganisationStructureCommandHandler>();
        services.AddScoped<OrganisationStructureQueryHandler>();

        // ---- Users: IAM-USR-01 through IAM-USR-06 --------------------------------------------------
        services.AddScoped<CreateUserCommandHandler>();
        services.AddScoped<UserLifecycleCommandHandler>();
        services.AddScoped<UserSecurityCommandHandler>();
        services.AddScoped<UserAccessCommandHandler>();
        services.AddScoped<LoginIdentifierChangeCommandHandler>();
        services.AddScoped<BulkUserAdministrationCommandHandler>();
        services.AddScoped<UserQueryHandler>();

        // ---- Roles and permissions -------------------------------------------------------------------
        services.AddScoped<RoleCommandHandler>();
        services.AddScoped<RoleQueryHandler>();

        // ---- Dynamic navigation ------------------------------------------------------------------------
        services.AddScoped<MenuCommandHandler>();
        services.AddScoped<NavigationQueryHandler>();

        // ---- Access governance ---------------------------------------------------------------------------
        services.AddScoped<AccessRequestCommandHandler>();
        services.AddScoped<AccessReviewCommandHandler>();
        services.AddScoped<GovernanceQueryHandler>();

        // ---- Self-service profile and security ---------------------------------------------------------------
        services.AddScoped<MyProfileFeatureHandler>();
        services.AddScoped<MySecurityFeatureHandler>();

        // ---- Reference data ----------------------------------------------------------------------------------
        services.AddScoped<ReferenceDataQueryHandler>();

        // ---- Global masters: migrated in from the standalone GlobalMaster service ----------------------------
        //
        // The write guard is registered alongside the handlers rather than in the
        // infrastructure layer, because it is application policy - who may edit a shared row -
        // and not a persistence concern. It is SCOPED because it reads the request's
        // ITenantContext, which is itself scoped and resolved per request.
        services.AddScoped<GlobalMasterWriteGuard>();
        services.AddScoped<CountryCommandHandler>();
        services.AddScoped<StateProvinceCommandHandler>();
        services.AddScoped<CityCommandHandler>();
        services.AddScoped<CurrencyCommandHandler>();
        services.AddScoped<TimeZoneCommandHandler>();
        services.AddScoped<GlobalMasterQueryHandler>();

        // ---- Configuration: payment gateways ---------------------------------------------------------------
        //
        // THE SCOPE OBJECT IS REGISTERED HERE, NOT IN INFRASTRUCTURE, for the same reason
        // GlobalMasterWriteGuard is: it is application policy - who may configure whose merchant
        // account - and not a persistence concern. Scoped, because it reads the request's
        // ITenantContext and ICurrentUser, both of which are themselves per-request.
        services.AddScoped<PaymentGatewayScope>();
        services.AddScoped<PaymentGatewayConfigurationCommandHandler>();
        services.AddScoped<PaymentGatewayConfigurationQueryHandler>();

        return services;
    }
}
