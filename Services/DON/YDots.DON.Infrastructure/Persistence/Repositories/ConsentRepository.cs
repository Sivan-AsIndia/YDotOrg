using Microsoft.EntityFrameworkCore;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.DTOs;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of the consent repository. No update method: consent is append only.</summary>
public sealed class ConsentRepository(DonDbContext context) : IConsentRepository
{
    public async Task<PagedResponse<Consent>> SearchAsync(
        ConsentSearchFilter filter,
        AccessScope scope,
        CancellationToken cancellationToken = default)
    {
        var consents = context.Consents
            .Include(consent => consent.Donor)
            .Where(consent => consent.OrganisationId == scope.OrganisationId);

        // A caller restricted to their own records only sees consent for donors they own.
        if (scope.IsOwnRecordsOnly)
        {
            consents = consents.Where(consent =>
                consent.Donor != null && consent.Donor.RelationshipOwnerUserId == scope.UserId);
        }

        // Superseded and withdrawn rows are history: they appear only when asked for.
        if (!filter.IncludeHistory)
        {
            consents = consents.Where(consent => consent.Status != ConsentStatus.Superseded);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            consents = consents.Where(consent =>
                consent.Purpose.ToLower().Contains(term)
                || consent.Name.ToLower().Contains(term)
                || (consent.Donor != null && consent.Donor.DonorNumber.ToLower().Contains(term)));
        }

        if (filter.DonorId is not null)
        {
            consents = consents.Where(consent => consent.DonorId == filter.DonorId);
        }

        if (filter.LeadId is not null)
        {
            consents = consents.Where(consent => consent.LeadId == filter.LeadId);
        }

        if (filter.Channel is not null)
        {
            consents = consents.Where(consent => consent.Channel == filter.Channel);
        }

        if (filter.ConsentState is not null)
        {
            consents = consents.Where(consent => consent.ConsentState == filter.ConsentState);
        }

        if (filter.Status is not null)
        {
            consents = consents.Where(consent => consent.Status == filter.Status);
        }

        if (!string.IsNullOrWhiteSpace(filter.NoticeVersion))
        {
            consents = consents.Where(consent => consent.NoticeVersion == filter.NoticeVersion);
        }

        if (filter.EffectiveAfterUtc is not null)
        {
            consents = consents.Where(consent => consent.EffectiveAtUtc >= filter.EffectiveAfterUtc);
        }

        if (filter.EffectiveBeforeUtc is not null)
        {
            consents = consents.Where(consent => consent.EffectiveAtUtc <= filter.EffectiveBeforeUtc);
        }

        var total = await consents.CountAsync(cancellationToken);

        var items = await consents
            .OrderByDescending(consent => consent.EffectiveAtUtc)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<Consent>(items, total, filter.Page, filter.PageSize);
    }

    public Task<Consent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Consents.Include(consent => consent.Donor)
            .FirstOrDefaultAsync(consent => consent.Id == id, cancellationToken);

    public Task<Consent?> GetCurrentAsync(Guid donorId, ConsentChannel channel, CancellationToken cancellationToken = default) =>
        context.Consents
            .Where(consent => consent.DonorId == donorId
                              && consent.Channel == channel
                              && consent.Status == ConsentStatus.Active)
            .OrderByDescending(consent => consent.EffectiveAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Consent>> GetCurrentForDonorAsync(Guid donorId, CancellationToken cancellationToken = default)
    {
        // "Current" means the newest non-superseded row for each channel. Withdrawn rows are
        // included on purpose: a refusal is a current fact and the follow-up planner needs it.
        var rows = await context.Consents
            .Where(consent => consent.DonorId == donorId && consent.Status != ConsentStatus.Superseded)
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .GroupBy(consent => consent.Channel)
                .Select(group => group.OrderByDescending(consent => consent.EffectiveAtUtc).First())
                .OrderBy(consent => consent.Channel)
        ];
    }

    public async Task<IReadOnlyList<Consent>> GetHistoryAsync(Guid donorId, CancellationToken cancellationToken = default) =>
        await context.Consents
            .Where(consent => consent.DonorId == donorId)
            .OrderByDescending(consent => consent.EffectiveAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Consent>> GetForLeadAsync(Guid leadId, CancellationToken cancellationToken = default) =>
        await context.Consents
            .Where(consent => consent.LeadId == leadId)
            .OrderBy(consent => consent.Channel)
            .ToListAsync(cancellationToken);

    public void Add(Consent consent) => context.Consents.Add(consent);
}
