using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Common.Constants;

/// <summary>
/// The checklist every new campaign starts with: one check per readiness category.
///
/// WHY A CAMPAIGN NEEDS THESE ON DAY ONE. The readiness screen was drawing six cards - Public
/// content, Budget approval, Tracking readiness, Payment readiness, Template readiness, Consent
/// notice - that existed only in the browser. Four were stubbed from a client-side service and
/// two were derived from other client stores. None of them was a <see cref="CampaignReadinessCheck"/>,
/// and three consequences followed, all of them visible:
///
///   - ASSIGN BLOCKER COULD NEVER SUCCEED. A blocker hangs off a check, and those cards had no
///     check id to hang from - they passed their own key, 'budget' or 'tracking'. Every attempt
///     was refused with "A blocker must be raised against a readiness check."
///   - PASS AND FAIL DID NOT PERSIST. The manual override lived in a browser signal, so a check
///     marked Passed was Pending again after a reload, and "MANUALLY OVERRIDDEN" recorded nothing.
///   - THE PERCENTAGE WAS FICTION. Overall readiness, the Passed / Failed / Pending tiles and
///     "Total items 6" were all computed from the synthetic cards rather than from the checklist
///     the launch gate actually consults - so the screen and the server could disagree completely
///     about whether a campaign was ready.
///
/// Seeding them as real rows makes all three work with no special cases: they are ordinary checks
/// that can be passed, failed, blocked, edited and reported on.
///
/// ONE PER CATEGORY, AND THAT IS THE WHOLE DESIGN. <see cref="ReadinessCheckCategory"/> has six
/// values and the screen had six cards; the match was not a coincidence, it was the intended
/// checklist that never reached the database.
///
/// ALL SIX ARE REQUIRED FOR LAUNCH. They are the six things this platform considers a campaign
/// cannot sensibly go live without. An organisation that disagrees can edit any of them, mark it
/// optional, or delete it - they are a starting point, not a policy.
/// </summary>
public static class DefaultReadinessChecks
{
    /// <summary>One seeded check, before it is attached to a campaign.</summary>
    public sealed record Definition(
        string CheckName,
        ReadinessCheckCategory Category,
        string SuccessCriteria,
        string Description);

    public static readonly IReadOnlyList<Definition> All =
    [
        new(
            "Public content status",
            ReadinessCheckCategory.Content,
            "The public description and the donor-facing page have been read and approved by "
            + "whoever owns the campaign's messaging.",
            "Covers the wording a donor sees before they give."),

        new(
            "Budget approval",
            ReadinessCheckCategory.Budget,
            "The campaign's budget and target figures have been agreed with finance.",
            "Sign this off from whatever finance actually uses while Budget & Target Plans is on "
            + "hold - the module cannot supply an answer, and a check that can never pass would "
            + "hold every campaign at zero readiness for ever."),

        new(
            "Tracking readiness",
            ReadinessCheckCategory.Tracking,
            "Every tracking asset the campaign needs has been created, approved and activated, "
            + "and at least one has been tested end to end.",
            "A QR code that resolves to nothing produces gifts nobody can attribute."),

        new(
            "Payment readiness",
            ReadinessCheckCategory.Payment,
            "The payment gateway is configured for this campaign and a test gift has settled.",
            "The one check whose failure costs money rather than accuracy."),

        new(
            "Template readiness",
            ReadinessCheckCategory.Template,
            "The receipt and thank-you templates for this campaign exist and have been previewed.",
            "A donor who gives and hears nothing back is the most expensive kind of mistake."),

        new(
            "Consent notice version",
            ReadinessCheckCategory.Consent,
            "The consent wording and any tax or data notices are published and current.",
            "Compliance wording is version-controlled; confirm the campaign points at the live one.")
    ];

    /// <summary>
    /// The seeded checks as entities, ready to be added alongside a new campaign.
    ///
    /// EVERY ONE STARTS <see cref="ReadinessCheckStatus.Pending"/>, with no owner and no due
    /// date. Assigning either from here would be inventing a commitment nobody made; the
    /// checklist screen is where a person picks them up.
    /// </summary>
    public static IEnumerable<CampaignReadinessCheck> ForCampaign(Guid campaignId) =>
        All.Select(definition => new CampaignReadinessCheck
        {
            CampaignId = campaignId,
            CheckName = definition.CheckName,
            Category = definition.Category,
            SuccessCriteria = definition.SuccessCriteria,
            Description = definition.Description,
            RequiredForLaunch = true,
            Status = ReadinessCheckStatus.Pending
        });
}
