namespace YDots.CAM.Domain.Common;

/// <summary>
/// Base type for every persisted entity. Every identifier in YDot is a UUID (Guid),
/// generated client-side so an aggregate has an identity before it is ever saved.
///
/// THIS USED TO CARRY THE AUDIT COLUMNS TOO, and the split matters. A join row such as
/// <c>CampaignChannel</c> is created and destroyed with its parent and has nothing
/// meaningful to say about who edited it; giving it CreatedBy, UpdatedBy and a concurrency
/// version cost five columns per row to record nothing. The audit columns now live on
/// <see cref="AuditEntity"/>, which the entities that are genuinely edited derive from.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}
