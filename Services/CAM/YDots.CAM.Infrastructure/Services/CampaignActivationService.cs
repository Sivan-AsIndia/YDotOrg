using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDots.CAM.Application.Common.Abstractions.Persistence.Seed;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Settings;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;
using YDots.CAM.Infrastructure.Persistence;

namespace YDots.CAM.Infrastructure.Services;

/// <summary>
/// Takes scheduled campaigns live on their start date.
///
/// IT DOES NOT CLOSE ANYTHING. A campaign that runs past its end date stays Active and is closed
/// through the two-step close, which needs a reason and a second person - the module brief has no
/// automatic closure, and inventing one here would end campaigns with money in flight and nobody
/// named as having decided to.
///
/// WHY THIS EXISTS AT ALL. <c>LifecycleActivation.Auto</c> has been on the campaign since the
/// module was written, the wizard has always offered it, and NOTHING ANYWHERE ACTED ON IT. A
/// campaign set to activate automatically simply sat in Scheduled forever, and the only way it
/// ever went live was somebody noticing and pressing Activate - which is the one thing the
/// setting says they will not have to do. The module brief is explicit that a campaign starts on
/// its start date; this is the thing that starts it.
///
/// A FAILED READINESS CHECK DOES NOT HOLD IT BACK, and that is deliberate rather than an
/// oversight. The brief says so directly: "if fail also campaign should start automatically on
/// that particular date". The checklist is a gate on the APPROVAL - it is what the readiness
/// screen and the manual Activate route consult before letting somebody launch an Approved
/// campaign early - but once a campaign has been approved and scheduled, the decision has been
/// taken by a person and the date is simply arriving. A sweep that silently refused to start an
/// approved campaign, at night, with no operator watching, would be the worst possible place to
/// discover an unpassed check.
///
/// IT BYPASSES THE ORGANISATION QUERY FILTER, using <c>IgnoreQueryFilters</c>, because there is
/// no request and therefore no resolved Organisation - the filter would match nothing and the
/// sweep would silently do nothing at all. That is one of the few legitimate bypasses in the
/// module, and it is safe here for the reason the DbContext comment gives: this code reads and
/// writes only the campaign's own status, never returns a row to a caller, and stamps each audit
/// row with the TenantId of the campaign it came from.
///
/// EVERY CAMPAIGN IS COMMITTED ON ITS OWN. One that fails - a concurrency clash with an operator
/// pressing Activate at the same moment - must not take the rest of the sweep down with it.
/// </summary>
public sealed class CampaignActivationService(
    IServiceScopeFactory scopeFactory,
    IOptions<CampaignSettings> options,
    ILogger<CampaignActivationService> logger) : BackgroundService
{
    private readonly CampaignSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.EnableAutomaticActivation)
        {
            logger.LogInformation(
                "Automatic campaign activation is switched off. Scheduled campaigns will only go "
                + "live when somebody activates them.");

            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Clamp(_settings.ActivationSweepMinutes, 1, 720));

        logger.LogInformation(
            "Automatic campaign activation is on, sweeping every {Minutes} minute(s).",
            interval.TotalMinutes);

        using var timer = new PeriodicTimer(interval);

        // Runs once at start-up before the first tick, so a service that has been down over a
        // start date catches up on restart rather than waiting out a whole interval.
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A sweep that throws must not kill the loop: the next one is fifteen minutes
                // away and would never come.
                logger.LogError(exception, "The campaign activation sweep failed. It will retry.");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    /// <summary>
    /// One pass: activate what is due, and close the window on what has run out.
    ///
    /// A SCOPE PER SWEEP, not per service. The DbContext, the clock and the tenant context are
    /// all scoped, and a singleton holding a DbContext for the life of the process would
    /// accumulate every entity it had ever tracked.
    /// </summary>
    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var today = clock.TodayUtc;
        var now = clock.UtcNow;

        // SCHEDULED AND SET TO ACTIVATE AUTOMATICALLY. Both halves are load-bearing.
        //
        // Scheduled, because an Approved campaign never reached a start date it was waiting for -
        // it was approved on or after the day - and an Active or Paused one is past this point.
        //
        // Auto, because LifecycleActivation is the operator's own answer to "who starts this".
        // Approval now leaves EVERY campaign Scheduled, whichever way that question was answered,
        // so without this filter the sweep would start campaigns whose detail screen says "Manual
        // activate" - overriding a choice the person made on the wizard's second step and making
        // the field decorative. A Manual campaign waits in Scheduled for somebody to press
        // Activate, and the readiness gate does not stand in their way when they do.
        var due = await context.Campaigns
            .IgnoreQueryFilters()
            .Where(campaign => campaign.Status == CampaignStatus.Scheduled)
            .Where(campaign => campaign.LifecycleActivation == LifecycleActivation.Auto)
            .Where(campaign => campaign.StartDate <= today)
            .ToListAsync(cancellationToken);

        foreach (var campaign in due)
        {
            // A campaign whose whole window slipped past while it sat in Scheduled has nothing
            // to activate INTO. Starting it would create a live campaign that is already over,
            // so it is left alone and reported - somebody has to decide whether to move its
            // dates or close it.
            if (campaign.EndDate < today)
            {
                logger.LogWarning(
                    "Campaign {CampaignId} ({CampaignCode}) was scheduled but its window closed on "
                    + "{EndDate}. It has not been activated.",
                    campaign.Id, campaign.Code, campaign.EndDate);

                continue;
            }

            await ActivateAsync(context, campaign, now, cancellationToken);
        }
    }

    private async Task ActivateAsync(
        CampaignDbContext context,
        Campaign campaign,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        campaign.Status = CampaignStatus.Active;

        context.CampaignLifecycleActions.Add(new CampaignLifecycleAction
        {
            CampaignId = campaign.Id,
            ActionType = CampaignLifecycleActionType.Activate,
            ActionStatus = CampaignLifecycleActionStatus.Completed,
            EffectiveAtUtc = now,
            ReasonCategory = "SCHEDULED",
            DetailedReason = "Activated automatically on the campaign's start date.",

            // Named explicitly, because the DbContext has no signed-in actor to stamp here and
            // a lifecycle row attributed to nobody is one an investigation cannot follow.
            RequestedByUserId = SystemUsers.SystemUserId,
            CreatedByUserId = SystemUsers.SystemUserId
        });

        // The audit row is written HERE rather than through IAuditWriter, which reads the actor
        // and the Organisation off the request context - and there is no request. Taking both
        // from the campaign keeps the row attributable and keeps it inside the Organisation the
        // campaign belongs to.
        context.AuditEvents.Add(new CampaignAuditEvent
        {
            TenantId = campaign.TenantId,
            BusinessUnitId = campaign.BusinessUnitId == Guid.Empty ? null : campaign.BusinessUnitId,
            ActorUserId = SystemUsers.SystemUserId,
            ActionCode = AuditActionCodes.CampaignAutoActivated,
            TargetType = nameof(Campaign),
            TargetId = campaign.Id,
            Result = AuditResult.Succeeded,
            Reason = $"Start date {campaign.StartDate:yyyy-MM-dd} reached.",
            CorrelationId = $"campaign-activation-sweep/{now:O}",
            OccurredAtUtc = now
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Campaign {CampaignId} ({CampaignCode}) activated automatically on its start date "
                + "{StartDate}.",
                campaign.Id, campaign.Code, campaign.StartDate);
        }
        catch (DbUpdateException exception)
        {
            // Almost always somebody pressing Activate on the same campaign in the same second.
            // The campaign ends up Active either way, so this is worth a line and nothing more.
            logger.LogWarning(
                exception,
                "Campaign {CampaignId} could not be activated by the sweep. It will be retried.",
                campaign.Id);

            context.ChangeTracker.Clear();
        }
    }

    /// <summary>Waits for the next tick, treating shutdown as "stop" rather than as an error.</summary>
    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
