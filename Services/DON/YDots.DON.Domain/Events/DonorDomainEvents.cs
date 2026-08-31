namespace YDots.DON.Domain.Events;

/// <summary>
/// Marker for the five domain events in section 10. They are plain records: a handler records
/// one, the audit writer and the outbox writer read it, and nothing else in the process needs
/// a dispatcher or a mediator to make that work.
/// </summary>
public interface IDonorDomainEvent
{
    Guid DonorId { get; }

    DateTimeOffset OccurredAtUtc { get; }
}

/// <summary>Raised by CreateDonorCommand once the aggregate has been staged.</summary>
public sealed record DonorCreatedDomainEvent(
    Guid DonorId,
    string DonorNumber,
    string DonorType,
    string Status,
    DateTimeOffset OccurredAtUtc) : IDonorDomainEvent;

/// <summary>Raised by UpdateDonorCommand after the expected version check passed.</summary>
public sealed record DonorUpdatedDomainEvent(
    Guid DonorId,
    string DonorNumber,
    long Version,
    DateTimeOffset OccurredAtUtc) : IDonorDomainEvent;

/// <summary>Raised by SubmitDonorCommand when the record moves to PendingApproval.</summary>
public sealed record DonorSubmittedDomainEvent(
    Guid DonorId,
    string DonorNumber,
    Guid SubmittedByUserId,
    DateTimeOffset OccurredAtUtc) : IDonorDomainEvent;

/// <summary>Raised by ApproveDonorCommand. Carries the decision so a rejection is visible too.</summary>
public sealed record DonorApprovedDomainEvent(
    Guid DonorId,
    string DonorNumber,
    bool Approved,
    Guid DecidedByUserId,
    DateTimeOffset OccurredAtUtc) : IDonorDomainEvent;

/// <summary>Raised by CancelDonorCommand. The reason is mandatory on the command.</summary>
public sealed record DonorCancelledDomainEvent(
    Guid DonorId,
    string DonorNumber,
    string Reason,
    DateTimeOffset OccurredAtUtc) : IDonorDomainEvent;
