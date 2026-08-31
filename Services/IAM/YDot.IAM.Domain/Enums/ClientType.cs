namespace YDot.IAM.Domain.Enums;

/// <summary>
/// Which kind of client presented the credentials. Captured on every sign-in attempt and
/// written into the token as <c>client_type</c>, so a session opened on a phone can be
/// listed, recognised and revoked separately from a browser session.
/// </summary>
public enum ClientType
{
    Unknown = 0,
    Web = 1,
    Mobile = 2,
    Desktop = 3,
    Api = 4
}
