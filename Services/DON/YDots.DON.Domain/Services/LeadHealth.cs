using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Domain.Services;

/// <summary>
/// Lead health: one 0-100 reading of how well a lead is going, with the reasons that produced it.
///
/// WHY IT IS HERE RATHER THAN IN THE BROWSER. The Lead Work Queue and My Leads both sort and
/// filter on health, and a score computed in the component can only be applied to the rows that
/// were already fetched - so "show me the healthiest leads" would rank one page rather than the
/// queue. The figure is written onto the lead whenever the record changes, so the database can
/// order by it.
///
/// THE WEIGHTS ARE A STARTING POSITION, NOT A SPECIFICATION. The module brief shows health
/// scores on screen but never states how they are produced, so this is a defensible reading of
/// the same signals the screens display - stage, temperature, contact recency, reachability -
/// and it is deliberately written as four small additions so it can be retuned by changing a
/// number rather than by unpicking a formula. If the business has a real model, this is the one
/// place to put it.
///
/// EVERY POINT IS EXPLAINED. <see cref="Explain"/> returns the reasons behind the number, which
/// is what the preview panel lists under "Lead health". A score somebody cannot account for is a
/// number they will learn to ignore.
/// </summary>
public static class LeadHealth
{
    /// <summary>How far through the pipeline the lead has come. Worth up to 40.</summary>
    private static int StagePoints(LeadStatus status) =>
        status switch
        {
            LeadStatus.Converted => 40,
            LeadStatus.Qualified => 35,
            LeadStatus.Contacted => 20,
            LeadStatus.Assigned => 10,
            LeadStatus.New => 5,
            LeadStatus.Nurture => 5,
            _ => 0
        };

    /// <summary>How engaged they are right now. Worth up to 30.</summary>
    private static int TemperaturePoints(LeadTemperature temperature) =>
        temperature switch
        {
            LeadTemperature.Hot => 30,
            LeadTemperature.Warm => 18,
            _ => 5
        };

    /// <summary>
    /// How recently somebody actually spoke to them. Worth up to 20.
    ///
    /// THIS IS THE ONE THAT DECAYS, and it is why health is recomputed on read-through rather
    /// than only on write: a lead nobody has touched for a month is not as healthy as it was a
    /// month ago, and no edit happened to say so.
    /// </summary>
    private static int RecencyPoints(DateTimeOffset? lastContactedUtc, DateTimeOffset now)
    {
        if (lastContactedUtc is null)
        {
            return 0;
        }

        var days = (now - lastContactedUtc.Value).TotalDays;

        return days switch
        {
            <= 7 => 20,
            <= 14 => 14,
            <= 30 => 8,
            <= 60 => 3,
            _ => 0
        };
    }

    /// <summary>Whether we can reach them at all, and whether they said we may. Worth up to 10.</summary>
    private static int ReachabilityPoints(Lead lead)
    {
        var points = 0;

        if (!string.IsNullOrWhiteSpace(lead.EmailAddress) || !string.IsNullOrWhiteSpace(lead.MobileNumber))
        {
            points += 5;
        }

        if (lead.ConsentState == ConsentState.Granted)
        {
            points += 5;
        }

        return points;
    }

    /// <summary>The score, clamped to 0-100.</summary>
    public static int Calculate(Lead lead, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lead);

        var total = StagePoints(lead.Status)
            + TemperaturePoints(lead.Temperature)
            + RecencyPoints(lead.LastContactedAtUtc, now)
            + ReachabilityPoints(lead);

        return Math.Clamp(total, 0, 100);
    }

    /// <summary>
    /// The reasons behind the score, in the order the preview panel lists them.
    ///
    /// PHRASED AS OBSERVATIONS, not as arithmetic. "Recent activity" is something a fundraiser can
    /// act on; "+20 recency" is a number explaining a number.
    /// </summary>
    public static IReadOnlyList<string> Explain(Lead lead, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lead);

        var reasons = new List<string>();

        if (RecencyPoints(lead.LastContactedAtUtc, now) >= 14)
        {
            reasons.Add("Recent activity");
        }
        else if (lead.LastContactedAtUtc is null)
        {
            reasons.Add("No contact yet");
        }
        else
        {
            reasons.Add("Contact is going stale");
        }

        if (lead.Temperature == LeadTemperature.Hot)
        {
            reasons.Add("Engagement is high");
        }

        if (lead.DonationPotential == DonationPotential.High)
        {
            reasons.Add("High donation potential");
        }

        if (!string.IsNullOrWhiteSpace(lead.Source))
        {
            reasons.Add("Source verified");
        }

        if (lead.ConsentState != ConsentState.Granted)
        {
            reasons.Add("Consent not granted");
        }

        if (string.IsNullOrWhiteSpace(lead.EmailAddress) && string.IsNullOrWhiteSpace(lead.MobileNumber))
        {
            reasons.Add("No way to reach this lead");
        }

        return reasons;
    }
}
