namespace YDots.DON.Application.Common.Abstractions.Services;

/// <summary>
/// Produces the stable business references: DON-2026-000184, LED-2026-000317 and friends.
///
/// UI section 5.3: "Generate once and display consistently in header, confirmation, history and
/// support context. Never create a replacement reference because a dependency retry failed."
/// </summary>
public interface IReferenceNumberGenerator
{
    Task<string> NextDonorNumberAsync(CancellationToken cancellationToken = default);

    Task<string> NextLeadReferenceAsync(CancellationToken cancellationToken = default);

    Task<string> NextMergeCaseReferenceAsync(CancellationToken cancellationToken = default);

    Task<string> NextVerificationReferenceAsync(CancellationToken cancellationToken = default);

    Task<string> NextFollowUpReferenceAsync(CancellationToken cancellationToken = default);
}
