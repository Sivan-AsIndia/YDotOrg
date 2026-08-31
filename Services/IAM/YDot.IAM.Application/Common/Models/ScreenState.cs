namespace YDot.IAM.Application.Common.Models;

/// <summary>
/// What a screen should render before it has any rows: loading, empty, no-access or error.
///
/// Returning this from the query rather than letting the client infer it means "you have no
/// users yet" and "you are not allowed to see the users" look different to the person, which
/// matters because the remedies are completely different — and because an empty grid is a
/// terrible way to communicate a permission problem.
/// </summary>
public enum ScreenState
{
    Ready = 0,
    Empty = 1,
    NoAccess = 2,
    Error = 3
}
