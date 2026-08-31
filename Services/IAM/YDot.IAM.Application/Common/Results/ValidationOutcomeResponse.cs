namespace YDot.IAM.Application.Common.Results;

/// <summary>
/// Result of a dry run: "would this work?", answered without writing anything.
///
/// Used by the bulk screens and the subdomain checker, where finding out after the fact is
/// expensive or embarrassing.
/// </summary>
public sealed record ValidationOutcomeResponse(
    bool IsValid,
    IReadOnlyList<ValidationError> Errors,
    IReadOnlyList<string> Warnings,
    string? Message = null)
{
    public static ValidationOutcomeResponse Valid(string? message = null) =>
        new(true, [], [], message);

    public static ValidationOutcomeResponse Invalid(IReadOnlyList<ValidationError> errors, string? message = null) =>
        new(false, errors, [], message);
}
