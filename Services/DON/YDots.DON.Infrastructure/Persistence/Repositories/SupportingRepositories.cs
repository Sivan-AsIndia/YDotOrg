using Microsoft.EntityFrameworkCore;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.DTOs;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

using YDots.DON.Infrastructure.Services;

namespace YDots.DON.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of the campaign repository.</summary>
public sealed class CampaignRepository(DonDbContext context, CampaignProjection projection)
    : ICampaignRepository
{
    public Task<Campaign?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Campaigns.FirstOrDefaultAsync(campaign => campaign.Id == id, cancellationToken);

    // CAM OWNS CAMPAIGNS; this module keeps a copy so its leads can point at one. Refreshing here
    // rather than on a timer means the campaign an Organisation approved a moment ago is on the
    // Lead Capture screen when they open it, instead of appearing whenever a job next ran.
    public async Task<IReadOnlyList<Campaign>> GetActiveAsync(Guid organisationId, CancellationToken cancellationToken = default)
    {
        await projection.RefreshAsync(organisationId, cancellationToken);

        return await context.Campaigns
            .Where(campaign => campaign.OrganisationId == organisationId && campaign.Status != CampaignStatus.Closed)
            .OrderBy(campaign => campaign.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Campaign>> SearchAsync(
        Guid organisationId,
        string? search,
        int maximumRows,
        CancellationToken cancellationToken = default)
    {
        await projection.RefreshAsync(organisationId, cancellationToken);

        var campaigns = context.Campaigns.Where(campaign => campaign.OrganisationId == organisationId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            campaigns = campaigns.Where(campaign =>
                campaign.Code.ToLower().Contains(term) || campaign.Name.ToLower().Contains(term));
        }

        return await campaigns
            .OrderBy(campaign => campaign.Name)
            .Take(maximumRows)
            .ToListAsync(cancellationToken);
    }

    public void Add(Campaign campaign) => context.Campaigns.Add(campaign);
}

/// <summary>EF Core implementation of the duplicate review repository.</summary>
public sealed class DonorMergeCaseRepository(DonDbContext context) : IDonorMergeCaseRepository
{
    public async Task<PagedResponse<DonorMergeCase>> SearchAsync(
        DuplicateReviewSearchFilter filter,
        AccessScope scope,
        CancellationToken cancellationToken = default)
    {
        var cases = context.DonorMergeCases
            .Include(mergeCase => mergeCase.CandidateADonor)
            .Include(mergeCase => mergeCase.CandidateBDonor)
            .Where(mergeCase => mergeCase.OrganisationId == scope.OrganisationId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            cases = cases.Where(mergeCase =>
                mergeCase.ReviewReference.ToLower().Contains(term)
                || mergeCase.Name.ToLower().Contains(term));
        }

        if (filter.Status is not null)
        {
            cases = cases.Where(mergeCase => mergeCase.Status == filter.Status);
        }

        if (filter.IdentityConfidence is not null)
        {
            cases = cases.Where(mergeCase => mergeCase.IdentityConfidence == filter.IdentityConfidence);
        }

        if (filter.Decision is not null)
        {
            cases = cases.Where(mergeCase => mergeCase.Decision == filter.Decision);
        }

        if (filter.CandidateDonorId is not null)
        {
            cases = cases.Where(mergeCase =>
                mergeCase.CandidateADonorId == filter.CandidateDonorId
                || mergeCase.CandidateBDonorId == filter.CandidateDonorId);
        }

        if (filter.RaisedAfterUtc is not null)
        {
            cases = cases.Where(mergeCase => mergeCase.CreatedAtUtc >= filter.RaisedAfterUtc);
        }

        if (filter.RaisedBeforeUtc is not null)
        {
            cases = cases.Where(mergeCase => mergeCase.CreatedAtUtc <= filter.RaisedBeforeUtc);
        }

        var total = await cases.CountAsync(cancellationToken);

        // Undecided cases first: the queue exists so somebody clears them.
        var items = await cases
            .OrderBy(mergeCase => mergeCase.DecidedAtUtc != null)
            .ThenByDescending(mergeCase => mergeCase.IdentityConfidence)
            .ThenByDescending(mergeCase => mergeCase.CreatedAtUtc)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<DonorMergeCase>(items, total, filter.Page, filter.PageSize);
    }

    public Task<DonorMergeCase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.DonorMergeCases.FirstOrDefaultAsync(mergeCase => mergeCase.Id == id, cancellationToken);

    public Task<DonorMergeCase?> GetWithCandidatesAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.DonorMergeCases
            .Include(mergeCase => mergeCase.CandidateADonor)
            .Include(mergeCase => mergeCase.CandidateBDonor)
            .FirstOrDefaultAsync(mergeCase => mergeCase.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DonorMergeCase>> GetForDonorAsync(Guid donorId, CancellationToken cancellationToken = default) =>
        await context.DonorMergeCases
            .Where(mergeCase => mergeCase.CandidateADonorId == donorId || mergeCase.CandidateBDonorId == donorId)
            .OrderByDescending(mergeCase => mergeCase.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<bool> PairExistsAsync(Guid candidateAId, Guid candidateBId, CancellationToken cancellationToken = default) =>
        // The pair is unordered: A/B and B/A are the same review, so both directions are checked.
        context.DonorMergeCases.AnyAsync(
            mergeCase => (mergeCase.CandidateADonorId == candidateAId && mergeCase.CandidateBDonorId == candidateBId)
                         || (mergeCase.CandidateADonorId == candidateBId && mergeCase.CandidateBDonorId == candidateAId),
            cancellationToken);

    public async Task<int> GetMaxReferenceSequenceAsync(int year, CancellationToken cancellationToken = default)
    {
        var prefix = $"DUP-{year:0000}-";

        var references = await context.DonorMergeCases
            .Where(mergeCase => mergeCase.ReviewReference.StartsWith(prefix))
            .Select(mergeCase => mergeCase.ReviewReference)
            .ToListAsync(cancellationToken);

        return references.Count == 0
            ? 0
            : references
                .Select(reference => int.TryParse(reference[prefix.Length..], out var parsed) ? parsed : 0)
                .DefaultIfEmpty(0)
                .Max();
    }

    public void Add(DonorMergeCase mergeCase) => context.DonorMergeCases.Add(mergeCase);
}

/// <summary>EF Core implementation of the identity verification repository.</summary>
public sealed class VerificationRepository(DonDbContext context) : IVerificationRepository
{
    /// <summary>The states where a verification is still in flight.</summary>
    private static readonly VerificationStatus[] OpenStatuses =
    [
        VerificationStatus.NotStarted, VerificationStatus.ChallengeSent, VerificationStatus.Escalated
    ];

    public async Task<PagedResponse<DonorIdentityVerification>> SearchAsync(
        VerificationSearchFilter filter,
        AccessScope scope,
        CancellationToken cancellationToken = default)
    {
        var verifications = context.DonorIdentityVerifications
            .Include(verification => verification.Donor)
            .Where(verification => verification.OrganisationId == scope.OrganisationId);

        if (scope.IsOwnRecordsOnly)
        {
            verifications = verifications.Where(verification =>
                verification.Donor != null && verification.Donor.RelationshipOwnerUserId == scope.UserId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            verifications = verifications.Where(verification =>
                verification.VerificationReference.ToLower().Contains(term)
                || (verification.Donor != null && verification.Donor.DonorNumber.ToLower().Contains(term)));
        }

        if (filter.DonorId is not null)
        {
            verifications = verifications.Where(verification => verification.DonorId == filter.DonorId);
        }

        if (filter.Status is not null)
        {
            verifications = verifications.Where(verification => verification.Status == filter.Status);
        }

        if (filter.Channel is not null)
        {
            verifications = verifications.Where(verification => verification.VerificationChannel == filter.Channel);
        }

        if (filter.IdentityConfidence is not null)
        {
            verifications = verifications.Where(verification => verification.IdentityConfidence == filter.IdentityConfidence);
        }

        if (filter.ReviewerUserId is not null)
        {
            verifications = verifications.Where(verification => verification.ReviewerUserId == filter.ReviewerUserId);
        }

        if (filter.SentAfterUtc is not null)
        {
            verifications = verifications.Where(verification => verification.SentAtUtc >= filter.SentAfterUtc);
        }

        if (filter.SentBeforeUtc is not null)
        {
            verifications = verifications.Where(verification => verification.SentAtUtc <= filter.SentBeforeUtc);
        }

        var total = await verifications.CountAsync(cancellationToken);

        var items = await verifications
            .OrderByDescending(verification => verification.CreatedAtUtc)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<DonorIdentityVerification>(items, total, filter.Page, filter.PageSize);
    }

    public Task<DonorIdentityVerification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.DonorIdentityVerifications
            .Include(verification => verification.Donor)
            .FirstOrDefaultAsync(verification => verification.Id == id, cancellationToken);

    public Task<DonorIdentityVerification?> GetOpenForDonorAsync(Guid donorId, CancellationToken cancellationToken = default) =>
        context.DonorIdentityVerifications
            .Where(verification => verification.DonorId == donorId && OpenStatuses.Contains(verification.Status))
            .OrderByDescending(verification => verification.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DonorIdentityVerification>> GetHistoryForDonorAsync(
        Guid donorId,
        CancellationToken cancellationToken = default) =>
        await context.DonorIdentityVerifications
            .Where(verification => verification.DonorId == donorId)
            .OrderByDescending(verification => verification.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<int> GetMaxReferenceSequenceAsync(int year, CancellationToken cancellationToken = default)
    {
        var prefix = $"VER-{year:0000}-";

        var references = await context.DonorIdentityVerifications
            .Where(verification => verification.VerificationReference.StartsWith(prefix))
            .Select(verification => verification.VerificationReference)
            .ToListAsync(cancellationToken);

        return references.Count == 0
            ? 0
            : references
                .Select(reference => int.TryParse(reference[prefix.Length..], out var parsed) ? parsed : 0)
                .DefaultIfEmpty(0)
                .Max();
    }

    public void Add(DonorIdentityVerification verification) => context.DonorIdentityVerifications.Add(verification);
}

/// <summary>EF Core implementation of the follow-up repository.</summary>
public sealed class FollowUpRepository(DonDbContext context) : IFollowUpRepository
{
    /// <summary>The states where a follow-up still needs doing.</summary>
    private static readonly FollowUpStatus[] OpenStatuses =
    [
        FollowUpStatus.Planned, FollowUpStatus.Assigned, FollowUpStatus.Rescheduled
    ];

    public async Task<PagedResponse<FollowUpTask>> SearchAsync(
        FollowUpSearchFilter filter,
        AccessScope scope,
        CancellationToken cancellationToken = default)
    {
        var tasks = context.FollowUpTasks
            .Include(task => task.Donor)
            .Include(task => task.Lead)
            .Where(task => task.OrganisationId == scope.OrganisationId);

        if (scope.IsOwnRecordsOnly)
        {
            tasks = tasks.Where(task => task.RelationshipOwnerUserId == scope.UserId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            tasks = tasks.Where(task =>
                task.FollowUpReference.ToLower().Contains(term)
                || (task.Donor != null && task.Donor.DonorNumber.ToLower().Contains(term))
                || (task.Lead != null && task.Lead.LeadReference.ToLower().Contains(term)));
        }

        if (filter.DonorId is not null)
        {
            tasks = tasks.Where(task => task.DonorId == filter.DonorId);
        }

        if (filter.LeadId is not null)
        {
            tasks = tasks.Where(task => task.LeadId == filter.LeadId);
        }

        if (filter.RelationshipOwnerUserId is not null)
        {
            tasks = tasks.Where(task => task.RelationshipOwnerUserId == filter.RelationshipOwnerUserId);
        }

        if (filter.Status is not null)
        {
            tasks = tasks.Where(task => task.Status == filter.Status);
        }

        if (filter.Priority is not null)
        {
            tasks = tasks.Where(task => task.Priority == filter.Priority);
        }

        if (filter.PermittedChannel is not null)
        {
            tasks = tasks.Where(task => task.PermittedChannel == filter.PermittedChannel);
        }

        if (!string.IsNullOrWhiteSpace(filter.PreferredLanguage))
        {
            tasks = tasks.Where(task => task.PreferredLanguage == filter.PreferredLanguage);
        }

        if (filter.DueAfterUtc is not null)
        {
            tasks = tasks.Where(task => task.DueAtUtc >= filter.DueAfterUtc);
        }

        if (filter.DueBeforeUtc is not null)
        {
            tasks = tasks.Where(task => task.DueAtUtc <= filter.DueBeforeUtc);
        }

        var total = await tasks.CountAsync(cancellationToken);

        var items = await tasks
            .OrderBy(task => task.DueAtUtc == null)
            .ThenBy(task => task.DueAtUtc)
            .ThenByDescending(task => task.Priority)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<FollowUpTask>(items, total, filter.Page, filter.PageSize);
    }

    public Task<FollowUpTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.FollowUpTasks
            .Include(task => task.Donor)
            .Include(task => task.Lead)
            .FirstOrDefaultAsync(task => task.Id == id, cancellationToken);

    public async Task<IReadOnlyList<FollowUpTask>> GetOpenForDonorAsync(Guid donorId, CancellationToken cancellationToken = default) =>
        await context.FollowUpTasks
            .Where(task => task.DonorId == donorId && OpenStatuses.Contains(task.Status))
            .OrderBy(task => task.DueAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FollowUpTask>> GetOpenForLeadAsync(Guid leadId, CancellationToken cancellationToken = default) =>
        await context.FollowUpTasks
            .Where(task => task.LeadId == leadId && OpenStatuses.Contains(task.Status))
            .OrderBy(task => task.DueAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<int> GetMaxReferenceSequenceAsync(int year, CancellationToken cancellationToken = default)
    {
        var prefix = $"FUP-{year:0000}-";

        var references = await context.FollowUpTasks
            .Where(task => task.FollowUpReference.StartsWith(prefix))
            .Select(task => task.FollowUpReference)
            .ToListAsync(cancellationToken);

        return references.Count == 0
            ? 0
            : references
                .Select(reference => int.TryParse(reference[prefix.Length..], out var parsed) ? parsed : 0)
                .DefaultIfEmpty(0)
                .Max();
    }

    public void Add(FollowUpTask task) => context.FollowUpTasks.Add(task);
}

/// <summary>EF Core implementation of the Donor 360 read-only panels.</summary>
public sealed class Donor360Repository(DonDbContext context) : IDonor360Repository
{
    /// <summary>
    /// The "Donation totals by stage" rows, in the order money moves through the stages.
    ///
    /// SORTED IN MEMORY, ON PURPOSE. <c>OrderBy(summary =&gt; summary.Stage)</c> looked like it
    /// ordered by the lifecycle and did not: the stage is persisted as a string, so the database
    /// sorted it alphabetically - Committed, Outstanding, Pledged, Received, Reconciled, Refunded -
    /// and the panel showed "Outstanding" above "Pledged". Ordering by the enum instead needs a
    /// CASE expression in SQL, and this is at most a handful of rows for one donor, so the sort is
    /// done after materialising rather than pushed into a query nobody can read.
    /// </summary>
    public async Task<IReadOnlyList<DonorDonationSummary>> GetDonationSummariesAsync(
        Guid donorId,
        CancellationToken cancellationToken = default)
    {
        var summaries = await context.DonorDonationSummaries
            .Where(summary => summary.DonorId == donorId)
            .ToListAsync(cancellationToken);

        return [.. summaries.OrderBy(summary => summary.Stage.LifecycleOrder())];
    }

    public async Task<IReadOnlyList<DonorPromise>> GetPromisesAsync(
        Guid donorId,
        CancellationToken cancellationToken = default) =>
        await context.DonorPromises
            .Include(promise => promise.Campaign)
            .Where(promise => promise.DonorId == donorId)
            .OrderByDescending(promise => promise.PromisedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DonorDocument>> GetDocumentsAsync(
        Guid donorId,
        bool includeConfidential,
        CancellationToken cancellationToken = default)
    {
        var documents = context.DonorDocuments.Where(document => document.DonorId == donorId);

        // Filtered in the query, not after it. An unpermitted caller never receives the row,
        // so its name and reference are not in the payload at all.
        if (!includeConfidential)
        {
            documents = documents.Where(document => document.Classification != DocumentClassification.Confidential);
        }

        return await documents.OrderByDescending(document => document.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(Campaign Campaign, string LeadReference, DateTimeOffset? ConvertedAtUtc)>>
        GetCampaignHistoryAsync(Guid donorId, CancellationToken cancellationToken = default)
    {
        // The campaign history is derived, not stored: a donor was reached through whichever
        // campaigns their leads came from.
        var rows = await context.Leads
            .Include(lead => lead.Campaign)
            .Where(lead => lead.ConvertedDonorId == donorId && lead.Campaign != null)
            .OrderByDescending(lead => lead.ConvertedAtUtc)
            .Select(lead => new { lead.Campaign, lead.LeadReference, lead.ConvertedAtUtc })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => (row.Campaign!, row.LeadReference, row.ConvertedAtUtc))];
    }

    public void AddPromise(DonorPromise promise) => context.DonorPromises.Add(promise);

    public void AddDocument(DonorDocument document) => context.DonorDocuments.Add(document);

    public void AddDonationSummary(DonorDonationSummary summary) => context.DonorDonationSummaries.Add(summary);
}

/// <summary>EF Core implementation of the idempotency store.</summary>
public sealed class IdempotencyRepository(DonDbContext context) : IIdempotencyRepository
{
    public Task<IdempotencyRecord?> FindAsync(string key, string endpoint, CancellationToken cancellationToken = default) =>
        context.IdempotencyRecords.FirstOrDefaultAsync(
            record => record.Key == key && record.Endpoint == endpoint,
            cancellationToken);

    public void Add(IdempotencyRecord record) => context.IdempotencyRecords.Add(record);
}
