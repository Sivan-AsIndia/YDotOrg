using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YDots.CAM.Application.Common.Settings;
using YDots.CAM.Application.Features.CampaignReadiness.Commands.ManageReadiness;
using YDots.CAM.Application.Features.CampaignReadiness.Queries.ReadinessQueries;
using YDots.CAM.Application.Features.Campaigns.Commands.CampaignLifecycle;
using YDots.CAM.Application.Features.Campaigns.Commands.ManageCampaign;
using YDots.CAM.Application.Features.Campaigns.Queries.CampaignQueries;
using YDots.CAM.Application.Features.ReferenceData.Queries;
using YDots.CAM.Application.Features.TrackingAssets.Commands.ManageTrackingAsset;
using YDots.CAM.Application.Features.TrackingAssets.Queries.TrackingAssetQueries;
using YDots.CAM.Application.Features.Attribution.Commands.ManageAttribution;
using YDots.CAM.Application.Features.Attribution.Queries.AttributionQueries;
using YDots.CAM.Application.Features.BudgetTargetPlans.Commands.ManageBudgetPlan;
using YDots.CAM.Application.Features.BudgetTargetPlans.Queries.BudgetPlanQueries;

namespace YDots.CAM.Application.ServiceContainer;

/// <summary>
/// Registers everything the application layer owns: the option-pattern settings, the
/// FluentValidation validators, and every CQRS handler.
///
/// WHAT REPLACED MediatR. This file used to be a single
/// <c>services.AddMediatR(cfg =&gt; cfg.RegisterServicesFromAssembly(...))</c>, which found the
/// handlers by scanning for a marker interface and dispatched to them through a runtime
/// registry. Handlers are now registered by hand and injected directly into the controller that
/// uses them.
///
/// It is a few more lines, and it buys three things. The list below IS the inventory of what
/// this service can do, readable in one place. A handler somebody forgot to wire up fails at
/// STARTUP with a clear message rather than at the first request with a container resolution
/// error. And the path from a route to the code that answers it is one you can follow by
/// clicking, rather than one the container assembles at runtime.
///
/// THE FOLDER IS <c>ServiceContainer</c>, matching IAM and DON, so a developer moving between
/// the three services finds the composition root in the same place each time.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ---- Option pattern -------------------------------------------------------------
        //
        // Bound once here and injected as IOptions<T>, so nothing downstream reads
        // IConfiguration directly and no handler carries a magic string for a section name.
        services.Configure<DatabaseSettings>(configuration.GetSection(DatabaseSettings.SectionName));
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<CampaignSettings>(configuration.GetSection(CampaignSettings.SectionName));
        services.Configure<ClientAppSettings>(configuration.GetSection(ClientAppSettings.SectionName));

        // ---- FluentValidation ---------------------------------------------------------------
        //
        // Scanned rather than listed, unlike the handlers. A validator is found by the TYPE it
        // validates, so a missing one degrades to "no validation ran" rather than to a startup
        // failure - which means listing them by hand would buy none of the safety that listing
        // the handlers does.
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        // ---- Campaigns ---------------------------------------------------------------------------
        services.AddScoped<CampaignCommandHandler>();
        services.AddScoped<CampaignLifecycleCommandHandler>();
        services.AddScoped<CampaignQueryHandler>();

        // ---- Tracking assets -----------------------------------------------------------------------
        services.AddScoped<TrackingAssetCommandHandler>();
        services.AddScoped<TrackingAssetQueryHandler>();

        // ---- Campaign readiness ------------------------------------------------------------------------
        services.AddScoped<ReadinessCommandHandler>();
        services.AddScoped<ReadinessQueryHandler>();

        // ---- Reference data: channels, sources, mediums ---------------------------------------------------
        services.AddScoped<BudgetPlanCommandHandler>();
        services.AddScoped<BudgetPlanQueryHandler>();

        services.AddScoped<AttributionCommandHandler>();
        services.AddScoped<AttributionQueryHandler>();

        services.AddScoped<ReferenceDataQueryHandler>();

        return services;
    }
}
