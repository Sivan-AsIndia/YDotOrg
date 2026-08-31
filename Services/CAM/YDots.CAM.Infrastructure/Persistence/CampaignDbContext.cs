using Microsoft.EntityFrameworkCore;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Domain.Common;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the Campaign module, and also its unit of work.
///
/// IT SHARES ONE DATABASE WITH IAM AND DON, which is why it needs its own migrations history
/// table. With the default <c>__EFMigrationsHistory</c> the three services would read and write
/// the same list: EF would see the other services' migration ids, report them as pending or
/// unknown, and <c>dotnet ef migrations list</c> would be wrong for all three. The tables
/// themselves never clash because IAM owns <c>iam_*</c> and <c>gm_*</c>, DON owns <c>don_*</c>
/// and CAM owns <c>cam_*</c>.
///
/// THERE IS NO IDENTITY TABLE HERE. IAM owns users and roles; CAM references a user by Guid and
/// never joins to one, which is what keeps the two independently deployable even though they
/// share a database.
///
/// THREE THINGS HAPPEN AUTOMATICALLY, so no handler has to remember them: audit columns are
/// stamped on save, Organisation ownership is stamped on insert and frozen on update, and on
/// every READ a global query filter keeps one Organisation out of another.
///
/// THAT LAST ONE IS NEW, and it is the most important change in this file. Isolation used to
/// depend on every repository method remembering <c>Where(x =&gt; x.OrganisationId == ...)</c>.
/// One forgotten Where clause was one Organisation reading another's campaigns, and nothing in
/// the type system would have said so.
/// </summary>
public class CampaignDbContext(
    DbContextOptions<CampaignDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : DbContext(options), IUnitOfWork
{
    /// <summary>Exposed so the query filters can read the current Organisation as each query runs.</summary>
    internal ITenantContext TenantContext => tenantContext;

    // ---- Campaigns ----------------------------------------------------------------------

    public DbSet<Campaign> Campaigns => Set<Campaign>();

    public DbSet<CampaignOwner> CampaignOwners => Set<CampaignOwner>();

    public DbSet<CampaignChannel> CampaignChannels => Set<CampaignChannel>();

    public DbSet<CampaignLifecycleAction> CampaignLifecycleActions => Set<CampaignLifecycleAction>();

    // ---- Tracking assets --------------------------------------------------------------------

    public DbSet<TrackingAsset> TrackingAssets => Set<TrackingAsset>();

    public DbSet<TrackingAssetPlace> TrackingAssetPlaces => Set<TrackingAssetPlace>();

    public DbSet<TrackingAssetCustomField> TrackingAssetCustomFields => Set<TrackingAssetCustomField>();

    // ---- Readiness ----------------------------------------------------------------------------------

    public DbSet<CampaignReadinessCheck> CampaignReadinessChecks => Set<CampaignReadinessCheck>();

    public DbSet<CampaignReadinessBlocker> CampaignReadinessBlockers => Set<CampaignReadinessBlocker>();

    // ---- Budget and target plans --------------------------------------------------------------------

    public DbSet<BudgetTargetPlan> BudgetTargetPlans => Set<BudgetTargetPlan>();

    public DbSet<BudgetTargetPlanVersion> BudgetTargetPlanVersions => Set<BudgetTargetPlanVersion>();

    // ---- Attribution ---------------------------------------------------------------------------------

    public DbSet<AttributionCorrectionRequest> AttributionCorrectionRequests =>
        Set<AttributionCorrectionRequest>();

    // ---- Global reference data ----------------------------------------------------------------------
    //
    // Deliberately NOT Organisation-scoped. These codes appear in tracking URLs and in
    // attribution reporting that spans Organisations, so one code has to mean one thing
    // platform-wide.

    public DbSet<Channel> Channels => Set<Channel>();

    public DbSet<Source> Sources => Set<Source>();

    public DbSet<Medium> Mediums => Set<Medium>();

    // ---- Audit ---------------------------------------------------------------------------------------------

    public DbSet<CampaignAuditEvent> AuditEvents => Set<CampaignAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // One IEntityTypeConfiguration per table, all in Persistence/Configurations.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CampaignDbContext).Assembly);

        // Applied LAST, because a filter has to be attached after the entity is fully
        // configured or EF quietly drops it.
        ApplyTenantQueryFilters(modelBuilder);
    }

    /// <summary>
    /// THE READ-SIDE HALF OF THE ISOLATION.
    ///
    /// Every entity marked <see cref="ITenantOwned"/> gets a filter of "TenantId == current",
    /// driven off the marker interface rather than written out per entity. A hand-written
    /// <c>HasQueryFilter</c> per table is one line somebody eventually forgets, and a missing
    /// filter is invisible - the code compiles, the tests pass, and one Organisation quietly
    /// reads another.
    ///
    /// THIS IS DEFENCE IN DEPTH, NOT THE ONLY CONTROL. A filter stops a forgotten Where clause
    /// leaking rows; it does not authorise anything, and <c>IgnoreQueryFilters()</c> walks
    /// straight past it. Handlers still check, and the two places that legitimately bypass it -
    /// resolving a tracking reference from the public donation flow, and reading the audit trail
    /// - say so at the point of use.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (entityType.IsOwned() || clrType.IsAbstract)
            {
                continue;
            }

            if (typeof(ITenantOwned).IsAssignableFrom(clrType))
            {
                TenantQueryFilter.Apply(modelBuilder, clrType, this);
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTenantOwnership();
        StampAuditColumns();

        return base.SaveChangesAsync(cancellationToken);
    }

    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        SaveChangesAsync(cancellationToken);

    /// <summary>
    /// THE WRITE-SIDE HALF OF THE ISOLATION.
    ///
    /// A new Tenant-owned row gets its TenantId and BusinessUnitId from the request context,
    /// never from the caller. Even if a request body carried a TenantId and a handler foolishly
    /// copied it onto the entity, it is overwritten here before it reaches the database.
    ///
    /// An existing row can never have its owner changed. Silently reverting the attempt rather
    /// than throwing keeps a legitimate save working while making the reassignment impossible;
    /// the audit trail is where a deliberate attempt shows up.
    /// </summary>
    private void StampTenantOwnership()
    {
        var tenantId = tenantContext.TenantId;
        var businessUnitId = tenantContext.BusinessUnitId;

        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.TenantId == Guid.Empty && tenantId.HasValue)
                    {
                        entry.Entity.TenantId = tenantId.Value;
                    }

                    if (entry.Entity.BusinessUnitId == Guid.Empty && businessUnitId != Guid.Empty)
                    {
                        entry.Entity.BusinessUnitId = businessUnitId;
                    }

                    break;

                case EntityState.Modified:
                    // Ownership is immutable. Revert any attempt to move a row between
                    // Organisations back to what the database already holds.
                    entry.Property(nameof(ITenantOwned.TenantId)).IsModified = false;
                    entry.Property(nameof(ITenantOwned.BusinessUnitId)).IsModified = false;
                    break;
            }
        }
    }

    /// <summary>
    /// Fills in who did it and when, and moves the concurrency version forward.
    ///
    /// EVERY HANDLER USED TO DO THIS BY HAND - <c>campaign.UpdatedAtUtc = now;</c>,
    /// <c>campaign.Version++;</c> - and any handler that forgot broke concurrency detection
    /// silently, because a version that never moves makes every ExpectedVersion check pass.
    /// </summary>
    private void StampAuditColumns()
    {
        var now = clock.UtcNow;
        var actorId = currentUser.UserId;

        foreach (var entry in ChangeTracker.Entries<AuditEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedByUserId = entry.Entity.CreatedByUserId == Guid.Empty
                        ? actorId
                        : entry.Entity.CreatedByUserId;
                    entry.Entity.Version = 1;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    entry.Entity.UpdatedByUserId = actorId;
                    entry.Entity.Version += 1;
                    break;
            }
        }
    }
}
