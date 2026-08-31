namespace YDots.CAM.Application.Common.Abstractions.Services;

/// <summary>
/// The clock, behind an interface.
///
/// NAMED <c>IDateTimeProvider</c> TO MATCH IAM AND DON. CAM called it <c>IClock</c>, which is a
/// perfectly good name but a different one - and three services sharing a database and a token
/// while naming the same abstraction three ways is the kind of drift that makes a developer
/// moving between them second-guess which is which.
///
/// Everything is UTC. A campaign start date means the same instant whichever server evaluates
/// it, and a local time on a shared database is a bug waiting for a deployment to another
/// region.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }

    DateOnly TodayUtc { get; }
}
