using FluentValidation;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Settings;
using YDots.CAM.Application.Features.Campaigns.DTOs;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.Campaigns.Validators;

/// <summary>
/// The owner-existence rule, shared by the create and update validators so the two cannot drift.
///
/// CAM stores an owner id but does not own the user it points at, so this is the one rule here
/// that has to leave the request and ask identity. It runs last, after the cheap structural
/// checks, and only when there is something worth asking about.
/// </summary>
internal static class CampaignTextRules
{
    /// <summary>
    /// An opening tag: "&lt;" followed by a letter or a slash. Deliberately narrow - it must not
    /// object to "Under &lt; 5 minutes" or "Cost &lt; target", which are ordinary things to write.
    /// </summary>
    private const string MarkupPattern = @"<\s*[A-Za-z/!]";

    /// <summary>
    /// Refuses markup in a field that is rendered as text.
    ///
    /// WHY, GIVEN THE UI IS SAFE ALREADY. Angular escapes an interpolated value and sanitises an
    /// [innerHTML] binding, so a stored "&lt;script&gt;" has never executed in this client. What it
    /// does do is travel: the API returns it verbatim to exports, to reports and to anything else
    /// that consumes the same endpoint without Angular's protection. A campaign name is a plain
    /// label - there is no reason for a tag to be in one - so it is refused at the door rather than
    /// left for every reader to defuse. The rich-text fields (Purpose, PublicDescription,
    /// TermsAndNotice) are NOT covered: they are authored in an editor and hold real markup.
    /// </summary>
    internal static IRuleBuilderOptions<T, string> NoMarkup<T>(this IRuleBuilder<T, string> rule) =>
        rule.Matches($"^(?!.*{MarkupPattern}).*$", System.Text.RegularExpressions.RegexOptions.Singleline)
            .WithMessage("Remove the HTML tags - this field is shown as plain text.");
}

internal static class CampaignOwnerRules
{
    internal static IRuleBuilderOptions<T, IReadOnlyList<Guid>> MustAllExist<T>(
        this IRuleBuilder<T, IReadOnlyList<Guid>> rule,
        IPeopleDirectory people,
        ITenantContext tenant) =>
        rule.MustAsync(async (owners, cancellationToken) =>
            {
                // Nothing to resolve. The NotEmpty and non-empty-guid rules above already have
                // something to say about these, and reporting the same field twice helps nobody.
                if (owners is null || owners.Count == 0 || owners.Any(id => id == Guid.Empty))
                {
                    return true;
                }

                if (!tenant.HasTenant)
                {
                    return true;
                }

                var found = await people.GetExistingUserIdsAsync(
                    tenant.RequireTenantId(), owners, cancellationToken);

                return owners.All(found.Contains);
            })
            .WithMessage("One or more of those owners is not a user in this organisation.");
}

