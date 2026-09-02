namespace YDots.CAM.Application.Common.Abstractions.Services;

/// <summary>
/// Enough of an IAM user to draw them: the code somebody quotes, and the name they read.
///
/// DELIBERATELY THIN. CAM shows who owns a campaign and who a blocker is assigned to; it has no
/// business holding e-mail addresses, phone numbers or anything else identity owns.
/// </summary>
public sealed record PersonSummary(Guid UserId, string? UserCode, string? DisplayName);

/// <summary>
/// Resolves the people CAM names but does not own: campaign owners, readiness check owners and
/// blocker owners all carry an IAM user id.
///
/// WHY THIS EXISTS. CAM stores an owner id and never checked that it belonged to anybody, so a
/// campaign could be created naming a user who does not exist. Ownership drives approval routing
/// and readiness assignment, which means an unresolvable owner leaves a campaign nobody is
/// accountable for and nobody to notify.
///
/// IT ALSO ANSWERS "WHAT IS THIS PERSON CALLED", which is what the register's owner column and
/// the readiness blocker popup need. Without it the API returned bare Guids and every screen
/// showed "Unassigned" against records that had an owner all along.
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

    /// <summary>
    /// The display names for a set of user ids, keyed by id.
    ///
    /// TAKES A SET, NOT AN ID, for the reason every other directory method here does: a register
    /// of twenty campaigns references a handful of owners, and asking per row would be twenty
    /// queries to draw one grid.
    ///
    /// AN ID THAT RESOLVES TO NOTHING IS SIMPLY ABSENT from the result rather than throwing. A
    /// name is decoration - a screen missing one should show the id, not fail to load - which is
    /// the opposite of <see cref="GetExistingUserIdsAsync"/>, whose whole job is to refuse.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, PersonSummary>> GetPeopleAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
}
