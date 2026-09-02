using YDots.DON.Application.Features.LeadWorkQueue.DTOs;
using Microsoft.EntityFrameworkCore;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.DTOs;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;
using YDots.DON.Infrastructure.Services;

namespace YDots.DON.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of the lead repository.</summary>
public sealed class LeadRepository(DonDbContext context, PeopleDirectory people) : ILeadRepository
{
    /// <summary>The states that still need somebody to do something. Used by the workload counts.</summary>
    private static readonly LeadStatus[] OpenStatuses =
    [
        LeadStatus.New, LeadStatus.Assigned, LeadStatus.Contacted, LeadStatus.Qualified, LeadStatus.Nurture
    ];

    public async Task<PagedResponse<Lead>> SearchAsync(
        LeadSearchFilter filter,
        AccessScope scope,
        CancellationToken cancellationToken = default)
    {
        var leads = ApplyScope(context.Leads.Include(lead => lead.Campaign), scope);

        if (!filter.IncludeDrafts)
        {
            leads = leads.Where(lead => !lead.IsDraft);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            leads = leads.Where(lead =>
                lead.LeadReference.ToLower().Contains(term)
                || lead.FirstName.ToLower().Contains(term)
                || (lead.LastName != null && lead.LastName.ToLower().Contains(term))
                || (lead.EmailAddress != null && lead.EmailAddress.ToLower().Contains(term))
                || (lead.MobileNumber != null && lead.MobileNumber.Contains(term)));
        }

        if (filter.CampaignId is not null)
        {
            leads = leads.Where(lead => lead.CampaignId == filter.CampaignId);
        }

        if (filter.OwnerUserId is not null)
        {
            leads = leads.Where(lead => lead.OwnerUserId == filter.OwnerUserId);
        }

        if (filter.Status is not null)
        {
            leads = leads.Where(lead => lead.Status == filter.Status);
        }

        if (filter.SlaState is not null)
        {
            leads = leads.Where(lead => lead.SlaState == filter.SlaState);
        }

        if (filter.Temperature is not null)
        {
            leads = leads.Where(lead => lead.Temperature == filter.Temperature);
        }

        if (filter.DonationPotential is not null)
        {
            leads = leads.Where(lead => lead.DonationPotential == filter.DonationPotential);
        }

        if (!string.IsNullOrWhiteSpace(filter.PreferredLanguage))
        {
            leads = leads.Where(lead => lead.PreferredLanguage == filter.PreferredLanguage);
        }

        if (!string.IsNullOrWhiteSpace(filter.TeamCode))
        {
            leads = leads.Where(lead => lead.TeamCode == filter.TeamCode);
        }

        if (filter.LastContactOutcome is not null)
        {
            leads = leads.Where(lead => lead.LastContactOutcome == filter.LastContactOutcome);
        }

        if (filter.DueBeforeUtc is not null)
        {
            leads = leads.Where(lead => lead.NextActionDueUtc != null && lead.NextActionDueUtc <= filter.DueBeforeUtc);
        }

        if (filter.DueAfterUtc is not null)
        {
            leads = leads.Where(lead => lead.NextActionDueUtc != null && lead.NextActionDueUtc >= filter.DueAfterUtc);
        }

        // ASSIGNED MEANS "HAS AN OWNER", and it has to be asked as a null check rather than as an
        // owner id, because a null OwnerUserId on the filter already means "do not filter by
        // owner". The Lead Queue's Unassigned tab is the entry point to the Assignment Board.
        if (filter.AssignmentState is not null)
        {
            leads = filter.AssignmentState == LeadAssignmentState.Unassigned
                ? leads.Where(lead => lead.OwnerUserId == null)
                : leads.Where(lead => lead.OwnerUserId != null);
        }

        // A CONVERTED LEAD LEAVES THE QUEUE. The document is explicit: on conversion the lead is
        // removed from the Lead Work Queue and added to the Donor List. Hiding them by default is
        // what makes that true on screen; the Converted Leads tab is how they are seen again.
        if (filter.IsConverted is not null)
        {
            leads = filter.IsConverted == true
                ? leads.Where(lead => lead.ConvertedDonorId != null)
                : leads.Where(lead => lead.ConvertedDonorId == null);
        }

        var total = await leads.CountAsync(cancellationToken);

        // Overdue work first, then whatever is due soonest. A lead with no due date sorts last
        // rather than first, which is why the null check is part of the ordering.
        // RECENTLY ADDED IS A DIFFERENT QUESTION FROM WHAT IS DUE. The default below is a work
        // queue - overdue first, then soonest - and sorting that way would put a lead captured
        // two minutes ago at the bottom, which is the opposite of what the tab is for.
        var ordered = filter.NewestFirst == true
            ? leads.OrderByDescending(lead => lead.CreatedAtUtc)
            : leads
                .OrderBy(lead => lead.NextActionDueUtc == null)
                .ThenBy(lead => lead.NextActionDueUtc)
                .ThenByDescending(lead => lead.CreatedAtUtc);

        var items = await ordered
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<Lead>(items, total, filter.Page, filter.PageSize);
    }

