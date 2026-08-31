using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Features.DuplicateReview.DTOs;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.DuplicateReview.Mappings;

/// <summary>Manual mapping for the duplicate review slice.</summary>
public static class DuplicateReviewMappingConfig
{
    public static DuplicateReviewListItemResponse ToListItemResponse(this DonorMergeCase mergeCase) =>
        new(
            mergeCase.Id,
            mergeCase.ReviewReference,
            mergeCase.Name,
            mergeCase.CandidateADonor?.DisplayName ?? mergeCase.CandidateADonorId.ToString(),
            mergeCase.CandidateBDonor?.DisplayName ?? mergeCase.CandidateBDonorId.ToString(),
            mergeCase.IdentityConfidence.ToString(),
            mergeCase.Status.ToString(),
            mergeCase.Decision?.ToString(),
            mergeCase.CreatedAtUtc,
            mergeCase.DecidedAtUtc,
            mergeCase.Version);

    public static DuplicateReviewDetailResponse ToDetailResponse(
        this DonorMergeCase mergeCase,
        bool canSeeContact,
        bool canSeeEvidence,
        IReadOnlyList<string> permittedActions) =>
        new(
            mergeCase.Id,
            mergeCase.ReviewReference,
            mergeCase.Name,
            mergeCase.Description,
            mergeCase.Status.ToString(),
            BuildCandidate(mergeCase.CandidateADonor, mergeCase.CandidateADonorId, canSeeContact),
            BuildCandidate(mergeCase.CandidateBDonor, mergeCase.CandidateBDonorId, canSeeContact),
            ContactMasking.Confidential(mergeCase.ContactComparison, canSeeContact),
            mergeCase.IdentityConfidence.ToString(),
            ContactMasking.Confidential(mergeCase.MatchingEvidence, canSeeEvidence),
            mergeCase.ConflictingFields,
            mergeCase.DonationHistoryImpact,
            mergeCase.ConsentImpact,
            mergeCase.Decision?.ToString(),
            mergeCase.DecisionReason,
            mergeCase.SurvivingDonorId,
            mergeCase.MergePreview,
            mergeCase.DecidedByUserId,
            mergeCase.DecidedByName,
            mergeCase.DecidedAtUtc,
            mergeCase.CreatedAtUtc,
            mergeCase.CreatedByUserId,
            mergeCase.UpdatedAtUtc,
            mergeCase.Version,
            !canSeeContact,
            !canSeeEvidence,
            permittedActions);

    /// <summary>
    /// One side of the comparison. The e-mail and phone are always run through the mask helper,
    /// because a duplicate review is exactly the screen where somebody else's protected details
    /// would otherwise be shown to a steward who only needs to know that the values match.
    /// </summary>
    private static CandidateSummaryResponse BuildCandidate(Donor? donor, Guid donorId, bool canSeeContact) =>
        donor is null
            ? new CandidateSummaryResponse(donorId, string.Empty, "Unavailable", string.Empty, string.Empty,
                string.Empty, default, null, null)
            : new CandidateSummaryResponse(
                donor.Id,
                donor.DonorNumber,
                donor.DisplayName,
                donor.DonorType.ToString(),
                donor.Status.ToString(),
                donor.PreferredLanguage,
                donor.CreatedAtUtc,
                ContactMasking.Email(donor.PrimaryEmail, canSeeContact),
                ContactMasking.Phone(donor.PrimaryPhone, canSeeContact));

    /// <summary>Which review actions the case state allows.</summary>
    public static IReadOnlyList<string> PermittedActionsFor(DonorMergeCase mergeCase) =>
        mergeCase.Status switch
        {
            DonorMergeCaseStatus.Active or DonorMergeCaseStatus.UnderReview =>
                ["Review evidence", "Merge", "Reject candidate"],
            _ => ["Review evidence"]
        };

    /// <summary>
    /// The readable preview of the record a merge would produce. Built from the surviving
    /// record with anything the other one can contribute, so the steward sees the outcome
    /// before committing to it.
    /// </summary>
    public static string BuildMergePreview(Donor surviving, Donor absorbed) =>
        string.Join(
            " | ",
            $"Donor number: {surviving.DonorNumber} (keeps)",
            $"Name: {surviving.DisplayName}",
            $"Type: {surviving.DonorType}",
            $"E-mail: {(string.IsNullOrWhiteSpace(surviving.PrimaryEmail) ? "from absorbed record" : "kept")}",
            $"Phone: {(string.IsNullOrWhiteSpace(surviving.PrimaryPhone) ? "from absorbed record" : "kept")}",
            $"Absorbed record {absorbed.DonorNumber} becomes Merged and keeps its history");

    /// <summary>Which fields the two records disagree on, as a readable list.</summary>
    public static string BuildConflictingFields(Donor candidateA, Donor candidateB)
    {
        var conflicts = new List<string>();

        if (candidateA.DonorType != candidateB.DonorType)
        {
            conflicts.Add("Donor type");
        }

        if (!Same(candidateA.FirstName, candidateB.FirstName))
        {
            conflicts.Add("First name");
        }

        if (!Same(candidateA.LastName, candidateB.LastName))
        {
            conflicts.Add("Last name");
        }

        if (!Same(candidateA.OrganisationName, candidateB.OrganisationName))
        {
            conflicts.Add("Organisation name");
        }

        if (!Same(candidateA.PrimaryEmail, candidateB.PrimaryEmail))
        {
            conflicts.Add("Primary e-mail");
        }

        if (!Same(candidateA.PrimaryPhone, candidateB.PrimaryPhone))
        {
            conflicts.Add("Primary phone");
        }

        if (!Same(candidateA.PreferredLanguage, candidateB.PreferredLanguage))
        {
            conflicts.Add("Preferred language");
        }

        if (candidateA.DoNotContact != candidateB.DoNotContact)
        {
            conflicts.Add("Do not contact");
        }

        return conflicts.Count == 0 ? "No conflicting fields." : string.Join(", ", conflicts);
    }

    /// <summary>A side-by-side contact summary that says whether values match, not what they are.</summary>
    public static string BuildContactComparison(Donor candidateA, Donor candidateB) =>
        string.Join(
            " | ",
            $"E-mail: {(Same(candidateA.PrimaryEmail, candidateB.PrimaryEmail) ? "identical" : "different")}",
            $"Phone: {(Same(candidateA.PrimaryPhone, candidateB.PrimaryPhone) ? "identical" : "different")}",
            $"Language: {(Same(candidateA.PreferredLanguage, candidateB.PreferredLanguage) ? "identical" : "different")}");

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
