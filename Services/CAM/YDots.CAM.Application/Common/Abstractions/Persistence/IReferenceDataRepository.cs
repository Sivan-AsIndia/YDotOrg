using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// The three global reference tables: Channel, Source and Medium.
///
/// ONE INTERFACE FOR ALL THREE, replacing IChannelRepository, ISourceRepository and
/// IMediumRepository. Those were three copies of the same four methods against three tables
/// with identical shapes, and the third copy is where a filter eventually comes out wrong.
///
/// THESE TABLES ARE NOT ORGANISATION-SCOPED, which is why nothing here takes a TenantId and why
/// none of it is behind a query filter. Their codes appear in tracking URLs and in attribution
/// reporting that spans Organisations, so one code has to mean one thing platform-wide. They
/// are maintained by SuperAdmin and read by everybody.
/// </summary>
public interface IReferenceDataRepository
{
    Task<Channel?> GetChannelAsync(Guid id, CancellationToken cancellationToken);

    Task<Source?> GetSourceAsync(Guid id, CancellationToken cancellationToken);

    Task<Medium?> GetMediumAsync(Guid id, CancellationToken cancellationToken);

    Task<Channel?> GetChannelByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>Active channels, in display order.</summary>
    Task<IReadOnlyList<Channel>> GetChannelsAsync(bool activeOnly, CancellationToken cancellationToken);

    Task<IReadOnlyList<Source>> GetSourcesAsync(bool activeOnly, CancellationToken cancellationToken);

    Task<IReadOnlyList<Medium>> GetMediumsAsync(bool activeOnly, CancellationToken cancellationToken);
}
