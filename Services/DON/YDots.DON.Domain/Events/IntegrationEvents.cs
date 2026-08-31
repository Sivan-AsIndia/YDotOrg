namespace YDots.DON.Domain.Events;

/// <summary>
/// The two integration events from section 10. These are the only facts another section is
/// told about, and they are deliberately thin: an identifier, a reference and a state. No
/// contact value, no consent evidence and no document ever leaves the Donors boundary.
/// </summary>
public static class IntegrationEventNames
{
    public const string DonorCreatedV1 = "DonorCreatedV1";
    public const string DonorStatusChangedV1 = "DonorStatusChangedV1";
}

/// <summary>Published when a donor record first exists.</summary>
public sealed record DonorCreatedV1(
    Guid DonorId,
    string DonorNumber,
    string DonorType,
    Guid OrganisationId,
    DateTimeOffset OccurredAtUtc);

/// <summary>Published on every status move: Prospect to Active, Active to Archived, and so on.</summary>
public sealed record DonorStatusChangedV1(
    Guid DonorId,
    string DonorNumber,
    string PreviousStatus,
    string CurrentStatus,
    Guid OrganisationId,
    DateTimeOffset OccurredAtUtc);