/// <summary>
/// Validators for the Campaigns slice.
///
/// WHAT THESE REPLACE. Each command handler used to carry a private
/// <c>Validate(request)</c> returning a <c>Dictionary&lt;string, string&gt;</c>, which it threw
/// as a <c>CustomValidationException</c> caught by middleware. Three things were wrong with
/// that: an exception is an expensive way to say "the form is wrong", the rules were invisible
/// to anything but the handler that owned them, and the create and update paths had two copies
/// of the same twenty checks that had already drifted apart.
///
/// FluentValidation runs in the pipeline before the handler, so a handler now only ever sees a
/// request that is structurally sound and can concentrate on the questions only it can answer -
/// does this code already exist, is this transition legal, is this caller independent.
///
/// THE MAXIMUM DURATION COMES FROM SETTINGS rather than a literal, so an Organisation running
/// a decade-long endowment appeal is a configuration change rather than a code change.
/// </summary>
public sealed class CreateCampaignRequestValidator : AbstractValidator<CreateCampaignRequest>
{
    public CreateCampaignRequestValidator(
        Microsoft.Extensions.Options.IOptions<CampaignSettings> settings,
        IPeopleDirectory people,
        ITenantContext tenant,
        IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(clock);

        var maximumDurationDays = settings.Value.MaximumCampaignDurationDays;

        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Enter the campaign name.")
            .MaximumLength(250)
            .NoMarkup();

        RuleFor(request => request.Code)
            .NotEmpty().WithMessage("Enter a campaign code.")
            .MaximumLength(20)
            .Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("Use letters, digits, underscores or hyphens only.");

        RuleFor(request => request.Purpose)
            .NotEmpty().WithMessage("Describe what the campaign is for.")
            .MaximumLength(CampaignFieldLimits.Purpose);

        RuleFor(request => request.FundOrProgramme)
            .NotEmpty().WithMessage("Name the fund or programme this campaign raises for.")
            .MaximumLength(250)
            .NoMarkup();

        // ---- The start date ------------------------------------------------------------------
        //
        // A NEW CAMPAIGN MAY NOT START IN THE PAST, and nothing enforced that. The only rule here
        // was "not the default value", the wizard's date input carried no `min`, and its own
        // check asked only whether the field was non-empty - so a campaign could be created with
        // a start date years gone. It was then born already elapsed: `ElapsedPercent` reads 100
        // before anybody has activated it, the reminder that fires `DaysBeforeStart` before the
        // start has a send date in the past, and an "auto-activate on the start date" campaign
        // has a trigger that can never arrive.
        //
        // TODAY IS ALLOWED. Starting a campaign today is the ordinary case for one being set up
        // this morning; only yesterday is wrong.
        //
        // COMPARED IN UTC, from the clock abstraction, because that is what the column stores.
        // A tenant a day ahead of UTC could in principle be refused a start date that is still
        // "today" for them - accepted deliberately over the alternative, which is a rule whose
        // answer depends on which server evaluated it.
        //
        // THIS APPLIES TO CREATE ONLY. The update validator deliberately does NOT carry it: a
        // campaign that has been running for a month must remain editable without its own
        // historic start date being rejected on every save.
        RuleFor(request => request.StartDate)
            .NotEqual(default(DateOnly)).WithMessage("Choose a start date.")
            .GreaterThanOrEqualTo(_ => clock.TodayUtc)
            .WithMessage("The start date cannot be in the past.")
            .When(request => request.StartDate != default);

        RuleFor(request => request.EndDate)
            .NotEqual(default(DateOnly)).WithMessage("Choose an end date.")
            .GreaterThanOrEqualTo(request => request.StartDate)
            .WithMessage("The end date cannot be before the start date.");

        // Guards the typo that would schedule a campaign for a decade. Checked as a pair rather
        // than on EndDate alone, because the span is what is wrong rather than either date.
        RuleFor(request => request)
            .Must(request => request.EndDate.DayNumber - request.StartDate.DayNumber <= maximumDurationDays)
            .WithName(nameof(CreateCampaignRequest.EndDate))
            .WithMessage($"A campaign cannot run for more than {maximumDurationDays} days.")
            .When(request => request.EndDate >= request.StartDate);

        // TARGET AND BUDGET ARE NOT VALIDATED HERE WHILE TARGET & BUDGET IS ON HOLD.
        //
        // The wizard has no Target & Budget step, so the client never sends either value and a
        // campaign is created with a target of 0 - a placeholder, not a figure anybody typed.
        // Validating a number no screen collects can only reject a payload the product itself
        // produced, which is exactly what happened when this briefly required GreaterThan(0):
        // every campaign created through the UI was refused with "Some of the details are not
        // valid", naming a field the user could not see anywhere on the form.
        //
        // WHEN THE MODULE COMES BACK, these rules belong on the Target & Budget step's own
        // request, and "a target must be a real number" belongs on submit or activate - where a
        // campaign claims to be ready - rather than on create, which has to accept a half-filled
        // draft.
        RuleFor(request => request.CurrencyId)
            .NotEmpty().WithMessage("Choose the currency the campaign is stated in.");

        // ---- Step 2: channels and sources ------------------------------------------------------
        //
        // EVERY FIELD ON THIS STEP IS REQUIRED, and none of them was. The wizard drew an asterisk
        // beside country, state, city, zip code, the channels and the reminder pair; the contract
        // accepted a request with all seven missing. A campaign saved that way then had nothing to
        // show on its detail screen - Channel "-", Location "-" - which is not a rendering fault
        // but a record that was genuinely empty.
        RuleFor(request => request.CountryId)
            .NotEmpty().WithMessage("Choose the country the campaign runs in.");

        RuleFor(request => request.StateId)
            .NotEmpty().WithMessage("Choose the state or province the campaign runs in.");

        RuleFor(request => request.CityId)
            .NotEmpty().WithMessage("Choose the city the campaign runs in.");

        RuleFor(request => request.ZipCode)
            .NotEmpty().WithMessage("Enter the postal or zip code.")
            .MaximumLength(20);

        RuleFor(request => request.LifecycleActivation).IsInEnum();

        // NOTNULL BEFORE THE RANGE, because 0 is a legitimate answer here - "remind me on the
        // day" - and an int that was never sent arrives as 0 too. Nullable on the request is the
        // only way the two can be told apart, and the difference matters: one is a choice and the
        // other is an unfinished form.
        RuleFor(request => request.DaysBeforeStart)
            .NotNull().WithMessage("Choose how many days before the start date the reminder runs.")
            .InclusiveBetween(0, 365)
            .WithMessage("The activation reminder runs from 0 to 365 days before the start date.");

        RuleFor(request => request.ReminderTime)
            .NotNull().WithMessage("Choose the time of day the reminder is sent.");

        // A campaign with no channel has no route to anybody, and the tracking assets created
        // against it have no channel to inherit.
        RuleFor(request => request.ChannelIds)
            .NotEmpty().WithMessage("Choose at least one channel.")
            .Must(channels => channels.All(id => id != Guid.Empty))
            .WithMessage("A channel id cannot be empty.");

        // ---- Step 3: publication and notice ----------------------------------------------------
        //
        // BOTH REQUIRED. These are the words a donor reads before giving - what the campaign is
        // for, and the terms the gift is made under - so a campaign that reaches a donor without
        // them is the one case where an empty optional field has a consequence outside the
        // building.
        RuleFor(request => request.PublicDescription)
            .NotEmpty().WithMessage("Write the description donors will see.")
            .MaximumLength(CampaignFieldLimits.PublicDescription);

        RuleFor(request => request.TermsAndNotice)
            .NotEmpty().WithMessage("Enter the terms and notice shown with this campaign.")
            .MaximumLength(CampaignFieldLimits.TermsAndNotice);

        // A campaign with no owner has nobody accountable for it, and nobody to notify when it
        // needs attention.
        RuleFor(request => request.OwnerIds)
            .NotEmpty().WithMessage("Assign at least one owner.")
            .Must(owners => owners.All(id => id != Guid.Empty))
            .WithMessage("An owner id cannot be empty.")
            .MustAllExist(people, tenant);

        // ONLY DRAFT OR SUBMITTED ON CREATE. Everything further along the lifecycle is reached
        // through an endpoint with its own permission and its own rules; accepting Active here
        // would route around every one of them in a single POST.
        RuleFor(request => request.Status)
            .Must(status => status is CampaignStatus.Draft or CampaignStatus.Submitted)
            .WithMessage("A campaign can only be created as Draft or Submitted.");
    }
}

