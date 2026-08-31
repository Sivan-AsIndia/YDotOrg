using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Abstractions.Services;

/// <summary>
/// Turns a raw User-Agent string into the fields the brief asks to capture: client type,
/// browser and operating system.
///
/// Deliberately approximate. User-Agent strings are famously unreliable and increasingly
/// frozen by browsers, so this produces something readable for the sessions screen and is
/// never used for an authorisation decision.
/// </summary>
public interface IUserAgentParser
{
    ClientInfo Parse(string? userAgent, string? clientTypeHeader = null);
}

/// <summary>What could be read from the user agent.</summary>
public sealed record ClientInfo(
    ClientType ClientType,
    string? Browser,
    string? OperatingSystem,
    string? DeviceName)
{
    public static ClientInfo Unknown => new(ClientType.Unknown, null, null, null);
}
