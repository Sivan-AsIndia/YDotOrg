using Microsoft.Extensions.Options;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Settings;

namespace YDots.DON.Infrastructure.Services;

/// <summary>
/// Produces the stable business references: DON-2026-000184, LED-2026-000317 and so on.
///
/// The next number is the highest one used this year plus one. That is simple and readable, and
/// it is backed by a unique index on each reference column: if two requests race and both pick
/// the same number, the second INSERT fails rather than producing two records with one reference.
/// </summary>
public sealed class ReferenceNumberGenerator(
    IDonorRepository donorRepository,
    ILeadRepository leadRepository,
    IDonorMergeCaseRepository mergeCaseRepository,
    IVerificationRepository verificationRepository,
    IFollowUpRepository followUpRepository,
    IDateTimeProvider clock,
    IOptions<DonorSettings> donorSettings) : IReferenceNumberGenerator
{
    private readonly DonorSettings _settings = donorSettings.Value;

    public async Task<string> NextDonorNumberAsync(CancellationToken cancellationToken = default)
    {
        var year = clock.UtcNow.Year;
        var sequence = await donorRepository.GetMaxNumberSequenceAsync(year, cancellationToken);

        return Format(_settings.DonorNumberPrefix, year, sequence + 1);
    }

    public async Task<string> NextLeadReferenceAsync(CancellationToken cancellationToken = default)
    {
        var year = clock.UtcNow.Year;
        var sequence = await leadRepository.GetMaxReferenceSequenceAsync(year, cancellationToken);

        return Format(_settings.LeadReferencePrefix, year, sequence + 1);
    }

    public async Task<string> NextMergeCaseReferenceAsync(CancellationToken cancellationToken = default)
    {
        var year = clock.UtcNow.Year;
        var sequence = await mergeCaseRepository.GetMaxReferenceSequenceAsync(year, cancellationToken);

        return Format(_settings.MergeCaseReferencePrefix, year, sequence + 1);
    }

    public async Task<string> NextVerificationReferenceAsync(CancellationToken cancellationToken = default)
    {
        var year = clock.UtcNow.Year;
        var sequence = await verificationRepository.GetMaxReferenceSequenceAsync(year, cancellationToken);

        return Format(_settings.VerificationReferencePrefix, year, sequence + 1);
    }

    public async Task<string> NextFollowUpReferenceAsync(CancellationToken cancellationToken = default)
    {
        var year = clock.UtcNow.Year;
        var sequence = await followUpRepository.GetMaxReferenceSequenceAsync(year, cancellationToken);

        return Format(_settings.FollowUpReferencePrefix, year, sequence + 1);
    }

    private static string Format(string prefix, int year, int sequence) =>
        $"{prefix}-{year:0000}-{sequence:000000}";
}
