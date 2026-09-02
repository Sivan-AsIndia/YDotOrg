using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Application.Features.AssignmentBoard.Commands.RouteLeads;
using YDots.DON.Application.Features.AssignmentBoard.Queries.GetAssignmentBoard;
using YDots.DON.Application.Features.ConsentCentre.Commands.ManageConsent;
using YDots.DON.Application.Features.ConsentCentre.Queries.GetConsentCentre;
using YDots.DON.Application.Features.Donor360.Commands.CreateIntent;
using YDots.DON.Application.Features.Donor360.Queries.GetDonor360;
using YDots.DON.Application.Features.Donors.Commands.ManageDonor;
using YDots.DON.Application.Features.Donors.Queries.SearchDonors;
using YDots.DON.Application.Features.DuplicateReview.Commands.DecideDuplicate;
using YDots.DON.Application.Features.DuplicateReview.Queries.GetDuplicateReview;
using YDots.DON.Application.Features.FollowUpPlanner.Commands.PlanFollowUp;
using YDots.DON.Application.Features.FollowUpPlanner.Queries.GetFollowUpPlanner;
using YDots.DON.Application.Features.IdentityVerification.Commands.VerifyIdentity;
using YDots.DON.Application.Features.IdentityVerification.Queries.GetVerifications;
using YDots.DON.Application.Features.LeadCapture.Commands.CaptureLead;
using YDots.DON.Application.Features.LeadCapture.Queries.GetLeadCapture;
using YDots.DON.Application.Features.LeadWorkQueue.Commands.LeadWorkQueueActions;
using YDots.DON.Application.Features.CommunicationTimeline.Queries;
using YDots.DON.Application.Features.LeadWorkQueue.Queries.GetLeadWorkQueue;
using YDots.DON.Application.Features.Navigation.Queries.GetDonorMenu;
using YDots.DON.Application.Features.ReferenceData.Queries.GetReferenceData;

namespace YDots.DON.Application.ServiceContainer;

/// <summary>
/// Registers everything the application layer owns: the option-pattern settings, the
/// FluentValidation validators and every CQRS handler.
///
/// Handlers are registered by hand rather than by assembly scanning. It is a few more lines,
/// but the list below is also the inventory of what this service can do, and a handler that
/// somebody forgot to wire up fails at startup instead of at the first request.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // ---- Option pattern -----------------------------------------------------------------
        services.Configure<DatabaseSettings>(configuration.GetSection(DatabaseSettings.SectionName));
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<ClientAppSettings>(configuration.GetSection(ClientAppSettings.SectionName));
        services.Configure<DonorSettings>(configuration.GetSection(DonorSettings.SectionName));
        services.Configure<SeedSettings>(configuration.GetSection(SeedSettings.SectionName));

        // ---- FluentValidation -----------------------------------------------------------------
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        // ---- Donor resource: the section 7 use-case inventory -----------------------------------
        services.AddScoped<DonorCommandHandler>();
        services.AddScoped<DonorQueryHandler>();

        // ---- SCR-DON-001 Lead work queue ---------------------------------------------------------
        services.AddScoped<LeadWorkQueueQueryHandler>();
        services.AddScoped<CommunicationTimelineQueryHandler>();
        services.AddScoped<LeadWorkQueueCommandHandler>();

        // ---- SCR-DON-002 Lead capture -------------------------------------------------------------
        services.AddScoped<GetLeadCaptureQueryHandler>();
        services.AddScoped<LeadCaptureCommandHandler>();

        // ---- SCR-DON-003 Donor 360 ------------------------------------------------------------------
        services.AddScoped<Donor360QueryHandler>();
        services.AddScoped<CreateIntentCommandHandler>();

        // ---- SCR-DON-004 Duplicate review --------------------------------------------------------------
        services.AddScoped<DuplicateReviewQueryHandler>();
        services.AddScoped<DuplicateReviewCommandHandler>();

        // ---- SCR-DON-005 Consent and preference centre -----------------------------------------------------
        services.AddScoped<ConsentCentreQueryHandler>();
        services.AddScoped<ConsentCommandHandler>();

        // ---- SCR-DON-006 Assignment board ----------------------------------------------------------------------
        services.AddScoped<AssignmentBoardQueryHandler>();
        services.AddScoped<AssignmentBoardCommandHandler>();

        // ---- DON-UI-07 Donor identity verification -------------------------------------------------------------
        services.AddScoped<IdentityVerificationQueryHandler>();
        services.AddScoped<IdentityVerificationCommandHandler>();

        // ---- DON-UI-08 Follow-up planner --------------------------------------------------------------------------
        services.AddScoped<FollowUpPlannerQueryHandler>();
        services.AddScoped<FollowUpCommandHandler>();

        // ---- Navigation and reference data --------------------------------------------------------------------------
        services.AddScoped<GetDonorMenuQueryHandler>();
        services.AddScoped<ReferenceDataQueryHandler>();

        return services;
    }
}
