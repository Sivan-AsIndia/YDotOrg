using YDots.DON.Domain.Entities;

namespace YDots.DON.Application.Common.Abstractions.Persistence;

/// <summary>
/// Reads one person's conversations, whichever side of conversion they were recorded on.
///
/// SEPARATE FROM <c>IDonorRepository.GetInteractionsAsync</c>, which asks only "what did we say to
/// this donor". The Communication Timeline has to answer "what did we say to this PERSON", and
/// the answer spans two ids: interactions recorded while they were a lead carry the lead id, and
/// those recorded afterwards carry the donor id. Merging is the whole job.
/// </summary>
public interface IInteractionTimelineReader
{
    /// <summary>
    /// Every interaction against either id, newest first.
    ///
    /// EITHER MAY BE NULL. A lead that never converted has no donor id, and a donor created
    /// directly by a donation has no lead - both are ordinary.
    /// </summary>
    Task<IReadOnlyList<DonorInteraction>> GetTimelineAsync(
        Guid? leadId,
        Guid? donorId,
        int maximumRows,
        CancellationToken cancellationToken = default);
}