    public Task<Lead?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Leads.Include(lead => lead.Campaign).FirstOrDefaultAsync(lead => lead.Id == id, cancellationToken);

    public Task<Lead?> GetConvertedFromAsync(Guid donorId, CancellationToken cancellationToken = default) =>
        context.Leads
            .Include(lead => lead.Campaign)
            .FirstOrDefaultAsync(lead => lead.ConvertedDonorId == donorId, cancellationToken);

    public Task<Lead?> GetWithAssignmentsAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Leads
            .Include(lead => lead.Campaign)
            .Include(lead => lead.Assignments)
            .FirstOrDefaultAsync(lead => lead.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Lead>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default) =>
        await context.Leads
            .Include(lead => lead.Campaign)
            .Where(lead => ids.Contains(lead.Id))
            .ToListAsync(cancellationToken);

    public async Task<int> GetMaxReferenceSequenceAsync(int year, CancellationToken cancellationToken = default)
    {
        var prefix = $"LED-{year:0000}-";

        var references = await context.Leads
            .Where(lead => lead.LeadReference.StartsWith(prefix))
            .Select(lead => lead.LeadReference)
            .ToListAsync(cancellationToken);

        return references.Count == 0
            ? 0
            : references
                .Select(reference => int.TryParse(reference[prefix.Length..], out var parsed) ? parsed : 0)
                .DefaultIfEmpty(0)
                .Max();
    }

