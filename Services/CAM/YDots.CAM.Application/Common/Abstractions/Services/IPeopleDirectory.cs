namespace YDots.CAM.Application.Common.Abstractions.Services;

/// <summary>
/// Resolves the people CAM names but does not own: campaign owners, readiness check owners and
/// blocker owners all carry an IAM user id.
///
/// WHY THIS EXISTS. CAM stores an owner id and never checked that it belonged to anybody, so a
/// campaign could be created naming a user who does not exist. Ownership drives approval routing
/// and readiness assignment, which means an unresolvable owner leaves a campaign nobody is
/// accountable for and nobody to notify.
///
/// READ-ONLY, like <see cref="IFinancialDirectory"/>, and for the same reason: CAM never writes a
/// user. If identity ever moves to a database of its own, this one interface is what changes.
/// </summary>
public interface IPeopleDirectory
{
    /// <summary>
    /// Of the ids given, returns those that are a real, non-deleted user inside this organisation.
    ///
    /// The caller compares the answer against what it asked for, so a caller can name exactly which
    /// id was wrong rather than saying "one of these is invalid".
    /// </summary>
    Task<IReadOnlySet<Guid>> GetExistingUserIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
}
