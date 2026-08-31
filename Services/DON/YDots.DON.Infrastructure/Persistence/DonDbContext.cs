using Microsoft.EntityFrameworkCore;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Domain.Common;
using YDots.DON.Domain.Entities;

namespace YDots.DON.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the Donors section, and also its unit of work: one
/// SaveChangesAsync per request stamps the audit columns and bumps the concurrency version.
///
/// There is no Identity table here. IAM owns users and roles; DON references a user by Guid and
/// never joins to one, which is what keeps the two services independently deployable even
/// though they share a database.
/// </summary>
public class DonDbContext(
    DbContextOptions<DonDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : DbContext(options), IUnitOfWork
{
    /// <summary>
    /// Exposed so the query filters can read the current Organisation as each query runs.
    ///
    /// Internal rather than public: it is here for <c>OrganisationQueryFilter</c> to close over,
    /// not for a repository to consult - a repository that read it would be re-deriving the
    /// filter the model already applies.
    /// </summary>
    internal ITenantContext TenantContext => tenantContext;

    public DbSet<Donor> Donors => Set<Donor>();

    public DbSet<DonorContact> DonorContacts => Set<DonorContact>();

    public DbSet<Consent> Consents => Set<Consent>();

    public DbSet<DonorInteraction> DonorInteractions => Set<DonorInteraction>();

    public DbSet<DonorMergeCase> DonorMergeCases => Set<DonorMergeCase>();

    public DbSet<DonorTag> DonorTags => Set<DonorTag>();

    public DbSet<Campaign> Campaigns => Set<Campaign>();

    public DbSet<Lead> Leads => Set<Lead>();

    public DbSet<LeadAssignment> LeadAssignments => Set<LeadAssignment>();

    public DbSet<DonorIdentityVerification> DonorIdentityVerifications => Set<DonorIdentityVerification>();

    public DbSet<FollowUpTask> FollowUpTasks => Set<FollowUpTask>();

    public DbSet<DonorPromise> DonorPromises => Set<DonorPromise>();

    public DbSet<DonorDocument> DonorDocuments => Set<DonorDocument>();

    public DbSet<DonorDonationSummary> DonorDonationSummaries => Set<DonorDonationSummary>();

    public DbSet<DonorAuditEvent> AuditEvents => Set<DonorAuditEvent>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // One IEntityTypeConfiguration per table, all in Persistence/Configurations.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DonDbContext).Assembly);

        // Applied LAST, because a filter has to be attached after the entity is fully
        // configured or EF quietly drops it.
        ApplyOrganisationQueryFilters(modelBuilder);
    }

    /// <summary>
    /// THE READ-SIDE HALF OF THE ISOLATION.
    ///
    /// Every entity marked <see cref="IOrganisationOwned"/> gets a filter of
    /// "OrganisationId == current", driven off the marker interface rather than written out per
    /// entity.
    ///
    /// THIS IS DEFENCE IN DEPTH, NOT A REPLACEMENT for the explicit Where clauses in the
    /// repositories. A filter stops a FORGOTTEN Where clause leaking rows; it does not
    /// authorise anything, and <c>IgnoreQueryFilters()</c> walks straight past it. Both layers
    /// stay, which is the right posture for the boundary between two Organisations' donor
    /// records.
    /// </summary>
    private void ApplyOrganisationQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (entityType.IsOwned() || clrType.IsAbstract)
            {
                continue;
            }

            if (typeof(IOrganisationOwned).IsAssignableFrom(clrType))
            {
                OrganisationQueryFilter.Apply(modelBuilder, clrType, this);
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampOrganisationOwnership();
        StampAuditColumns();

        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// THE WRITE-SIDE HALF OF THE ISOLATION.
    ///
    /// A new Organisation-owned row gets its OrganisationId from the request context, never
    /// from the caller. Even if a request body carried one and a handler foolishly copied it
    /// onto the entity, it is overwritten here before it reaches the database.
    ///
    /// An existing row can never have its owner changed. Silently reverting the attempt rather
    /// than throwing keeps a legitimate save working while making the reassignment impossible;
    /// the audit trail is where a deliberate attempt shows up.
    ///
    /// A HANDLER THAT ALREADY SET IT IS LEFT ALONE, which matters for the seeder and for the
    /// few flows that legitimately write on behalf of a specific Organisation - the stamp only
    /// fills in a blank.
    /// </summary>
    private void StampOrganisationOwnership()
    {
        var organisationId = tenantContext.OrganisationId;

        foreach (var entry in ChangeTracker.Entries<IOrganisationOwned>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.OrganisationId == Guid.Empty && organisationId.HasValue)
                    {
                        entry.Entity.OrganisationId = organisationId.Value;
                    }

                    break;

                case EntityState.Modified:
                    // Ownership is immutable. Revert any attempt to move a row between
                    // Organisations back to what the database already holds.
                    entry.Property(nameof(IOrganisationOwned.OrganisationId)).IsModified = false;
                    break;
            }
        }
    }

    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Fills in who did it and when, and moves the concurrency version forward. Handlers never
    /// have to remember the audit columns, and the version can never be set by a caller.
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
