using Microsoft.EntityFrameworkCore;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Infrastructure.Persistence.Repositories;

/// <summary>
/// Receipts, their deliveries, and the allocator behind the receipt number.
///
/// <see cref="AllocateNextReceiptNumberAsync"/> IS THE REASON THIS CLASS IS NOT TRIVIAL. Every
/// other reference on the platform is random and unique; a tax receipt number has to be
/// SEQUENTIAL and GAP-FREE within an Organisation and a financial year, because a tax authority
/// reads a gap as a destroyed receipt and asks about it.
/// </summary>
public sealed class ReceiptRepository(PaymentDbContext context) : IReceiptRepository
{
    public async Task AddAsync(Receipt receipt, CancellationToken cancellationToken) =>
        await context.Receipts.AddAsync(receipt, cancellationToken);

    public Task<Receipt?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        context.Receipts
            .Include(receipt => receipt.Deliveries)
            .Include(receipt => receipt.Donation)
            .FirstOrDefaultAsync(receipt => receipt.Id == id, cancellationToken);

    /// <summary>
    /// Every receipt version issued against a donation, oldest first.
    ///
    /// ORDERED BY VERSION rather than by date: two corrections issued in the same second would
    /// order arbitrarily by timestamp, and the version chain is what makes "which one superseded
    /// which" answerable.
    /// </summary>
    public async Task<IReadOnlyList<Receipt>> GetForDonationAsync(
        Guid donationId, CancellationToken cancellationToken) =>
        await context.Receipts
            .Include(receipt => receipt.Deliveries)
            .Where(receipt => receipt.DonationId == donationId)
            .OrderBy(receipt => receipt.VersionNumber)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The receipt currently standing for a donation.
    ///
    /// ONLY <c>Issued</c> COUNTS. A corrected or voided receipt still exists - it must, because a
    /// donor may have claimed relief on it - but it is no longer the document that represents the
    /// gift, and treating it as current would block a correction from being issued.
    /// </summary>
    public Task<Receipt?> GetValidForDonationAsync(Guid donationId, CancellationToken cancellationToken) =>
        context.Receipts
            .Include(receipt => receipt.Deliveries)
            .Where(receipt => receipt.DonationId == donationId)
            .Where(receipt => receipt.Status == ReceiptStatus.Issued)
            .OrderByDescending(receipt => receipt.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddDeliveryAsync(ReceiptDelivery delivery, CancellationToken cancellationToken) =>
        await context.ReceiptDeliveries.AddAsync(delivery, cancellationToken);

    /// <summary>
    /// Allocates the next receipt number for an Organisation and financial year.
    ///
    /// THE ROW LOCK IS THE WHOLE MECHANISM. Two receipts issued in the same instant would
    /// otherwise both read the same last number and both take it: one insert would survive, the
    /// other would fail on the unique index, and the caller would see an error on an operation
    /// that had nothing wrong with it.
    ///
    /// <c>FOR UPDATE</c> serialises the two against each other and NOTHING ELSE, because the
    /// lock is on one row per (Organisation, year). Receipts for other charities, and for other
    /// years, are unaffected - which is why this is a counter table rather than a database
    /// sequence or a table-level lock.
    ///
    /// IT MUST BE CALLED INSIDE A TRANSACTION. Outside one the lock would be released the moment
    /// the statement finished and would guarantee nothing; the caller's
    /// <c>ExecuteInTransactionAsync</c> is what holds it until the receipt is actually written.
    /// The guard below refuses rather than issuing a number that only looks safe.
    ///
    /// FIRST USE HAS NO ROW, so one is inserted. The unique index means two simultaneous first
    /// issues cannot both insert - the loser retries and finds the winner's row, which is the
    /// one place a retry is genuinely correct rather than a way of hiding a race.
    /// </summary>
    public async Task<int> AllocateNextReceiptNumberAsync(
        Guid tenantId, string financialYear, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "AllocateNextReceiptNumberAsync must run inside a transaction. Without one the "
                + "row lock is released immediately and two receipts can take the same number.");
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            // The lock. Raw SQL because EF has no expression for FOR UPDATE, and the parameters
            // are bound rather than interpolated into the text.
            var counter = await context.ReceiptNumberCounters
                .FromSql(
                    $"""
                     SELECT * FROM pay_receipt_number_counters
                     WHERE tenant_id = {tenantId} AND financial_year = {financialYear}
                     FOR UPDATE
                     """)
                .FirstOrDefaultAsync(cancellationToken);

            if (counter is not null)
            {
                counter.LastNumber += 1;

                await context.SaveChangesAsync(cancellationToken);

                return counter.LastNumber;
            }

            // No counter yet: this Organisation's first receipt of the year.
            try
            {
                var created = new ReceiptNumberCounter
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    FinancialYear = financialYear,
                    LastNumber = 1
                };

                await context.ReceiptNumberCounters.AddAsync(created, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);

                return created.LastNumber;
            }
            catch (DbUpdateException)
            {
                // Somebody else inserted the row between our read and our write. Detach the row
                // we failed to add - otherwise the next SaveChanges would retry the same insert
                // - and go round once to take a number from theirs.
                foreach (var entry in context.ChangeTracker
                             .Entries<ReceiptNumberCounter>()
                             .Where(entry => entry.State == EntityState.Added)
                             .ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }
        }

        throw new InvalidOperationException(
            $"Could not allocate a receipt number for financial year {financialYear} after the "
            + "counter row was created concurrently. This indicates the counter table is being "
            + "written by something outside the allocator.");
    }
}
