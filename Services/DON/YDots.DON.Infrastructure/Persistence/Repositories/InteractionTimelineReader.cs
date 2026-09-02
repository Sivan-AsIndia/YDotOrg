using Microsoft.EntityFrameworkCore;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Domain.Entities;

namespace YDots.DON.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class InteractionTimelineReader(DonDbContext context) : IInteractionTimelineReader
{
    public async Task<IReadOnlyList<DonorInteraction>> GetTimelineAsync(
        Guid? leadId,
        Guid? donorId,
        int maximumRows,
        CancellationToken cancellationToken = default)
    {
        if (leadId is null && donorId is null)
        {
            return [];
        }

        // ONE QUERY WITH AN OR, not two queries merged in memory. An interaction can only carry
        // one of the two ids, so there is nothing to de-duplicate - and letting the database do
        // the ordering means the row limit takes the newest overall rather than the newest of
        // each half.
        return await context.DonorInteractions
            .AsNoTracking()
            .Where(interaction =>
                (leadId != null && interaction.LeadId == leadId)
                || (donorId != null && interaction.DonorId == donorId))
            .OrderByDescending(interaction => interaction.OccurredAtUtc)
            .Take(maximumRows)
            .ToListAsync(cancellationToken);
    }
}
