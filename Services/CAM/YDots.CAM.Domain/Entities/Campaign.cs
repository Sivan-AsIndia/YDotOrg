using YDots.CAM.Domain.Common;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Domain.Entities;

/// <summary>
/// A fundraising campaign.
///
/// IT DERIVES FROM <see cref="TenantEntity"/>, WHICH IS THE CHANGE THAT MATTERS. The bare
/// <c>OrganisationId</c> column it used to carry is now <c>TenantId</c> on the base, and the
/// base implements <see cref="ITenantOwned"/> - so the DbContext attaches a query filter and
/// stamps the owner on insert automatically. Isolation stopped depending on every repository
/// method remembering a Where clause.
///
/// THE GEOGRAPHY AND CURRENCY ARE IDs INTO THE IAM MASTER CATALOGUE - <c>gm_countries</c>,
/// <c>gm_state_provinces</c>, <c>gm_cities</c>, <c>gm_currencies</c> - which now live in the
/// same database. They are deliberately NOT foreign keys: CAM and IAM are separately
/// deployable services that happen to share a database, and a cross-service FK would make a
/// schema change in one able to block a migration in the other.
/// </summary>
public class Campaign : TenantEntity, ICodedEntity
{
    /// <summary>Unique inside the Organisation, for example SUMMER25.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;

    public string FundOrProgramme { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public decimal TargetAmount { get; set; }

    /// <summary>Row in the IAM currency master. Not an FK - see the type comment.</summary>
    public Guid CurrencyId { get; set; }

    public decimal? BudgetAmount { get; set; }

    public Guid CountryId { get; set; }

    public Guid? StateId { get; set; }

    public Guid? CityId { get; set; }

    public string? ZipCode { get; set; }

    public LifecycleActivation LifecycleActivation { get; set; }

    public int DaysBeforeStart { get; set; }

    public TimeOnly ReminderTime { get; set; }

    public string? PublicDescription { get; set; }

    public string? TermsAndNotice { get; set; }

    public CampaignStatus Status { get; set; }

    /// <summary>
    /// Who submitted it for approval, and when.
    ///
    /// STORED RATHER THAN DERIVED FROM THE AUDIT TRAIL, because the segregation-of-duties rule
    /// needs it on every approval check: nobody may approve a campaign they personally created
    /// or submitted, whatever role they hold. Reading that from the audit table on each
    /// approval would be a scan of an append-only log to answer a question the row itself can
    /// hold.
    /// </summary>
    public Guid? SubmittedByUserId { get; set; }

    public DateTimeOffset? SubmittedAtUtc { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public ICollection<CampaignOwner> Owners { get; set; } = [];

    public ICollection<CampaignChannel> Channels { get; set; } = [];

    public ICollection<CampaignLifecycleAction> LifecycleActions { get; set; } = [];

    public ICollection<TrackingAsset> TrackingAssets { get; set; } = [];

    public ICollection<CampaignReadinessCheck> ReadinessChecks { get; set; } = [];

    /// <summary>
    /// Whether this caller is independent enough to approve the campaign.
    ///
    /// The rule from section 5.2 of the module brief, expressed once on the entity rather than
    /// re-derived in each of the approval handlers. Creator and submitter are BOTH excluded:
    /// somebody who created a campaign and had a colleague submit it would otherwise still be
    /// able to approve their own work.
    ///
    /// IT ASKS NOTHING ABOUT THE CALLER'S ROLE, and that is the point. TENANT_ADMIN holds every
    /// permission in the Organisation and is refused here exactly like everybody else, because
    /// four-eyes is the one control the platform does not let a role grant its way past.
    /// </summary>
    public bool CanBeApprovedBy(Guid userId) =>
        CreatedByUserId != userId && SubmittedByUserId != userId;

    /// <summary>Only a Draft campaign may be edited freely or deleted.</summary>
    public bool IsDraft => Status == CampaignStatus.Draft;
}
