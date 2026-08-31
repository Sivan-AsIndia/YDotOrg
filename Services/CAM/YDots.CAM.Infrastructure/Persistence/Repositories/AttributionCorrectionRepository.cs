using Microsoft.EntityFrameworkCore;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Infrastructure.Persistence.Repositories;

/// <summary>Write-side access to attribution correction requests.</summary>
public sealed class AttributionCorrectionRepository(CampaignDbContext context)
    : IAttributionCorrectionRepository
{
    public async Task AddAsync(
        AttributionCorrectionRequest request, CancellationToken cancellationToken) =>
        await context.AttributionCorrectionRequests.AddAsync(request, cancellationToken);

    public Task<AttributionCorrectionRequest?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken) =>
        context.AttributionCorrectionRequests
            .FirstOrDefaultAsync(request => request.Id == id, cancellationToken);

    public Task<AttributionCorrectionRequest?> GetOpenForDonationAsync(
        Guid donationId, CancellationToken cancellationToken) =>
        context.AttributionCorrectionRequests
            .FirstOrDefaultAsync(
                request => request.DonationId == donationId && !request.IsResolved,
                cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetDonationsWithOpenRequestsAsync(
        IReadOnlyCollection<Guid> donationIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(donationIds);

        if (donationIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = donationIds.Distinct().ToArray();

        var found = await context.AttributionCorrectionRequests
            .AsNoTracking()
            .Where(request => !request.IsResolved && ids.Contains(request.DonationId))
            .Select(request => request.DonationId)
            .ToListAsync(cancellationToken);

        return found.ToHashSet();
    }
}
