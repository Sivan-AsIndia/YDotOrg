using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Domain.Common;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the Payments module, and also its unit of work.
///
/// IT SHARES ONE DATABASE WITH IAM, DON AND CAM, so it needs its own migrations history table.
/// With the default <c>__EFMigrationsHistory</c> the four services would read and write the same
/// list: EF would see the other services' migration ids, report them as pending or unknown, and
/// <c>dotnet ef migrations list</c> would be wrong for all four. The tables never clash because
/// IAM owns <c>iam_*</c> and <c>gm_*</c>, DON owns <c>don_*</c>, CAM owns <c>cam_*</c> and PAY
/// owns <c>pay_*</c>.
///
/// THE ISOLATION MATTERS MORE HERE THAN ANYWHERE ELSE ON THE PLATFORM. These tables hold money:
/// donation amounts, gateway references, receipt numbers and refund decisions. A read that
/// crossed an Organisation boundary would not leak a name, it would show one charity another
/// charity's income - so the query filter is applied off the marker interface exactly as it is
/// in IAM and CAM, with nothing left to a handler to remember.
///
/// <see cref="ExecuteInTransactionAsync"/> IS THE ONE ADDITION over the other services' contexts.
/// Applying a gateway event has to read the current state, decide and write, and two capture
/// webhooks arriving at once would otherwise both read "not yet paid" and both record a donation.
/// </summary>
public class PaymentDbContext(
    DbContextOptions<PaymentDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : DbContext(options), IUnitOfWork
{
    /// <summary>Exposed so the query filters can read the current Organisation as each query runs.</summary>
    internal ITenantContext TenantContext => tenantContext;

    // ---- The donation flow ---------------------------------------------------------------

    public DbSet<DonationIntent> DonationIntents => Set<DonationIntent>();

    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();

    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();

    public DbSet<Donation> Donations => Set<Donation>();

    // ---- Receipts ----------------------------------------------------------------------------

    public DbSet<Receipt> Receipts => Set<Receipt>();

    public DbSet<ReceiptDelivery> ReceiptDeliveries => Set<ReceiptDelivery>();

    /// <summary>
    /// The per-Organisation, per-financial-year receipt counter.
    ///
    /// A TABLE RATHER THAN A DATABASE SEQUENCE, because the counter is scoped to two values and a
    /// sequence is global. Row-locking one row per (Organisation, year) is what lets two
    /// receipts issued in the same instant take different numbers without serialising every
    /// receipt on the platform behind one lock.
    /// </summary>
    public DbSet<ReceiptNumberCounter> ReceiptNumberCounters => Set<ReceiptNumberCounter>();

    // ---- Cases ------------------------------------------------------------------------------------

    public DbSet<RefundCase> RefundCases => Set<RefundCase>();

    public DbSet<ChargebackCase> ChargebackCases => Set<ChargebackCase>();

    // ---- Configuration and audit ---------------------------------------------------------------------

    public DbSet<PaymentGatewayAccount> GatewayAccounts => Set<PaymentGatewayAccount>();

    public DbSet<PaymentAuditEvent> AuditEvents => Set<PaymentAuditEvent>();

    /// <summary>
    /// IAM's payment gateway configuration table, READ-ONLY and EXCLUDED FROM THIS SERVICE'S
    /// MIGRATIONS.
    ///
    /// It is what a TenantAdmin fills in on the configuration screen, and it is read on the
    /// donation path so a merchant credential entered there is honoured without a deployment.
    /// The four services share one database, so this is a local read rather than a call to IAM
    /// in the middle of taking money - see the entity for why that trade was made and what it
    /// costs. IAM owns the DDL; nothing here writes to it.
    /// </summary>
    internal DbSet<Gateway.TenantGatewayConfiguration> TenantGatewayConfigurations =>
        Set<Gateway.TenantGatewayConfiguration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);

        // Applied LAST, because a filter has to be attached after the entity is fully configured
        // or EF quietly drops it.
        ApplyTenantQueryFilters(modelBuilder);
    }

    /// <summary>
    /// THE READ-SIDE HALF OF THE ISOLATION.
    ///
    /// Driven off <see cref="ITenantOwned"/> rather than written out per entity: a hand-written
    /// filter per table is one line somebody eventually forgets, and a missing filter on a
    /// donations table is one charity reading another's income.
    ///
    /// <c>PaymentAuditEvent</c> is deliberately NOT marked and therefore not filtered - see the
    /// entity for why an audit row must stay visible even when no Organisation is resolved.
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
    /// Runs a body inside an explicit database transaction.
    ///
    /// AN AMBIENT TRANSACTION IS REUSED rather than nested. EF does not support true nested
    /// transactions, and a handler that calls another handler - applying a capture event calls
    /// receipt issuing, which opens its own - would otherwise throw. Joining the outer one keeps
    /// the whole operation atomic, which is what both callers actually wanted.
    /// </summary>
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        await using IDbContextTransaction transaction =
            await Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = await operation(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// THE WRITE-SIDE HALF OF THE ISOLATION.
    ///
    /// A new Organisation-owned row gets its owner from the request context, never from the
    /// caller - so even if a request body carried a TenantId and a handler copied it onto the
    /// entity, it is overwritten here.
    ///
    /// A ROW THAT ALREADY HAS AN OWNER IS LEFT ALONE, which matters more in this service than
    /// the others: the webhook and public donation paths resolve an Organisation from an intent
    /// reference and set it explicitly, and the ambient context may legitimately be empty.
    /// </summary>
    private void StampTenantOwnership()
    {
        var tenantId = tenantContext.TenantId;

        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.TenantId == Guid.Empty && tenantId.HasValue)
                    {
                        entry.Entity.TenantId = tenantId.Value;
                    }

                    break;

                case EntityState.Modified:
                    // Ownership is immutable. A donation cannot be moved between charities.
                    entry.Property(nameof(ITenantOwned.TenantId)).IsModified = false;
                    entry.Property(nameof(ITenantOwned.BusinessUnitId)).IsModified = false;
                    break;
            }
        }
    }

    /// <summary>
    /// Fills in who did it and when, and moves the concurrency version forward.
    ///
    /// <c>CreatedByUserId</c> IS OFTEN Guid.Empty HERE, and that is correct rather than a bug: a
    /// public donation is made by a stranger with no account, so there is no user to record. The
    /// audit row carries the Organisation and the correlation id instead.
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

/// <summary>
/// The per-Organisation, per-financial-year receipt counter.
///
/// IT LIVES IN THE INFRASTRUCTURE LAYER RATHER THAN THE DOMAIN, deliberately. It is not a
/// business concept - no rule in the module brief mentions it - it is the mechanism that makes
/// a gap-free sequential number possible under concurrency. The domain knows receipts have
/// numbers; how they are allocated safely is a persistence problem.
/// </summary>
public sealed class ReceiptNumberCounter
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>"2026-27". Part of the scope the sequence is unique within.</summary>
    public string FinancialYear { get; set; } = string.Empty;

    /// <summary>The last number issued. The next receipt takes this plus one.</summary>
    public int LastNumber { get; set; }
}
