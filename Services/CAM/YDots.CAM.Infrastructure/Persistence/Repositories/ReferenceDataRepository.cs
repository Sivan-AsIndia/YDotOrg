using Microsoft.EntityFrameworkCore;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Domain.Common.Enums;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Infrastructure.Persistence.Repositories;

/// <summary>
/// The three global reference tables.
///
/// NO ORGANISATION FILTER APPLIES to any of these, because none of the three entities is
/// <c>ITenantOwned</c>. That is deliberate rather than an omission: their codes appear in
/// tracking URLs and in cross-Organisation attribution reporting, so one code has to mean one
/// thing platform-wide.
///
/// The reads are untracked. These rows are looked up constantly to resolve a name for a
/// response and are changed by nobody but SuperAdmin, so tracking them would fill the change
/// tracker with entities no handler intends to modify.
/// </summary>
public sealed class ReferenceDataRepository(CampaignDbContext context) : IReferenceDataRepository
{
    public Task<Channel?> GetChannelAsync(Guid id, CancellationToken cancellationToken) =>
        context.Channels.AsNoTracking().FirstOrDefaultAsync(channel => channel.Id == id, cancellationToken);

    public Task<Source?> GetSourceAsync(Guid id, CancellationToken cancellationToken) =>
        context.Sources.AsNoTracking().FirstOrDefaultAsync(source => source.Id == id, cancellationToken);

    public Task<Medium?> GetMediumAsync(Guid id, CancellationToken cancellationToken) =>
        context.Mediums.AsNoTracking().FirstOrDefaultAsync(medium => medium.Id == id, cancellationToken);

    public Task<Channel?> GetChannelByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.Channels.AsNoTracking().FirstOrDefaultAsync(channel => channel.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Channel>> GetChannelsAsync(
        bool activeOnly, CancellationToken cancellationToken) =>
        await context.Channels
            .AsNoTracking()
            .Where(channel => !activeOnly || channel.Status == Status.Active)
            .OrderBy(channel => channel.SortOrder)
            .ThenBy(channel => channel.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Source>> GetSourcesAsync(
        bool activeOnly, CancellationToken cancellationToken) =>
        await context.Sources
            .AsNoTracking()
            .Where(source => !activeOnly || source.Status == Status.Active)
            .OrderBy(source => source.SortOrder)
            .ThenBy(source => source.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Medium>> GetMediumsAsync(
        bool activeOnly, CancellationToken cancellationToken) =>
        await context.Mediums
            .AsNoTracking()
            .Where(medium => !activeOnly || medium.Status == Status.Active)
            .OrderBy(medium => medium.SortOrder)
            .ThenBy(medium => medium.Name)
            .ToListAsync(cancellationToken);
}