/// <summary>Validator for editing a campaign. Mirrors the create rules, minus the code and status.</summary>
public sealed class UpdateCampaignRequestValidator : AbstractValidator<UpdateCampaignRequest>
{
    public UpdateCampaignRequestValidator(
        Microsoft.Extensions.Options.IOptions<CampaignSettings> settings,
        IPeopleDirectory people,
        ITenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(tenant);

        var maximumDurationDays = settings.Value.MaximumCampaignDurationDays;

        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Enter the campaign name.")
            .MaximumLength(250)
            .NoMarkup();

        RuleFor(request => request.Purpose)
            .NotEmpty().WithMessage("Describe what the campaign is for.")
            .MaximumLength(CampaignFieldLimits.Purpose);

        RuleFor(request => request.FundOrProgramme)
            .NotEmpty().WithMessage("Name the fund or programme this campaign raises for.")
            .MaximumLength(250)
            .NoMarkup();

        RuleFor(request => request.EndDate)
            .GreaterThanOrEqualTo(request => request.StartDate)
            .WithMessage("The end date cannot be before the start date.");

        RuleFor(request => request)
            .Must(request => request.EndDate.DayNumber - request.StartDate.DayNumber <= maximumDurationDays)
            .WithName(nameof(UpdateCampaignRequest.EndDate))
            .WithMessage($"A campaign cannot run for more than {maximumDurationDays} days.")
            .When(request => request.EndDate >= request.StartDate);

        // TARGET AND BUDGET ARE NOT VALIDATED HERE WHILE TARGET & BUDGET IS ON HOLD.
        //
        // The wizard has no Target & Budget step, so the client never sends either value and a
        // campaign is created with a target of 0 - a placeholder, not a figure anybody typed.
        // Validating a number no screen collects can only reject a payload the product itself
        // produced, which is exactly what happened when this briefly required GreaterThan(0):
        // every campaign created through the UI was refused with "Some of the details are not
        // valid", naming a field the user could not see anywhere on the form.
        //
        // WHEN THE MODULE COMES BACK, these rules belong on the Target & Budget step's own
        // request, and "a target must be a real number" belongs on submit or activate - where a
        // campaign claims to be ready - rather than on create, which has to accept a half-filled
        // draft.
        RuleFor(request => request.CurrencyId)
            .NotEmpty().WithMessage("Choose the currency the campaign is stated in.");

        // ---- Step 2 and step 3 --------------------------------------------------------------
        //
        // THE SAME MANDATORY SET AS CREATE, and it has to be: an edit that could clear the
        // channels, the location or the public description would be a second route to the
        // half-empty campaign the create rules now refuse.
        RuleFor(request => request.CountryId)
            .NotEmpty().WithMessage("Choose the country the campaign runs in.");

        RuleFor(request => request.StateId)
            .NotEmpty().WithMessage("Choose the state or province the campaign runs in.");

        RuleFor(request => request.CityId)
            .NotEmpty().WithMessage("Choose the city the campaign runs in.");

        RuleFor(request => request.ZipCode)
            .NotEmpty().WithMessage("Enter the postal or zip code.")
            .MaximumLength(20);

        RuleFor(request => request.LifecycleActivation).IsInEnum();

        RuleFor(request => request.DaysBeforeStart)
            .NotNull().WithMessage("Choose how many days before the start date the reminder runs.")
            .InclusiveBetween(0, 365)
            .WithMessage("The activation reminder runs from 0 to 365 days before the start date.");

        RuleFor(request => request.ReminderTime)
            .NotNull().WithMessage("Choose the time of day the reminder is sent.");

        RuleFor(request => request.PublicDescription)
            .NotEmpty().WithMessage("Write the description donors will see.")
            .MaximumLength(CampaignFieldLimits.PublicDescription);

        RuleFor(request => request.TermsAndNotice)
            .NotEmpty().WithMessage("Enter the terms and notice shown with this campaign.")
            .MaximumLength(CampaignFieldLimits.TermsAndNotice);

        RuleFor(request => request.OwnerIds)
            .NotEmpty().WithMessage("Assign at least one owner.")
            .Must(owners => owners.All(id => id != Guid.Empty))
            .WithMessage("An owner id cannot be empty.")
            .MustAllExist(people, tenant);

        RuleFor(request => request.ChannelIds)
            .NotEmpty().WithMessage("Choose at least one channel.")
            .Must(channels => channels.All(id => id != Guid.Empty))
            .WithMessage("A channel id cannot be empty.");
    }
}

/// <summary>
/// Validator for a lifecycle transition body.
///
/// It checks only what is true of EVERY transition: a version, and reasonable text lengths.
/// Which reason fields are REQUIRED depends on the transition - a close request needs one,
/// resuming does not - and that belongs in the handler, which knows which transition it is.
/// </summary>
public sealed class CampaignLifecycleRequestValidator : AbstractValidator<CampaignLifecycleRequest>
{
    public CampaignLifecycleRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.ReasonCategory).MaximumLength(100);
        RuleFor(request => request.DetailedReason).MaximumLength(2000);
        RuleFor(request => request.CommunicationImpact).MaximumLength(2000);
        RuleFor(request => request.ClosureSummary).MaximumLength(4000);
    }
}
