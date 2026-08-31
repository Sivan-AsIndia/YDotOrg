using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Application.Common.Abstractions.Persistence;

/// <summary>Write-side access to receipts and their deliveries.</summary>
public interface IReceiptRepository
{
    Task AddAsync(Receipt receipt, CancellationToken cancellationToken);

    /// <summary>One receipt with its delivery history, tracked for editing.</summary>
    Task<Receipt?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every receipt version issued against a donation, oldest first.</summary>
    Task<IReadOnlyList<Receipt>> GetForDonationAsync(Guid donationId, CancellationToken cancellationToken);

    /// <summary>The current valid receipt for a donation, if any.</summary>
    Task<Receipt?> GetValidForDonationAsync(Guid donationId, CancellationToken cancellationToken);

    Task AddDeliveryAsync(ReceiptDelivery delivery, CancellationToken cancellationToken);

    /// <summary>
    /// Allocates the next receipt number for an Organisation and financial year.
    ///
    /// UNLIKE EVERY OTHER REFERENCE IN THE PLATFORM THIS IS SEQUENTIAL, because tax authorities
    /// expect receipt numbers to run in an unbroken series and a gap is something an auditor
    /// asks about.
    ///
    /// IT MUST BE CALLED INSIDE A TRANSACTION. The implementation takes a row lock on the
    /// counter so two receipts issued in the same instant cannot take the same number - which,
    /// with a unique index behind it, is the difference between a rare duplicate and none.
    /// </summary>
    Task<int> AllocateNextReceiptNumberAsync(
        Guid tenantId, string financialYear, CancellationToken cancellationToken);
}