    public async Task<IReadOnlyList<Lead>> FindDuplicateCandidatesAsync(
        Guid organisationId,
        string? email,
        string? mobileNumber,
        string? firstName,
        string? lastName,
        Guid? excludingId,
        CancellationToken cancellationToken = default)
    {
        var normalisedEmail = email?.Trim().ToLowerInvariant();
        var normalisedPhone = mobileNumber?.Trim();
        var normalisedName = string.Join(' ', new[] { firstName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalisedEmail)
            && string.IsNullOrWhiteSpace(normalisedPhone)
            && string.IsNullOrWhiteSpace(normalisedName))
        {
            return [];
        }

        return await context.Leads
            .Where(lead => lead.OrganisationId == organisationId
                           && lead.Status != LeadStatus.Converted
                           && (excludingId == null || lead.Id != excludingId)
                           && ((normalisedEmail != null && lead.EmailAddress != null && lead.EmailAddress.ToLower() == normalisedEmail)
                               || (normalisedPhone != null && lead.MobileNumber == normalisedPhone)
                               || (normalisedName != string.Empty
                                   && (lead.FirstName + " " + (lead.LastName ?? string.Empty)).Trim().ToLower() == normalisedName)))
            .Take(20)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetOpenWorkCountsByOwnerAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var counts = await context.Leads
            .Where(lead => lead.OrganisationId == organisationId
                           && !lead.IsDraft
                           && lead.OwnerUserId != null
                           && OpenStatuses.Contains(lead.Status))
            .GroupBy(lead => lead.OwnerUserId!.Value)
            .Select(group => new { OwnerUserId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(entry => entry.OwnerUserId, entry => entry.Count);
    }

    public async Task<IReadOnlyList<(Guid UserId, string Name, string? TeamCode)>> GetKnownOwnersAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        // DON has no user table: IAM owns those. The list therefore starts with the Organisation's
        // ACTUAL PEOPLE, read from the identity tables, and the names already recorded on leads and
        // assignments are folded in after.
        //
        // It used to be built from those recorded names ALONE, which could only ever contain
        // somebody who was already on it: a new Organisation has no leads, so no owner names, so
        // nobody to assign to, so no lead ever gets an owner. The selector was empty exactly when
        // it was needed.
        var fromDirectory = await people.GetAssignableAsync(organisationId, cancellationToken);
        var fromLeads = await context.Leads
            .Where(lead => lead.OrganisationId == organisationId && lead.OwnerUserId != null && lead.OwnerName != null)
            .Select(lead => new { UserId = lead.OwnerUserId!.Value, Name = lead.OwnerName!, lead.TeamCode })
            .Distinct()
            .ToListAsync(cancellationToken);

        var fromAssignments = await context.LeadAssignments
            .Where(assignment => assignment.OrganisationId == organisationId)
            .Select(assignment => new { UserId = assignment.NewOwnerUserId, Name = assignment.NewOwnerName, TeamCode = (string?)null })
            .Distinct()
            .ToListAsync(cancellationToken);

        return
        [
            .. fromDirectory
                .Select(person => new { UserId = person.UserId, Name = person.Name, TeamCode = (string?)null })
                .Concat(fromLeads)
                .Concat(fromAssignments)
                .GroupBy(owner => owner.UserId)
                .Select(group =>
                {
                    var first = group.First();
                    var team = group.FirstOrDefault(owner => owner.TeamCode != null)?.TeamCode;
                    return (group.Key, first.Name, team);
                })
                .OrderBy(owner => owner.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    public async Task<IReadOnlyDictionary<string, int>> GetStatusCountsAsync(
        Guid organisationId,
        AccessScope scope,
        CancellationToken cancellationToken = default)
    {
        // The totals on the queue header have to obey the same scope as the rows themselves,
        // otherwise the count would tell somebody about work they are not allowed to see.
        var leads = ApplyScope(context.Leads.Where(lead => lead.OrganisationId == organisationId), scope)
            .Where(lead => !lead.IsDraft);

        var counts = await leads
            .GroupBy(lead => lead.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(entry => entry.Status.ToString(), entry => entry.Count, StringComparer.Ordinal);
    }

    public async Task<LeadQueueSummaryResponse> GetQueueSummaryAsync(
        Guid organisationId,
        AccessScope scope,
        CancellationToken cancellationToken = default)
    {
        // Same scope filter and the same "not a draft" rule as the rows, so a card can never
        // report work the caller is not allowed to open.
        var leads = ApplyScope(context.Leads.Where(lead => lead.OrganisationId == organisationId), scope)
            .Where(lead => !lead.IsDraft);

        // ONE QUERY FOR ALL SIX. GroupBy(1) collapses to a single row of aggregates, so the six
        // cards cost one round trip rather than six counts over the same table.
        var summary = await leads
            .GroupBy(_ => 1)
            .Select(group => new LeadQueueSummaryResponse(
                group.Count(),
                group.Count(lead => lead.OwnerUserId == null),
                group.Count(lead => lead.OwnerUserId != null),
                group.Count(lead => lead.Temperature == LeadTemperature.Hot),
                group.Count(lead => lead.Status == LeadStatus.Converted || lead.ConvertedDonorId != null),
                group.Count(lead => lead.DonationPotential == DonationPotential.High)))
            .FirstOrDefaultAsync(cancellationToken);

        // An empty queue produces no group at all, and six zeroes is the honest answer.
        return summary ?? new LeadQueueSummaryResponse(0, 0, 0, 0, 0, 0);
    }

    public void Add(Lead lead) => context.Leads.Add(lead);

    public void Remove(Lead lead) => context.Leads.Remove(lead);

    public void AddAssignment(LeadAssignment assignment) => context.LeadAssignments.Add(assignment);

    public async Task<IReadOnlyList<LeadAssignment>> GetAssignmentHistoryAsync(
        Guid leadId,
        CancellationToken cancellationToken = default) =>
        await context.LeadAssignments
            .Where(assignment => assignment.LeadId == leadId)
            .OrderByDescending(assignment => assignment.EffectiveAtUtc)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The scope gate. Organisation always; then, for a caller who carries only narrowing
    /// scopes, ownership plus whichever campaign, geography and explicit-record scopes their
    /// token actually named. Nothing in this class queries Leads without going through here.
    /// </summary>
    private static IQueryable<Lead> ApplyScope(IQueryable<Lead> leads, AccessScope scope)
    {
        leads = leads.Where(lead => lead.OrganisationId == scope.OrganisationId);

        if (scope.IsOrganisationWide)
        {
            return leads;
        }

        // An explicit-record scope is the narrowest of all: it names the exact rows, so it
        // replaces the ownership test rather than adding to it.
        var explicitRecordIds = scope.ExplicitRecordIds;
        if (explicitRecordIds.Count > 0)
        {
            return leads.Where(lead => explicitRecordIds.Contains(lead.Id));
        }

        leads = leads.Where(lead => lead.OwnerUserId == scope.UserId);

        var campaignIds = scope.CampaignIds;
        if (campaignIds.Count > 0)
        {
            leads = leads.Where(lead => campaignIds.Contains(lead.CampaignId));
        }

        var geographyCodes = scope.GeographyCodes;
        if (geographyCodes.Count > 0)
        {
            leads = leads.Where(lead => lead.GeographyCode != null && geographyCodes.Contains(lead.GeographyCode));
        }

        return leads;
    }
}
